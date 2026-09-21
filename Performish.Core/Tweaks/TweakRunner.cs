using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Performish.Core.Backup;
using Performish.Core.Models;

namespace Performish.Core.Tweaks
{
    public sealed class TweakRunResult
    {
        public TweakDefinition Tweak { get; set; }
        public TweakOperationResult Result { get; set; }
        public Exception Exception { get; set; }

        public bool Failed => Exception != null || Result?.Outcome == OperationOutcome.Failed;
    }

    public sealed class BatchRunResult
    {
        public bool RestorePointCreated { get; set; }
        public bool RestorePointAttempted { get; set; }
        public bool Cancelled { get; set; }
        public List<TweakRunResult> Results { get; } = new List<TweakRunResult>();
    }

    /// <summary>Runs a batch of tweaks (apply or undo) with per-tweak error isolation - "per-tweak
    /// error handling so one failure never aborts the batch" - logging every outcome to the change
    /// log, and optionally creating a System Restore point first for an apply batch that includes
    /// any Moderate/Advanced tweak.</summary>
    public sealed class TweakRunner
    {
        private readonly IChangeLogStore _changeLog;
        private readonly IRestorePointBackend _restorePoint;

        public TweakRunner(IChangeLogStore changeLog, IRestorePointBackend restorePoint)
        {
            _changeLog = changeLog;
            _restorePoint = restorePoint;
        }

        public BatchRunResult ApplyBatch(IEnumerable<TweakDefinition> tweaks, TweakExecutionContext ctx, bool createRestorePoint,
            CancellationToken cancellationToken = default)
        {
            var list = tweaks.ToList();
            var batchResult = new BatchRunResult();

            if (createRestorePoint && !ctx.DryRun)
            {
                batchResult.RestorePointAttempted = true;
                batchResult.RestorePointCreated = _restorePoint.TryCreate($"Performish - before applying {list.Count} tweak(s)");
                ctx.Log(batchResult.RestorePointCreated
                    ? "Restore point created."
                    : "Restore point could not be created (System Protection may be off) - continuing anyway.");
            }

            // Tweaks apply strictly one at a time, in the order given, deliberately never in
            // parallel and never reordered - some tweaks have implicit ordering/reboot dependencies
            // (e.g. a service disabled by one tweak, queried by another's Check()), and the change
            // log's meaning depends on entries being written in true application order. See
            // DECISIONS.md "tweak application stays sequential".
            foreach (var tweak in list)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Cancellation only ever takes effect BETWEEN tweaks, never mid-Apply/Undo - a
                    // tweak that has started always finishes and is logged, so the change log and
                    // undo snapshots stay consistent with what the system actually has applied.
                    batchResult.Cancelled = true;
                    ctx.Log($"Cancelled - {list.Count - batchResult.Results.Count} tweak(s) not started.");
                    break;
                }
                batchResult.Results.Add(RunOne(tweak, ctx, ChangeLogAction.Apply));
            }

            return batchResult;
        }

        public BatchRunResult UndoBatch(IEnumerable<TweakDefinition> tweaks, TweakExecutionContext ctx,
            CancellationToken cancellationToken = default)
        {
            var list = tweaks.ToList();
            var batchResult = new BatchRunResult();
            foreach (var tweak in list)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    batchResult.Cancelled = true;
                    ctx.Log($"Cancelled - {list.Count - batchResult.Results.Count} tweak(s) not started.");
                    break;
                }
                batchResult.Results.Add(RunOne(tweak, ctx, ChangeLogAction.Undo));
            }
            return batchResult;
        }

        /// <summary>"One-click revert everything": undoes every tweak whose most recent change-log
        /// entry is a successful, non-dry-run Apply that hasn't since been undone.</summary>
        public BatchRunResult RevertEverything(TweakRegistry registry, TweakExecutionContext ctx,
            CancellationToken cancellationToken = default)
        {
            var appliedIds = CurrentlyAppliedTweakIds();
            var toUndo = appliedIds.Select(registry.Find).Where(t => t != null).ToList();
            return UndoBatch(toUndo, ctx, cancellationToken);
        }

        /// <summary>Finds "drift": tweaks the change log says were successfully, really (non-dry-run)
        /// applied, but whose Check() now reports NotApplied against the live system - meaning a
        /// Windows Update, a Group Policy refresh, or something else reverted it without Performish's
        /// knowledge. Read-only (Check() never writes); safe to call any time, dry-run or not.</summary>
        public List<TweakDefinition> DetectDrift(TweakRegistry registry, TweakExecutionContext ctx)
        {
            var appliedIds = CurrentlyAppliedTweakIds();
            var drifted = new List<TweakDefinition>();

            foreach (var id in appliedIds)
            {
                var tweak = registry.Find(id);
                if (tweak == null) continue; // tweak removed from a later build's library - nothing to reapply

                TweakState state;
                try { state = tweak.Check(ctx); }
                catch { continue; } // a Check() failure isn't drift - don't claim to know the state

                if (state == TweakState.NotApplied)
                    drifted.Add(tweak);
            }

            return drifted;
        }

        public List<string> CurrentlyAppliedTweakIds()
        {
            var latestByTweak = _changeLog.ReadAll()
                .GroupBy(e => e.TweakId)
                .Select(g => g.OrderBy(e => e.TimestampUtc).Last());

            return latestByTweak
                .Where(e => e.Action == ChangeLogAction.Apply && e.Outcome == OperationOutcome.Success && !e.DryRun)
                .Select(e => e.TweakId)
                .ToList();
        }

        private TweakRunResult RunOne(TweakDefinition tweak, TweakExecutionContext ctx, ChangeLogAction action)
        {
            var run = new TweakRunResult { Tweak = tweak };
            try
            {
                ctx.Log($"{(action == ChangeLogAction.Apply ? "Applying" : "Undoing")}: [{tweak.Id}] {tweak.Title}");
                var result = action == ChangeLogAction.Apply ? tweak.Apply(ctx) : tweak.Undo(ctx);
                run.Result = result;

                _changeLog.Append(new ChangeLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    TweakId = tweak.Id,
                    TweakName = tweak.Title,
                    Action = action,
                    Outcome = result.Outcome,
                    Message = result.Message,
                    DryRun = result.WasDryRun
                });

                // Always names the tweak id, not just its display name - "reported with its ID and
                // reason" (see DECISIONS.md / this fix's regression tests). A failed/Skipped result
                // here never throws past this point - the batch (TweakRunner.ApplyBatch/UndoBatch)
                // continues to the next tweak regardless.
                ctx.Log($"  -> [{tweak.Id}] {result.Outcome}{(result.WasDryRun ? " (dry run)" : "")}: {result.Message}");
            }
            catch (Exception ex)
            {
                run.Exception = ex;
                run.Result = TweakOperationResult.Failed($"Unhandled error: {ex.Message}");
                _changeLog.Append(new ChangeLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    TweakId = tweak.Id,
                    TweakName = tweak.Title,
                    Action = action,
                    Outcome = OperationOutcome.Failed,
                    Message = ex.Message,
                    DryRun = ctx.DryRun
                });
                ctx.Log($"  -> [{tweak.Id}] Failed: {ex.Message}");
            }

            return run;
        }
    }
}
