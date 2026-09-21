namespace Performish.Core.Tweaks
{
    /// <summary>The set of things Performish will never write a tweak for, let alone include in a preset -
    /// "Protected list never touched by default: Windows Update core, Defender, networking, audio,
    /// drivers, Store". This class exists so the UI and README can show the list explicitly and
    /// auditably, rather than the protection being implicit-by-omission from the tweak libraries.
    /// None of Performish.Core.Tweaks.*Tweaks ever defines a TweakDefinition that disables, removes, or
    /// deprovisions anything named here.</summary>
    public static class ProtectedList
    {
        public static readonly string[] Services =
        {
            "wuauserv",      // Windows Update
            "UsoSvc",        // Update Orchestrator
            "WaaSMedicSvc",  // Windows Update Medic
            "WinDefend",     // Microsoft Defender Antivirus
            "SecurityHealthService",
            "wscsvc",        // Security Center
            "MpsSvc",        // Windows Defender Firewall
            "Dnscache",      // DNS Client
            "Dhcp",
            "NlaSvc",        // Network Location Awareness
            "netprofm",
            "AudioSrv",      // Windows Audio
            "AudioEndpointBuilder",
            "PlugPlay",
            "DcomLaunch",
            "RpcSs",
            "BFE",           // Base Filtering Engine (firewall)
        };

        public static readonly string[] AppxPackagePrefixes =
        {
            "Microsoft.WindowsStore",
            "Microsoft.StorePurchaseApp",
            "Microsoft.DesktopAppInstaller", // winget
            "Microsoft.SecHealthUI",         // Defender Security Center UI
            "Microsoft.VCLibs",
            "Microsoft.NET.Native",
            "Microsoft.UI.Xaml",
            "Microsoft.WindowsNotepad",
            "Microsoft.Paint",
            "Microsoft.ScreenSketch",        // Snipping Tool
            "Microsoft.WindowsCalculator",
            "Microsoft.WindowsCamera",
        };

        public static readonly string[] Reasons =
        {
            "Windows Update core + Defender + networking + audio + firewall services are excluded " +
            "from every performance tweak - disabling them trades stability/security for savings " +
            "that are not worth it on any workstation.",
            "Store, winget, and core inbox utilities (Notepad, Paint, Snipping Tool, Calculator, " +
            "Camera) plus their shared runtime frameworks (VCLibs/NET.Native/UI.Xaml, which other " +
            "apps depend on) are excluded from every debloat tweak."
        };
    }
}
