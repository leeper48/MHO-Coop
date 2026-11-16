// "A new version is available" window: release notes, and Update and Restart / Later / Skip This Version /
// Release Page. Laid out like the one in MHO Extended Mod Manager.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MhoCoop
{
    internal sealed class UpdateForm : Form
    {
        private readonly Config cfg;
        private readonly ReleaseInfo release;
        private readonly Label status;
        private readonly Button update, later, skip, page;
        private bool busy;

        // True when the update was installed and the new program has been started: the caller must close.
        public bool Restarting;

        public UpdateForm(Config config, ReleaseInfo info)
        {
            cfg = config;
            release = info;
            Theme.StyleDialog(this, "Update", 600, 430);

            Label heading = Theme.MakeLabel("MHO Couch Co-op " + info.Version + " is available (you have " + Updater.Current + ").", false);
            heading.Font = Theme.Ui(11.5f, true);
            heading.ForeColor = Theme.Title;
            Theme.Put(this, heading, 20, 18, 0, 0);

            Panel frame = Theme.Put(this, new Panel(), 20, 54, 560, 270);
            frame.BackColor = Theme.Border;
            TextBox notes = new TextBox();
            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.BorderStyle = BorderStyle.None;
            notes.BackColor = Theme.Well;
            notes.ForeColor = Theme.Text;
            notes.Font = Theme.Ui(10f, false);
            notes.Text = (info.Notes.Trim().Length > 0 ? info.Notes.Trim() : "No notes were written for this version.").Replace("\r\n", "\n").Replace("\n", "\r\n");
            notes.Location = new Point(1, 1);
            notes.Size = new Size(frame.Width - 2, frame.Height - 2);
            notes.TabStop = false;
            frame.Controls.Add(notes);

            status = Theme.MakeLabel("", true);
            status.AutoSize = false;
            Theme.Put(this, status, 20, 334, 560, 40);

            update = Theme.Put(this, Theme.MakeButton("Update and Restart", true), 416, 382, 164, 32);
            later = Theme.Put(this, Theme.MakeButton("Later", false), 316, 382, 92, 32);
            skip = Theme.Put(this, Theme.MakeButton("Skip This Version", false), 156, 382, 152, 32);
            page = Theme.Put(this, Theme.MakeButton("Release Page", false), 20, 382, 128, 32);

            update.Click += delegate { StartUpdate(); };
            later.Click += delegate { Close(); };
            skip.Click += delegate
            {
                cfg.SkipVersion = release.Version.ToString();
                cfg.Save();
                Close();
            };
            page.Click += delegate
            {
                try { if (Updater.Trusted(release.PageUrl) && release.PageUrl.StartsWith("https://")) Process.Start(release.PageUrl); }
                catch (Exception) { }
            };
            AcceptButton = update;
            CancelButton = later;
            Shown += delegate { update.Focus(); };
        }

        private void StartUpdate()
        {
            if (busy) return;
            busy = true;
            update.Enabled = later.Enabled = skip.Enabled = false;
            status.ForeColor = Theme.Secondary;
            Thread worker = new Thread(delegate()
            {
                string error;
                bool ok = Updater.Install(release, Say, out error);
                Post(delegate { Finished(ok, error); });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Say(string text)
        {
            Post(delegate { status.Text = text; });
        }

        private void Post(MethodInvoker action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void Finished(bool ok, string error)
        {
            busy = false;
            if (!ok)
            {
                status.ForeColor = Theme.Warning;
                status.Text = "Not Updated: " + error;
                update.Enabled = later.Enabled = skip.Enabled = true;
                return;
            }
            try
            {
                Updater.Restart();
                Restarting = true;
                Close();
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Warning;
                status.Text = "Updated, but the new version could not be started (" + ex.Message + "). Close this app and start it again.";
                later.Enabled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) e.Cancel = true;            // never abandon a half-done install
            base.OnFormClosing(e);
        }
    }
}
