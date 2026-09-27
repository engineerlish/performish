using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Performish.Core.Hardware;
using Xunit;

namespace Performish.Tests
{
    /// <summary>The hardware-visibility feature is read-only by rule (FEASIBILITY_BIOS_OVERCLOCK.md): no
    /// BIOS/UEFI writes, no clock/voltage/power-limit changes, no registry writes, no process launches.
    /// These tests check that structurally - the NVML entry-point allowlist, every P/Invoke declared in the
    /// hardware namespace, the backend interface's shape, and a scan of the feature's source for any API
    /// that can write.</summary>
    public class HardwareNoWritePathTests
    {
        private static readonly Regex WriteVerb = new Regex("(Set|Write|Reset|Apply|Put|Delete|Clear|Enable|Disable|Flash|Update)", RegexOptions.IgnoreCase);

        [Fact]
        public void NvmlAllowlist_ContainsOnlyQueriesPlusInitShutdown()
        {
            foreach (var name in NvmlReader.ImportedFunctions)
            {
                var isQuery = name.StartsWith("nvmlDeviceGet", StringComparison.Ordinal);
                var isLifecycle = name == "nvmlInit_v2" || name == "nvmlShutdown";
                Assert.True(isQuery || isLifecycle, $"{name} is not a read-only NVML query");
                Assert.DoesNotMatch(WriteVerb, name.Replace("nvmlDeviceGet", ""));
            }
        }

        [Fact]
        public void EveryPInvokeInTheHardwareNamespace_IsARead()
        {
            var imports = typeof(HardwareReport).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("Performish.Core.Hardware", StringComparison.Ordinal))
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null)
                .ToList();

            Assert.NotEmpty(imports);
            foreach (var m in imports)
            {
                var entry = m.GetCustomAttribute<DllImportAttribute>().EntryPoint ?? m.Name;
                Assert.StartsWith("Get", entry);
                Assert.DoesNotMatch(WriteVerb, entry);
            }
        }

        [Fact]
        public void BackendInterface_OnlyReads()
        {
            var methods = typeof(IHardwareInfoBackend).GetMethods();
            Assert.Single(methods);
            Assert.Equal("Read", methods[0].Name);
            Assert.Equal(typeof(HardwareRawSnapshot), methods[0].ReturnType);
        }

        [Fact]
        public void ReportModel_HasNoApplyActions()
        {
            foreach (var type in new[] { typeof(HardwareReport), typeof(HardwareFinding), typeof(HardwareReading), typeof(HardwareGroup) })
            {
                Assert.DoesNotContain(type.GetProperties(), p => typeof(Delegate).IsAssignableFrom(p.PropertyType));
                Assert.DoesNotContain(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                    m => !m.IsSpecialName && Regex.IsMatch(m.Name, "^(Apply|Set|Write|Change|Enable|Disable)"));
            }
        }

        // APIs that could change the machine. Any of these in the hardware feature's source fails the test.
        private static readonly (string Pattern, string Why)[] Forbidden =
        {
            (@"SetFirmwareEnvironmentVariable", "UEFI variable write"),
            (@"\bnvmlDeviceSet", "NVML setter"),
            (@"\bnvmlDeviceReset", "NVML reset"),
            (@"\.SetValue\s*\(", "registry write"),
            (@"CreateSubKey", "registry key creation"),
            (@"DeleteValue|DeleteSubKey", "registry delete"),
            (@"OpenSubKey\s*\([^)]*,\s*true\s*\)", "writable registry open"),
            (@"RegistryKeyPermissionCheck\.ReadWriteSubTree", "writable registry open"),
            (@"InvokeMethod", "WMI method call"),
            (@"\.Put\s*\(", "WMI instance write"),
            (@"Process\.Start|ProcessStartInfo|IProcessRunner", "process launch"),
            (@"\bshutdown(\.exe)?\s+/", "reboot"),
            (@"File\.(Write|Delete|Move|Copy)|Directory\.(Delete|Create)", "file-system write"),
            (@"DeviceIoControl|CreateFile\b", "raw driver access"),
            (@"WinRing0|PawnIO\.|inpout", "kernel driver use"),
        };

        [Fact]
        public void HardwareFeatureSource_ContainsNoWriteCapableApis()
        {
            var root = FindRepoRoot();
            var files = new[]
            {
                Path.Combine(root, "Performish.Core", "Hardware"),
                Path.Combine(root, "Performish", "Hardware"),
            }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToList();

            Assert.True(files.Count >= 5, "expected to find the hardware feature's source files");
            foreach (var file in files)
            {
                var code = StripComments(File.ReadAllText(file));
                foreach (var (pattern, why) in Forbidden)
                    Assert.False(Regex.IsMatch(code, pattern), $"{Path.GetFileName(file)} uses a write-capable API ({why}): /{pattern}/");
            }
        }

        [Fact]
        public void RegistryOpens_InHardwareBackend_AreAllReadOnlyOverloads()
        {
            var code = StripComments(File.ReadAllText(Path.Combine(FindRepoRoot(), "Performish.Core", "Hardware", "HardwareInfoBackend.cs")));
            foreach (Match m in Regex.Matches(code, @"OpenSubKey\s*\(([^()]|\([^()]*\))*\)"))
                Assert.DoesNotContain(",", m.Value.Substring(m.Value.IndexOf('(')));
        }

        private static string StripComments(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(code, @"//[^\n]*", "");
        }

        /// <summary>Walks up from this source file's compile-time location (works from any test runner),
        /// falling back to the test binary's folder.</summary>
        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        {
            foreach (var start in new[] { Path.GetDirectoryName(sourceFile), AppContext.BaseDirectory })
            {
                var d = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "Performish.slnx"))) d = d.Parent;
                if (d != null) return d.FullName;
            }
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Performish.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
