using System;
using System.Windows.Forms;
using Performish.Core;
using Performish.Core.Cli;

namespace Performish
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Headless/CLI mode for RMM/scripted use (see ROADMAP.md "IT and fleet use") - any
            // argument at all skips the WinForms UI entirely. No args: normal graphical launch,
            // unchanged.
            if (args.Length > 0)
                return RunCli(args);

            ApplicationConfiguration.Initialize();

            // One-shot tool (see CONVENTIONS.md "Tray shell vs. one-shot"): scan, review, apply,
            // see the result, close. No tray shell.
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunCli(string[] args)
        {
            var options = CliOptionsParser.Parse(args);
            var services = AppServices.BuildReal();
            var runner = new CliRunner();
            return runner.Run(options, services, Console.WriteLine);
        }
    }
}
