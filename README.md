# Performish

Performish is a Windows 11 debloat and performance-tuning tool. It helps
you clean up unnecessary components, disable bloatware, and tune your
workstation or gaming PC for better performance and efficiency.

## What it does

- **System scan** - reads your machine's hardware, software, and current
  settings so you know what's on it before changing anything.
- **Health score** - a 0-100 score for how "clean" your system currently
  is, with a full breakdown of every factor behind it: what's being
  measured, how many points it earned, and plain-language guidance on
  what to do about it - including when something is outside what
  Performish (or any software) can change directly.
- **Startup item review** - see everything set to launch automatically
  when you sign in and choose which ones to stop, without uninstalling
  anything - each one can still be launched manually afterward, and the
  change is fully undoable.
- **Plain-language options** - every change has a short, clear title
  describing what it does, with the full explanation (what changes, why
  it helps, risk level, whether a reboot or admin rights are needed, and
  what undoing it does) one click away.
- **Debloat options** - remove or disable unwanted preinstalled apps,
  background services, ads and suggestions, and other clutter, with a
  clear description and risk level for each one.
- **Performance and gaming tuning** - power plan, visual effects,
  background app behavior, and gaming-focused settings aimed at smoother,
  more responsive performance.
- **Maintenance tools** - free up disk space by clearing update, cache,
  and temporary files.
- **Presets** - Conservative, Balanced, and Aggressive presets for
  different comfort levels, or pick individual changes yourself.
- **Dry-run preview** - see exactly what a change will do before it's
  applied.
- **Undo and revert** - undo an individual change or revert everything
  in one click.
- **One window** - browsing tweaks, choosing a preset, confirming, and
  reading the results all happen inside the main window: a paged grid
  of tweak cards with details underneath, a small confirmation card
  before anything is applied, and no pop-up windows for those steps.
- **Clear results** - after applying changes, see at a glance what
  succeeded, what failed, and what was skipped, with failures shown
  first, a short reason for each, and the option to retry.
- **Change history** - a running log of what's been changed and when.
- **Drift detection** - checks whether anything Performish set up has
  been reverted by Windows or another program, and offers to reapply it.
- **Real before/after benchmarking** - measures your machine (CPU idle,
  available memory, disk speed, and more) before and after applying
  changes and shows exactly what improved, what got worse, and what
  stayed flat, in real numbers with their measurement variance shown
  alongside - never an estimate. Works for a single change, a preset, a
  custom selection, or on its own as a general checkpoint. Every run is
  saved so you can compare any two checkpoints later, not just the most
  recent, and results can be exported alongside the rest of a report.
- **Command-line mode** - run presets unattended from a script or
  management tool, with the same preview/report/benchmark options.
- **Settings** - control whether a System Restore point is created
  before applying and whether benchmarking includes network metrics.

## Requirements

- Windows 11
- Administrator access (most changes require it)

## Getting started

1. Download or build the Performish executable. To build from source:
   `dotnet build Performish.slnx -c Release` (requires the .NET SDK
   matching the target framework in `Performish/Performish.csproj`).
2. Run it as Administrator.
3. Run a scan to see your system's current state.
4. Open Tweaks (F2), choose a preset or pick individual tweaks, review
   them, and apply. Keep Dry run on to preview first.

## Safety

Every change can be previewed before it's applied and undone afterward,
either individually or all at once. As with any system-tuning tool, back
up anything important before making changes, and review what a change
does before applying it.
