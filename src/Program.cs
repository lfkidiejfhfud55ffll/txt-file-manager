using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TxtManager
{
    internal static class Program
    {
        internal static string AutoShotPath;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0
                && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(SelfTest.Run(args));
                return;
            }

            if (args != null && args.Length > 1
                && string.Equals(args[0], "--shot", StringComparison.OrdinalIgnoreCase))
            {
                AutoShotPath = args[1];
            }

            try { SetProcessDPIAware(); }
            catch (Exception) { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
