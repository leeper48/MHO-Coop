// Progress window shown while the games start. Once they are running it hides and leaves an icon
// near the clock until both games close.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MhoCoop
{
    internal sealed class LaunchForm : Form
    {
        private readonly Session session;
        private readonly Label title, detail;
        private readonly Button button;
        private readonly NotifyIcon tray;
        private Thread worker;
        private bool failed;

        public LaunchForm(Config cfg)
        {
            Theme.StyleDialog(this, "MHO Couch Co-op", 520, 200);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimizeBox = true;
            TopMost = true;

            title = Theme.MakeLabel("Getting ready", false);
            title.Font = Theme.Ui(15f, true);
            title.ForeColor = Theme.Title;
            Theme.Put(this, title, 22, 20, 0, 0);

            Panel bar = Theme.Put(this, new Panel(), 24, 60, 64, 3);
            bar.BackColor = Theme.Accent;

            detail = Theme.MakeLabel("Gamepad " + cfg.LeftPad + " on the left, gamepad " + cfg.RightPad + " on the right.", true);
            detail.AutoSize = false;
            detail.Font = Theme.Ui(10f, false);
            Theme.Put(this, detail, 24, 76, 472, 70);

            button = Theme.Put(this, Theme.MakeButton("Cancel", false), 404, 152, 92, 32);
            button.Click += delegate { ButtonClicked(); };

            session = new Session(cfg, Report);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Bring both games to the front", null, delegate { session.ShowBoth(cfg.LeftPad); });
            menu.Items.Add("Open the log", null, delegate { OpenLog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit (the games keep running)", null, delegate { session.Stop(); });
            tray = new NotifyIcon();
            tray.Icon = Theme.AppIcon;
            tray.Text = "MHO Couch Co-op";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { session.ShowBoth(cfg.LeftPad); };
        }

        private static void OpenLog()
        {
            try { if (File.Exists(Config.LogPath)) Process.Start(Config.LogPath); }
            catch (Exception) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            worker = new Thread(Work);
            worker.IsBackground = true;
            worker.Name = "session";
            worker.Start();
        }

        // Runs on the worker thread.
        private void Work()
        {
            bool ok = session.Launch();
            Post(delegate { AfterLaunch(ok); });
            if (!ok) return;
            session.Watch();
            Post(delegate { Close(); });
        }

        private void Post(MethodInvoker action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void Report(string heading, string text)
        {
            Post(delegate
            {
                title.Text = heading;
                detail.Text = text;
            });
        }

        private void AfterLaunch(bool ok)
        {
            if (ok)
            {
                tray.Visible = true;             // games are up: this window hides, the icon stays
                ShowInTaskbar = false;
                Hide();
                return;
            }
            if (session.Cancelled) { Close(); return; }
            failed = true;
            title.Text = "Could not finish starting";
            title.ForeColor = Theme.Warning;
            detail.Text = session.Error;
            detail.ForeColor = Theme.Text;
            button.Text = "Close";
            Height += Theme.S(40);
            detail.Height += Theme.S(40);
            button.Top += Theme.S(40);
        }

        private void ButtonClicked()
        {
            if (failed) { Close(); return; }
            session.Cancel();
            title.Text = "Cancelling";
            detail.Text = "A game that has already started stays open.";
            button.Enabled = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            session.Cancel();
            session.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }
}
