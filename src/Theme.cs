// Colours, fonts and small helpers for the dark look (sampled from MEMM).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MhoCoop
{
    // A flat button that does not draw the light outline Windows adds to the default or focused button.
    internal sealed class FlatButton : Button
    {
        protected override bool ShowFocusCues { get { return false; } }
        public override void NotifyDefault(bool value) { base.NotifyDefault(false); }
    }

    internal static class Theme
    {
        public static readonly Color Window = Rgb(0x1E1E1E);
        public static readonly Color Card = Rgb(0x27272B);
        public static readonly Color Border = Rgb(0x3A383D);
        public static readonly Color Accent = Rgb(0x6C5CF0);
        public static readonly Color AccentHot = Rgb(0x7D6EF5);
        public static readonly Color SelectedFace = Rgb(0x2E2B55);
        public static readonly Color Text = Rgb(0xEDEDF0);
        public static readonly Color Title = Rgb(0xF2F2F2);
        public static readonly Color Secondary = Rgb(0x9A9AA2);
        public static readonly Color Faint = Rgb(0x6A6A72);
        public static readonly Color Dim = Rgb(0x55555B);
        public static readonly Color Well = Rgb(0x18181A);
        public static readonly Color WellHot = Rgb(0x2A2748);
        public static readonly Color ButtonFace = Rgb(0x34333A);
        public static readonly Color ButtonHot = Rgb(0x424149);
        public static readonly Color ButtonText = Rgb(0xE6E6E6);
        public static readonly Color Disabled = Rgb(0x77767C);
        public static readonly Color Warning = Rgb(0xE8B339);
        public static readonly Color Good = Rgb(0x6FCF97);
        public static readonly Color Update = Rgb(0x2E9E5B);
        public static readonly Color UpdateHot = Rgb(0x38B36A);

        private static Color Rgb(int value) { return Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255); }

        private static float scale;
        private static Icon appIcon;

        // Screen scaling: 1.0 at 100%, 2.25 at 225%.
        public static float Scale
        {
            get
            {
                if (scale <= 0f)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                }
                return scale;
            }
        }

        public static int S(int value) { return (int)Math.Round(value * Scale); }

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch (Exception) { appIcon = SystemIcons.Application; }
                }
                return appIcon;
            }
        }

        public static Font Ui(float points, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", points, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (Native.DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) Native.DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch (Exception) { }
        }

        // Common setup for the small dialogs. Sizes are given for 100% scaling.
        public static void StyleDialog(Form f, string title, int width, int height)
        {
            f.Text = title;
            f.AutoScaleMode = AutoScaleMode.None;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.BackColor = Window;
            f.ForeColor = Text;
            f.Font = Ui(9.5f, false);
            f.ClientSize = new Size(S(width), S(height));
            f.Icon = AppIcon;
            f.HandleCreated += delegate { DarkTitleBar(f.Handle); };
        }

        // Adds a control at a position given for 100% scaling. Width or height 0 keeps the control's own size.
        public static T Put<T>(Control parent, T control, int x, int y, int width, int height) where T : Control
        {
            control.Location = new Point(S(x), S(y));
            if (width > 0 && height > 0) control.Size = new Size(S(width), S(height));
            else if (width > 0) control.Width = S(width);
            parent.Controls.Add(control);
            return control;
        }

        public static Label MakeLabel(string text, bool secondary)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.ForeColor = secondary ? Secondary : Text;
            l.BackColor = Color.Transparent;
            return l;
        }

        public static TextBox MakeTextBox()
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Well;
            t.ForeColor = Text;
            t.Font = Ui(10f, false);
            return t;
        }

        public static Button MakeButton(string text, bool primary)
        {
            Button b = new FlatButton();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = primary ? Accent : ButtonFace;
            b.ForeColor = primary ? Color.White : ButtonText;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHot : ButtonHot;
            b.FlatAppearance.MouseDownBackColor = primary ? AccentHot : ButtonHot;
            b.Font = Ui(9.5f, true);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static ComboBox MakeCombo()
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Well;
            c.ForeColor = Text;
            c.Font = Ui(10f, false);
            c.MaxDropDownItems = 16;
            c.IntegralHeight = false;
            return c;
        }

        public static CheckBox MakeCheckBox(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.ForeColor = Text;
            c.BackColor = Color.Transparent;
            return c;
        }

        public static NumericUpDown MakeNumber(int min, int max, int value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.BackColor = Well;
            n.ForeColor = Text;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Font = Ui(10f, false);
            return n;
        }

        public static void Warn(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "MHO Couch Co-op", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
