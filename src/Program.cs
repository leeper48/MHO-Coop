// MHO Couch Co-op: two Marvel Heroes Omega games side by side, one gamepad locked to each.
// Needs ProtoInput (a separate download) for the gamepad locking. Nothing else.
//
// Build: powershell -File build.ps1   (uses the C# compiler that ships with Windows, so the code stays C# 5)
using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("MHO Couch Co-op")]
[assembly: AssemblyProduct("MHO Couch Co-op")]
[assembly: AssemblyDescription("Two Marvel Heroes Omega games side by side, one gamepad each")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace MhoCoop
{
    internal static class Program
    {
        private const string AppName = "MHO Couch Co-op";

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool first;
            using (Mutex mutex = new Mutex(true, "MhoCoop.SingleInstance", out first))
            {
                // Started by the updater: the old copy is still closing, so wait for it instead of giving up.
                if (!first && Environment.GetEnvironmentVariable(Updater.RestartVariable) == "1")
                {
                    try { first = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { first = true; }
                }
                if (!first)
                {
                    MessageBox.Show(AppName + " is already running. Look for its icon near the clock.", AppName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }
                try { return Run(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Something unexpected went wrong:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }


        private static int Run()
        {
            Config cfg = Config.Load();
            Updater.CleanUp();                    // remove what the last update left behind

            // Asked once, before the app ever contacts GitHub.
            if (Updater.Available && !cfg.UpdateCheckAsked)
            {
                DialogResult answer = MessageBox.Show(
                    "Look for new versions of " + AppName + " when it starts?\n\n" +
                    "It asks GitHub (api.github.com) which version is the newest. Nothing about you, your games or your files is sent.\n\n" +
                    "You can change this later in Settings.",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                cfg.CheckUpdates = answer == DialogResult.Yes;
                cfg.UpdateCheckAsked = true;
                cfg.Save();
            }

            using (PickerForm menu = new PickerForm(cfg))
            {
                if (menu.ShowDialog() != DialogResult.OK) return 0;
            }

            Application.Run(new LaunchForm(cfg));
            return 0;
        }
    }
}