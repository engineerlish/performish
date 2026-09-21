using System.Security.Principal;

namespace Performish.Core
{
    /// <summary>Whether the current process is elevated. Performish requests admin via its application
    /// manifest (see DECISIONS.md) rather than a runtime relaunch, so by the time any app code runs
    /// this is already decided - this is a read for the UI to display, not a gate to act on.</summary>
    public static class Elevation
    {
        public static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
