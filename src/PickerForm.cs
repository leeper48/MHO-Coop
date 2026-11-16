// The start menu: which gamepad drives which game, and Start. Each game then shows its own login screen.
// Everything is drawn by hand in "units" (pixels at 100% scaling) and scaled to the screen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace MhoCoop
{
    internal sealed class PickerForm : Form
    {
        private const int ColW = 300, GapCol = 14;
        private const int ColL = 24, ColR = ColL + ColW + GapCol;
        private const int TotalW = ColR + ColW + 24;
        private const int WellTop = 100, WellH = 132;
        private const int BottomY = WellTop + WellH + 16;
        private const string HintText =
            "Enter or A: start two games side by side.    Esc or B: cancel.\n" +
            "Each player logs in on the game's own login screen.";

        private sealed class Btn
        {
            public RectangleF R;
            public string Text = "";
            public int Kind;                      // 0 = main, 1 = plain, 2 = quiet (header), 3 = update available
            public MethodInvoker Click;
            public string Tip = "";               // shown when the mouse rests on the button
        }

        private readonly Config cfg;
        private float scale;                      // screen scaling, reduced when the menu would not fit the screen
        private readonly Font titleFont, labelFont, padFont, buttonFont, smallButtonFont, hintFont, stateFont;
        private readonly Timer padTimer;
        private readonly ToolTip tip = new ToolTip();
        private Btn tipFor;                       // button whose tip is showing now
        private string tipText = "";
        private Font tipFont;
        private readonly List<Btn> buttons = new List<Btn>();
        private string warning = "";
        private Btn hotButton, downButton;
        private int padPrevious = -1;
        private bool leftConnected, rightConnected, leftHeld, rightHeld;

        private ReleaseInfo offered;              // a newer version found by this session's check

        public bool Restarting;                   // an update was installed and the new program started: just close

        public PickerForm(Config config)
        {
            cfg = config;

            Text = "MHO Couch Co-op";
            Icon = Theme.AppIcon;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Theme.Window;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            titleFont = UnitFont("Segoe UI Semibold", 17f);
            labelFont = UnitFont("Segoe UI", 9f);
            padFont = UnitFont("Segoe UI Semibold", 18f);
            buttonFont = UnitFont("Segoe UI Semibold", 11f);
            smallButtonFont = UnitFont("Segoe UI Semibold", 9.5f);
            hintFont = UnitFont("Segoe UI", 9f);
            stateFont = UnitFont("Segoe UI", 10f);

            // Shrink the whole menu when it would not fit a small screen.
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            float roomWide = area.Width - 40f * Theme.Scale, roomTall = area.Height - 80f * Theme.Scale;
            scale = Math.Min(Theme.Scale, Math.Min(roomWide / TotalW, roomTall / (BottomY + 40 + 24)));
            ClientSize = new Size((int)Math.Round(TotalW * scale), (int)Math.Round((BottomY + 40 + 24) * scale));

            float bx = ColR + ColW - 112;
            AddButton("Start", 0, new RectangleF(bx, BottomY, 112, 40), StartClicked);
            AddButton("Cancel", 1, new RectangleF(bx - 124, BottomY, 112, 40), CancelClicked);
            AddButton("Settings", 2, new RectangleF(TotalW - 24 - 96, 22, 96, 30), OpenSettings);
            AddUpdateButton();

            // The hover note, drawn in the app's own colours and shown at once rather than faded in.
            tipFont = Theme.Ui(9.5f, false);
            tip.UseFading = false;
            tip.UseAnimation = false;
            tip.OwnerDraw = true;
            tip.Popup += delegate(object sender, PopupEventArgs e)
            {
                Size text = TextRenderer.MeasureText(tip.GetToolTip(this).Length > 0 ? tip.GetToolTip(this) : tipText, tipFont);
                e.ToolTipSize = new Size(text.Width + Theme.S(16), text.Height + Theme.S(10));
            };
            tip.Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                using (SolidBrush back = new SolidBrush(Theme.ButtonFace)) e.Graphics.FillRectangle(back, e.Bounds);
                using (Pen edge = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(edge, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, e.Bounds, Theme.Text,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            padTimer = new Timer();
            padTimer.Interval = 60;
            padTimer.Tick += PollPads;
            padTimer.Start();
        }

        // A font sized in units, so the drawing scale enlarges it together with everything else.
        private static Font UnitFont(string family, float points)
        {
            return new Font(family, points * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            padTimer.Stop();
            padTimer.Dispose();
            base.OnFormClosed(e);
        }

        private Btn AddButton(string text, int kind, RectangleF r, MethodInvoker click)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Kind = kind;
            b.R = r;
            b.Click = click;
            buttons.Add(b);
            return b;
        }

        // ---- updates ----
        // The green "Update Available" button, shown when a newer version is known.
        private void AddUpdateButton()
        {
            buttons.RemoveAll(delegate(Btn b) { return b.Kind == 3; });
            Version newer = offered != null ? offered.Version : KnownNewer();
            if (newer == null) return;
            Btn button = AddButton("↑ Update Available: v" + newer, 3,
                                   new RectangleF(TotalW - 24 - 96 - 8 - 236, 22, 236, 30), UpdateClicked);
            button.Tip = "Version " + newer + " is out. You have " + Updater.Current + ".";
        }

        // The newer version remembered from an earlier check, unless the user chose to skip it.
        private Version KnownNewer()
        {
            Version known;
            if (!Updater.Available || !Version.TryParse(cfg.LatestKnownVersion, out known)) return null;
            if (known <= Updater.Current || cfg.LatestKnownVersion == cfg.SkipVersion) return null;
            return known;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // At most one automatic check an hour, and never before the user has said yes to checking.
            if (!Updater.Available || !cfg.CheckUpdates || (DateTime.UtcNow - cfg.LastUpdateCheck).TotalHours < 1) return;
            System.Threading.Thread worker = new System.Threading.Thread(delegate()
            {
                string error;
                ReleaseInfo found = Updater.Check(out error);
                try
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke((MethodInvoker)delegate { CheckFinished(found, error); });
                }
                catch (InvalidOperationException) { }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void CheckFinished(ReleaseInfo found, string error)
        {
            if (error.Length > 0) return;         // an automatic check that fails stays silent
            cfg.LastUpdateCheck = DateTime.UtcNow;
            cfg.LatestKnownVersion = found != null ? found.Version.ToString() : "";
            cfg.Save();
            offered = found != null && found.Version > Updater.Current && found.Version.ToString() != cfg.SkipVersion ? found : null;
            AddUpdateButton();
            Invalidate();
        }

        private void UpdateClicked()
        {
            ReleaseInfo release = offered;
            if (release == null)                  // only the version number is remembered: ask GitHub for the rest
            {
                Cursor = Cursors.WaitCursor;
                string error;
                release = Updater.Check(out error);
                Cursor = Cursors.Default;
                if (release == null || release.Version <= Updater.Current)
                {
                    cfg.LatestKnownVersion = "";
                    AddUpdateButton();
                    SetWarning(error.Length > 0 ? "Couldn't check for updates: " + error : "You have the latest version.");
                    return;
                }
                offered = release;
            }
            using (UpdateForm f = new UpdateForm(cfg, release))
            {
                f.ShowDialog(this);
                if (f.Restarting)
                {
                    Restarting = true;
                    DialogResult = DialogResult.Cancel;
                    return;
                }
            }
            if (cfg.SkipVersion == release.Version.ToString()) offered = null;
            AddUpdateButton();
            padPrevious = -1;
            Invalidate();
        }

        private void SetWarning(string text)
        {
            if (warning == text) return;
            warning = text;
            Invalidate();
        }

        // ---- buttons ----
        private void StartClicked()
        {
            if (!File.Exists(cfg.GameExe)) { SetWarning("The game has not been found yet. Open Settings, top right."); return; }
            if (!Proto.FolderLooksValid(cfg.ProtoDir)) { SetWarning("ProtoInput has not been found yet. Open Settings, top right."); return; }
            DialogResult = DialogResult.OK;
        }

        private void CancelClicked()
        {
            DialogResult = DialogResult.Cancel;
        }

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(cfg))
            {
                f.ShowDialog(this);
                if (f.Restarting)
                {
                    Restarting = true;
                    DialogResult = DialogResult.Cancel;
                    return;
                }
            }
            AddUpdateButton();
            warning = "";
            padPrevious = -1;
            Invalidate();
        }

        // ---- keyboard, mouse, gamepad ----
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Enter: StartClicked(); return true;
                case Keys.Escape: CancelClicked(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private PointF Units(Point p) { return new PointF(p.X / scale, p.Y / scale); }

        private Btn ButtonAt(PointF u)
        {
            foreach (Btn b in buttons)
                if (b.R.Contains(u)) return b;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            downButton = ButtonAt(Units(e.Location));
            if (downButton != null) Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Btn over = ButtonAt(Units(e.Location));
            ShowTipFor(over);
            if (over != hotButton)
            {
                hotButton = over;
                Cursor = over != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || downButton == null) return;
            Btn b = downButton;
            downButton = null;
            Invalidate();
            if (ButtonAt(Units(e.Location)) == b && b.Click != null) b.Click();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            ShowTipFor(null);
            if (hotButton != null) { hotButton = null; Invalidate(); }
        }

        // Shows a button's tip just under it while the mouse is on it.
        private void ShowTipFor(Btn b)
        {
            if (b != null && b.Tip.Length == 0) b = null;
            if (b == tipFor) return;
            tipFor = b;
            if (b == null) { tip.Hide(this); return; }
            // ToolTip.Show measures from the corner of the whole window, title bar included.
            Point inside = PointToScreen(Point.Empty);
            tipText = b.Tip;
            tip.Show(b.Tip, this, inside.X - Left + (int)Math.Round(b.R.Left * scale),
                                  inside.Y - Top + (int)Math.Round((b.R.Bottom + 6) * scale));
        }

        // Any connected pad: A starts, B cancels. Each side lights up while its own pad has a button held,
        // so the players can see which pad drives which game.
        private void PollPads(object sender, EventArgs e)
        {
            int now = 0, leftButtons = 0, rightButtons = 0;
            bool leftOn = false, rightOn = false;
            for (int slot = 0; slot < 4; slot++)
            {
                int held;
                if (!Native.PadButtons(slot, out held)) continue;
                now |= held;
                if (slot == cfg.LeftPad - 1) { leftOn = true; leftButtons = held; }
                if (slot == cfg.RightPad - 1) { rightOn = true; rightButtons = held; }
            }
            if (leftOn != leftConnected || rightOn != rightConnected || (leftButtons != 0) != leftHeld || (rightButtons != 0) != rightHeld)
            {
                leftConnected = leftOn;
                rightConnected = rightOn;
                leftHeld = leftButtons != 0;
                rightHeld = rightButtons != 0;
                Invalidate();
            }

            // Ignore the pad while Settings is open, and anything already held when the menu appears.
            if (!IsHandleCreated || !Native.IsWindowEnabled(Handle) || padPrevious < 0)
            {
                padPrevious = now;
                return;
            }
            int pressed = now & ~padPrevious;
            padPrevious = now;
            if ((pressed & 0x1000) != 0) StartClicked();
            else if ((pressed & 0x2000) != 0) CancelClicked();
        }

        // ---- drawing ----
        private static void Fill(Graphics g, Color c, RectangleF r)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        private static void Write(Graphics g, string text, Font font, Color c, RectangleF r, StringAlignment across, StringAlignment down, bool wrap)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = across;
                f.LineAlignment = down;
                f.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) f.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text, font, b, r, f);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Window);
            g.ScaleTransform(scale, scale);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Write(g, "Couch Co-op", titleFont, Theme.Title, new RectangleF(ColL - 3, 14, 300, 40), StringAlignment.Near, StringAlignment.Near, false);
            Fill(g, Theme.Accent, new RectangleF(ColL, 58, 64, 3));

            Write(g, "LEFT GAME", labelFont, Theme.Secondary, new RectangleF(ColL - 2, 76, ColW, 20), StringAlignment.Near, StringAlignment.Near, false);
            Write(g, "RIGHT GAME", labelFont, Theme.Secondary, new RectangleF(ColR - 2, 76, ColW, 20), StringAlignment.Near, StringAlignment.Near, false);
            DrawSide(g, new RectangleF(ColL, WellTop, ColW, WellH), cfg.LeftPad, leftConnected, leftHeld);
            DrawSide(g, new RectangleF(ColR, WellTop, ColW, WellH), cfg.RightPad, rightConnected, rightHeld);

            foreach (Btn b in buttons)
            {
                bool hot = b == hotButton || b == downButton;
                Color face, ink;
                if (b.Kind == 0)
                {
                    face = hot ? Theme.AccentHot : Theme.Accent;
                    ink = Color.White;
                }
                else if (b.Kind == 1)
                {
                    face = hot ? Theme.ButtonHot : Theme.ButtonFace;
                    ink = Theme.ButtonText;
                }
                else if (b.Kind == 3)
                {
                    face = hot ? Theme.UpdateHot : Theme.Update;
                    ink = Color.White;
                }
                else
                {
                    face = hot ? Theme.ButtonFace : Theme.Card;
                    ink = hot ? Theme.Text : Theme.Secondary;
                }
                Fill(g, face, b.R);
                Write(g, b.Text, b.Kind >= 2 ? smallButtonFont : buttonFont, ink, b.R, StringAlignment.Center, StringAlignment.Center, false);
            }

            RectangleF hint = new RectangleF(ColL - 2, BottomY + 2, ColR + ColW - 124 - 112 - 12 - ColL, 40);
            if (warning.Length > 0) Write(g, warning, hintFont, Theme.Warning, hint, StringAlignment.Near, StringAlignment.Near, true);
            else Write(g, HintText, hintFont, Theme.Secondary, hint, StringAlignment.Near, StringAlignment.Near, true);
        }

        // One half of the screen: which gamepad drives it, and whether that pad is connected now.
        private void DrawSide(Graphics g, RectangleF r, int pad, bool connected, bool held)
        {
            Fill(g, held ? Theme.Accent : Theme.Border, r);
            Fill(g, held ? Theme.WellHot : Theme.Well, RectangleF.Inflate(r, -2, -2));
            Write(g, "Gamepad " + pad, padFont, Theme.Text, new RectangleF(r.X + 18, r.Y + 22, r.Width - 36, 40),
                  StringAlignment.Near, StringAlignment.Near, false);
            Write(g, connected ? "Connected" : "Not connected", stateFont, connected ? Theme.Good : Theme.Warning,
                  new RectangleF(r.X + 18, r.Y + 66, r.Width - 36, 22), StringAlignment.Near, StringAlignment.Near, false);
            Write(g, "Logs in on the game's own screen", stateFont, Theme.Faint,
                  new RectangleF(r.X + 18, r.Y + 92, r.Width - 36, 22), StringAlignment.Near, StringAlignment.Near, false);
        }
    }
}
