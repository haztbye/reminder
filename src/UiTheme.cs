using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KaoYanReminder
{
    public static class UiTheme
    {
        public static readonly Color Accent = Color.FromArgb(255, 107, 53);
        public static readonly Color AccentDark = Color.FromArgb(232, 89, 36);
        public static readonly Color AccentSoft = Color.FromArgb(255, 237, 227);
        public static readonly Color TextPrimary = Color.FromArgb(31, 41, 55);
        public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);
        public static readonly Color TextLight = Color.FromArgb(156, 163, 175);
        public static readonly Color Background = Color.FromArgb(248, 250, 252);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(229, 231, 235);
        public static readonly Color Green = Color.FromArgb(16, 185, 129);
        public static readonly Color DarkBar = Color.FromArgb(28, 33, 45);

        public static readonly Font F9 = MakeFont(9f, FontStyle.Regular);
        public static readonly Font F9B = MakeFont(9f, FontStyle.Bold);
        public static readonly Font F10 = MakeFont(10f, FontStyle.Regular);
        public static readonly Font F10B = MakeFont(10f, FontStyle.Bold);
        public static readonly Font F11 = MakeFont(11f, FontStyle.Regular);
        public static readonly Font F11B = MakeFont(11f, FontStyle.Bold);
        public static readonly Font F12B = MakeFont(12f, FontStyle.Bold);
        public static readonly Font F14B = MakeFont(14f, FontStyle.Bold);
        public static readonly Font F16B = MakeFont(16f, FontStyle.Bold);
        public static readonly Font F22B = MakeFont(22f, FontStyle.Bold);
        public static readonly Font F26 = MakeFont(26f, FontStyle.Regular);
        public static readonly Font F30B = MakeFont(30f, FontStyle.Bold);
        public static readonly Font Mono10 = new Font("Consolas", 10f, FontStyle.Regular);
        public static readonly Font Mono17B = new Font("Consolas", 17f, FontStyle.Bold);
        public static readonly Font Mono26 = new Font("Consolas", 26f, FontStyle.Regular);

        private static Font MakeFont(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style); }
        }

        public static Label MakeLabel(string text, Font font, Color color)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = font;
            lbl.ForeColor = color;
            lbl.AutoSize = true;
            lbl.BackColor = Color.Transparent;
            return lbl;
        }

        public static Button MakeButton(string text, bool primary, int width, int height)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.Font = F10;
            Size measure = TextRenderer.MeasureText(text, b.Font, new Size(1200, 200), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            b.Width = Math.Max(width, measure.Width + 36);
            b.Height = Math.Max(height, measure.Height + 14);
            b.Cursor = Cursors.Hand;
            if (primary)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = AccentDark;
                b.FlatAppearance.MouseDownBackColor = AccentDark;
            }
            else
            {
                b.BackColor = Color.White;
                b.ForeColor = TextPrimary;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 244, 246);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(229, 231, 235);
            }
            return b;
        }

        public static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) d = 1;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void SetChildLabelBackgrounds(Control parent, Color color)
        {
            if (parent == null) return;
            foreach (Control c in parent.Controls)
            {
                Label lbl = c as Label;
                if (lbl != null) lbl.BackColor = color;
            }
        }

        public static void SetRounded(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            try { control.Region = new Region(RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius)); }
            catch { }
        }

        public static void DrawCardBackground(Graphics g, Rectangle bounds, Color fill, Color border, int radius)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedPath(bounds, radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
                if (border != Color.Empty)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }
    }
}