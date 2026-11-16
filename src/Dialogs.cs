// The Settings dialog.
using System;
using System.IO;
using System.Windows.Forms;

namespace MhoCoop
{
    // ---- settings ----
    internal sealed class SettingsForm : Form
    {
        private readonly Config cfg;
        private readonly TextBox gameBox, protoBox, serverBox, leftArgsBox, rightArgsBox;
        private readonly Label gameNote, protoNote;
        private readonly NumericUpDown leftPad, rightPad, loadWait;
        private readonly CheckBox borderless, wheel, checkUpdates;

        // True when an update was installed from here and the new program started: the caller must close.
        public bool Restarting;

        public SettingsForm(Config config)
        {
            cfg = config;
            Theme.StyleDialog(this, "Settings", 620, 596);

            Theme.Put(this, Theme.MakeLabel("Game program (MarvelHeroesOmega.exe)", true), 20, 18, 0, 0);
            gameBox = Theme.Put(this, Theme.MakeTextBox(), 20, 40, 480, 0);
            Button browseGame = Theme.Put(this, Theme.MakeButton("Browse", false), 508, 39, 92, 28);
            gameNote = Theme.Put(this, Theme.MakeLabel("", true), 20, 70, 0, 0);
            gameNote.Font = Theme.Ui(8.5f, false);

            Theme.Put(this, Theme.MakeLabel("ProtoInput folder", true), 20, 100, 0, 0);
            protoBox = Theme.Put(this, Theme.MakeTextBox(), 20, 122, 480, 0);
            Button browseProto = Theme.Put(this, Theme.MakeButton("Browse", false), 508, 121, 92, 28);
            protoNote = Theme.Put(this, Theme.MakeLabel("", true), 20, 152, 0, 0);
            protoNote.Font = Theme.Ui(8.5f, false);

            Theme.Put(this, Theme.MakeLabel("Server address", true), 20, 182, 0, 0);
            serverBox = Theme.Put(this, Theme.MakeTextBox(), 20, 204, 580, 0);

            Theme.Put(this, Theme.MakeLabel("Left game options", true), 20, 246, 0, 0);
            leftArgsBox = Theme.Put(this, Theme.MakeTextBox(), 20, 268, 285, 0);
            Theme.Put(this, Theme.MakeLabel("Right game options", true), 315, 246, 0, 0);
            rightArgsBox = Theme.Put(this, Theme.MakeTextBox(), 315, 268, 285, 0);

            Theme.Put(this, Theme.MakeLabel("Left game uses gamepad", false), 20, 318, 0, 0);
            leftPad = Theme.Put(this, Theme.MakeNumber(1, 4, cfg.LeftPad), 220, 314, 60, 0);
            Theme.Put(this, Theme.MakeLabel("Right game uses gamepad", false), 330, 318, 0, 0);
            rightPad = Theme.Put(this, Theme.MakeNumber(1, 4, cfg.RightPad), 540, 314, 60, 0);

            Theme.Put(this, Theme.MakeLabel("Seconds to let each game load", false), 20, 358, 0, 0);
            loadWait = Theme.Put(this, Theme.MakeNumber(3, 180, cfg.LoadWait), 220, 354, 60, 0);

            borderless = Theme.Put(this, Theme.MakeCheckBox("Remove the title bars (borderless)"), 20, 396, 0, 0);
            wheel = Theme.Put(this, Theme.MakeCheckBox("Mouse wheel only scrolls the game under the cursor"), 20, 424, 0, 0);

            Panel rule = Theme.Put(this, new Panel(), 20, 458, 580, 1);
            rule.BackColor = Theme.Border;
            checkUpdates = Theme.Put(this, Theme.MakeCheckBox("Look for a new version when the app starts"), 20, 474, 0, 0);
            Button checkNow = Theme.Put(this, Theme.MakeButton("Check Now", false), 492, 470, 108, 28);
            Label version = Theme.Put(this, Theme.MakeLabel("You have version " + Updater.Current + ".", true), 20, 502, 0, 0);
            version.Font = Theme.Ui(8.5f, false);
            checkUpdates.Checked = cfg.CheckUpdates;
            if (!Updater.Available)
            {
                checkUpdates.Enabled = false;
                checkNow.Enabled = false;
                version.Text = "You have version " + Updater.Current + ". This build does not update itself.";
            }
            checkNow.Click += delegate { CheckNow(); };

            Button save = Theme.Put(this, Theme.MakeButton("Save", true), 508, 546, 92, 32);
            Button cancel = Theme.Put(this, Theme.MakeButton("Cancel", false), 408, 546, 92, 32);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = save;
            CancelButton = cancel;

            gameBox.Text = cfg.GameExe;
            protoBox.Text = cfg.ProtoDir;
            serverBox.Text = cfg.Server;
            leftArgsBox.Text = cfg.LeftArgs;
            rightArgsBox.Text = cfg.RightArgs;
            borderless.Checked = cfg.Borderless;
            wheel.Checked = cfg.WheelPerWindow;

            gameBox.TextChanged += delegate { UpdateNotes(); };
            protoBox.TextChanged += delegate { UpdateNotes(); };
            browseGame.Click += delegate { BrowseGame(); };
            browseProto.Click += delegate { BrowseProto(); };
            save.Click += delegate { SaveClicked(); };
            UpdateNotes();
        }

        private void UpdateNotes()
        {
            bool gameOk = false;
            try { gameOk = File.Exists(gameBox.Text.Trim()); }
            catch (ArgumentException) { }
            gameNote.Text = gameOk ? "Found." : "This file does not exist.";
            gameNote.ForeColor = gameOk ? Theme.Good : Theme.Warning;

            bool protoOk = Proto.FolderLooksValid(protoBox.Text.Trim());
            protoNote.Text = protoOk ? "ProtoInput found." : "ProtoInputLoader64.dll and ProtoInputHooks64.dll are not in this folder.";
            protoNote.ForeColor = protoOk ? Theme.Good : Theme.Warning;
        }

        private void BrowseGame()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Find MarvelHeroesOmega.exe (in UnrealEngine3\\Binaries\\Win64)";
                d.Filter = "Game program|MarvelHeroesOmega.exe|Programs|*.exe";
                try
                {
                    string dir = Path.GetDirectoryName(gameBox.Text.Trim());
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
                }
                catch (ArgumentException) { }
                if (d.ShowDialog(this) == DialogResult.OK) gameBox.Text = d.FileName;
            }
        }

        // The regular file selector: pick any ProtoInput file and its folder is used.
        private void BrowseProto()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Find ProtoInput: select ProtoInputHost.exe in the folder where you unzipped it";
                d.Filter = "ProtoInput|ProtoInputHost.exe;ProtoInputLoader64.dll;ProtoInputHooks64.dll|Programs and libraries|*.exe;*.dll|All files|*.*";
                try
                {
                    if (Directory.Exists(protoBox.Text.Trim())) d.InitialDirectory = protoBox.Text.Trim();
                }
                catch (ArgumentException) { }
                if (d.ShowDialog(this) == DialogResult.OK) protoBox.Text = Path.GetDirectoryName(d.FileName);
            }
        }

        // A check by hand: works with automatic checks off, offers a skipped version again, and says what it found.
        private void CheckNow()
        {
            Cursor = Cursors.WaitCursor;
            string error;
            ReleaseInfo found = Updater.Check(out error);
            Cursor = Cursors.Default;
            if (error.Length > 0)
            {
                Theme.Warn(this, "Couldn't Check for Updates\n\n" + error);
                return;
            }
            cfg.LastUpdateCheck = DateTime.UtcNow;
            cfg.LatestKnownVersion = found != null ? found.Version.ToString() : "";
            cfg.Save();
            if (found == null || found.Version <= Updater.Current)
            {
                MessageBox.Show(this, "You have the latest version (" + Updater.Current + ").", "MHO Couch Co-op",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (UpdateForm f = new UpdateForm(cfg, found))
            {
                f.ShowDialog(this);
                if (f.Restarting)
                {
                    Restarting = true;
                    DialogResult = DialogResult.Cancel;
                }
            }
        }

        private void SaveClicked()
        {
            if (leftPad.Value == rightPad.Value)
            {
                Theme.Warn(this, "The left and right games need different gamepads, or one pad will drive both.");
                return;
            }
            cfg.GameExe = gameBox.Text.Trim();
            cfg.ProtoDir = protoBox.Text.Trim();
            cfg.Server = serverBox.Text.Trim();
            cfg.LeftArgs = leftArgsBox.Text.Trim();
            cfg.RightArgs = rightArgsBox.Text.Trim();
            cfg.LeftPad = (int)leftPad.Value;
            cfg.RightPad = (int)rightPad.Value;
            cfg.LoadWait = (int)loadWait.Value;
            cfg.Borderless = borderless.Checked;
            cfg.WheelPerWindow = wheel.Checked;
            if (Updater.Available)
            {
                cfg.CheckUpdates = checkUpdates.Checked;
                cfg.UpdateCheckAsked = true;
            }
            cfg.Save();
            DialogResult = DialogResult.OK;
        }
    }
}
