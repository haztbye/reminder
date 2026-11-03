using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>今日计划窗口：列出当天全部安排，高亮当前项并显示倒计时。</summary>
    public class MainForm : Form
    {
        private readonly ScheduleEngine engine;
        private AppConfig config;
        private readonly Timer clockTimer;
        private Panel header;
        private Panel footer;
        private Button btnTest;
        private Button btnHide;
        private Label lblHeadTitle;
        private Label lblDate;
        private Label lblStatus;
        private Label lblPlanTag;
        private Button btnSettings;
        private FlowLayoutPanel body;
        private Label lblFooterHint;
        private readonly List<RowRefs> rows = new List<RowRefs>();
        private bool scrollPending = true;
        private float scale = 1f;

        private bool allowClose;

        public bool AllowClose
        {
            get { return allowClose; }
            set { allowClose = value; }
        }

        public event EventHandler SettingsRequested;
        public event EventHandler TestRequested;

        private class RowRefs
        {
            public PlanItem Item;
            public Panel Panel;
            public Label Time;
            public Label Title;
            public Label Status;
            public Panel Stripe;
        }

        public MainForm(AppConfig config, ScheduleEngine engine)
        {
            this.config = config;
            this.engine = engine;
            BuildUi();
            RebuildRows();
            clockTimer = new Timer();
            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate { RefreshState(); };
            clockTimer.Start();
            Shown += delegate { RefreshState(); };
            FormClosing += OnClosing;
            ApplyScale();
        }

        private void ApplyScale()
        {
            if (UiScale.Factor <= 1.001f) return;
            UiScale.ScaleForm(this);
            scale = UiScale.Factor;
            ResizeRows();
            if (btnSettings != null && header != null)
                btnSettings.Left = header.ClientSize.Width - btnSettings.Width - UiScale.Px(16);
            if (footer != null && btnTest != null && btnHide != null)
            {
                btnHide.Left = footer.ClientSize.Width - UiScale.Px(20) - btnHide.Width;
                btnTest.Left = btnHide.Left - UiScale.Px(12) - btnTest.Width;
            }
        }

        public void ApplyConfig(AppConfig newConfig)
        {
            bool wasScaled = scale > 1.001f;
            if (wasScaled)
            {
                UiScale.UnscaleForm(this);
                scale = 1f;
            }
            config = newConfig;
            engine.Config = newConfig;
            RebuildRows();
            if (wasScaled) ApplyScale();
            ResizeRows();
            RefreshState();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "考研提醒 · 今日计划";
            BackColor = UiTheme.Background;
            Font = UiTheme.F10;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 760);
            MinimumSize = new Size(500, 560);
            DoubleBuffered = true;
            KeyPreview = true;

            header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 118;
            header.Paint += OnHeaderPaint;
            Controls.Add(header);

            lblHeadTitle = UiTheme.MakeLabel("今日计划", UiTheme.F10B, Color.FromArgb(235, 255, 255, 255));
            lblHeadTitle.Location = new Point(24, 16);
            header.Controls.Add(lblHeadTitle);

            lblDate = UiTheme.MakeLabel("", UiTheme.F14B, Color.White);
            lblDate.Location = new Point(22, 40);
            lblDate.AutoSize = false;
            lblDate.Width = 330;
            lblDate.Height = 30;
            lblDate.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(lblDate);

            lblStatus = UiTheme.MakeLabel("", UiTheme.F10, Color.White);
            lblStatus.Location = new Point(24, 78);
            lblStatus.AutoSize = false;
            lblStatus.Width = 470;
            lblStatus.Height = 24;
            lblStatus.AutoEllipsis = true;
            header.Controls.Add(lblStatus);

            lblPlanTag = UiTheme.MakeLabel("", UiTheme.F9, Color.FromArgb(255, 236, 224));
            lblPlanTag.Location = new Point(130, 17);
            lblPlanTag.AutoSize = false;
            lblPlanTag.Width = 280;
            lblPlanTag.Height = 20;
            lblPlanTag.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(lblPlanTag);

            btnSettings = UiTheme.MakeButton("设置", false, 62, 30);
            btnSettings.Font = UiTheme.F9;
            btnSettings.BackColor = Color.White;
            btnSettings.ForeColor = UiTheme.AccentDark;
            btnSettings.FlatAppearance.BorderSize = 0;
            btnSettings.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 237, 227);
            btnSettings.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 224, 207);
            btnSettings.Location = new Point(474, 12);
            btnSettings.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            header.Controls.Add(btnSettings);
            btnSettings.BringToFront();

            body = new FlowLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.FlowDirection = FlowDirection.TopDown;
            body.WrapContents = false;
            body.AutoScroll = true;
            body.BackColor = UiTheme.Background;
            body.Padding = new Padding(16, 14, 16, 10);
            body.Resize += delegate { ResizeRows(); };
            Controls.Add(body);

            footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 66;
            footer.BackColor = Color.White;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.Border, 1f)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            Controls.Add(footer);

            lblFooterHint = UiTheme.MakeLabel("关闭窗口后仍在托盘运行", UiTheme.F9, UiTheme.TextLight);
            lblFooterHint.Location = new Point(20, 24);
            lblFooterHint.BackColor = Color.White;
            lblFooterHint.AutoSize = false;
            lblFooterHint.Width = 240;
            lblFooterHint.Height = 20;
            lblFooterHint.AutoEllipsis = true;
            footer.Controls.Add(lblFooterHint);

            btnTest = UiTheme.MakeButton("立即测试", false, 96, 36);
            btnTest.Location = new Point(footer.Width - 20 - 96 - 104 - 12, 15);
            btnTest.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnTest.Click += delegate { if (TestRequested != null) TestRequested(this, EventArgs.Empty); };
            footer.Controls.Add(btnTest);

            btnHide = UiTheme.MakeButton("隐藏到托盘", true, 104, 36);
            btnHide.Location = new Point(footer.Width - 20 - 104, 15);
            btnHide.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnHide.Click += delegate { Hide(); };
            footer.Controls.Add(btnHide);
            footer.Resize += delegate
            {
                btnHide.Left = footer.ClientSize.Width - UiScale.Px(20) - btnHide.Width;
                btnTest.Left = btnHide.Left - UiScale.Px(12) - btnTest.Width;
            };
            body.BringToFront();
            btnTest.BringToFront();
            btnHide.BringToFront();

            ResumeLayout(false);
            PerformLayout();
        }

        private void OnHeaderPaint(object sender, PaintEventArgs e)
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, header.Width, header.Height),
                Color.FromArgb(255, 141, 66), Color.FromArgb(255, 92, 46), LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, header.ClientRectangle);
            }
            
        }

        private void RebuildRows()
        {
            body.SuspendLayout();
            foreach (Control c in body.Controls) c.Dispose();
            body.Controls.Clear();
            rows.Clear();

            PlanProfile plan = config.GetTodayProfile();
            List<PlanItem> items = new List<PlanItem>();
            if (plan != null && plan.Items != null)
            {
                foreach (PlanItem it in plan.Items) if (it != null) items.Add(it);
                items.Sort(PlanItemComparer.Instance);
            }

            if (items.Count == 0)
            {
                Label empty = UiTheme.MakeLabel("今天还没有计划。点击右上角“设置”添加，或从托盘菜单切换方案。", UiTheme.F10, UiTheme.TextSecondary);
                empty.AutoSize = false;
                empty.Width = Math.Max(300, body.ClientSize.Width - 40);
                empty.Height = 80;
                empty.Padding = new Padding(8, 20, 8, 0);
                body.Controls.Add(empty);
                body.ResumeLayout();
                ResizeRows();
                return;
            }

            int rowWidth = GetRowWidth();
            foreach (PlanItem item in items)
            {
                Panel row = new Panel();
                row.Height = 48;
                row.Width = rowWidth;
                row.Margin = new Padding(0, 0, 0, 8);
                row.BackColor = Color.White;
                row.Paint += OnRowPaint;

                Panel stripe = new Panel();
                stripe.Width = 3;
                stripe.Height = 30;
                stripe.Location = new Point(0, 9);
                row.Controls.Add(stripe);

                Label time = UiTheme.MakeLabel(TimeUtil.FormatRange(item), UiTheme.Mono10, UiTheme.TextSecondary);
                time.Location = new Point(14, 12);
                time.AutoSize = false;
                time.Width = 154;
                time.Height = 24;
                time.BackColor = Color.Transparent;
                row.Controls.Add(time);

                Label title = UiTheme.MakeLabel(item.Title, UiTheme.F10B, UiTheme.TextPrimary);
                title.Location = new Point(172, 11);
                title.AutoSize = false;
                title.Width = row.Width - 172 - 92;
                title.Height = 26;
                title.AutoEllipsis = true;
                title.BackColor = Color.Transparent;
                row.Controls.Add(title);

                Label status = UiTheme.MakeLabel("待开始", UiTheme.F9, UiTheme.TextLight);
                status.AutoSize = false;
                status.Width = 80;
                status.Height = 22;
                status.Location = new Point(row.Width - 92, 14);
                status.TextAlign = ContentAlignment.MiddleRight;
                status.BackColor = Color.Transparent;
                row.Controls.Add(status);

                body.Controls.Add(row);

                RowRefs rr = new RowRefs();
                rr.Item = item;
                rr.Panel = row;
                rr.Time = time;
                rr.Title = title;
                rr.Status = status;
                rr.Stripe = stripe;
                rows.Add(rr);
            }
            body.ResumeLayout();
            ResizeRows();
            scrollPending = true;
        }

        private int GetRowWidth()
        {
            int w = body.ClientSize.Width - 56;
            if (body.VerticalScroll.Visible) w -= SystemInformation.VerticalScrollBarWidth;
            if (w < 260) w = 260;
            return w;
        }

        private void ResizeRows()
        {
            int w = GetRowWidth();
            foreach (RowRefs rr in rows)
            {
                rr.Panel.Width = w;
                rr.Title.Width = w - UiScale.Px(172) - UiScale.Px(92);
                rr.Status.Left = w - UiScale.Px(92);
            }
        }

        private void OnRowPaint(object sender, PaintEventArgs e)
        {
            Panel row = sender as Panel;
            if (row == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Color.FromArgb(243, 244, 246), 1f))
            {
                e.Graphics.DrawLine(line, 12, row.Height - 1, row.Width - 12, row.Height - 1);
            }
        }

        private void RefreshState()
        {
            if (config == null || engine == null) return;
            DateTime now = DateTime.Now;
            lblDate.Text = now.Month + " 月 " + now.Day + " 日 · " + WeekdayText(now.DayOfWeek);
            PlanProfile plan = config.GetTodayProfile();
            string planName = plan == null ? AppConfig.DefaultPlanName : plan.Name;
            lblPlanTag.Text = string.Equals(planName, AppConfig.DefaultPlanName, StringComparison.Ordinal) ? "" : "方案：" + planName;

            PlanItem current = engine.GetCurrent(now);
            DateTime nextWhen;
            PlanItem next = engine.GetNext(now, out nextWhen);
            bool paused = engine.IsPaused(now);

            if (paused && engine.PausedUntil.HasValue)
            {
                lblStatus.Text = "提醒已暂停，至 " + TimeUtil.FormatClock(engine.PausedUntil.Value) + "（托盘菜单可恢复）";
            }
            else if (current != null)
            {
                TimeSpan s, e;
                current.TryGetTimes(out s, out e);
                DateTime endAt = now.Date + e;
                lblStatus.Text = "正在 " + current.Title + " · 剩余 " + TimeUtil.FormatCountdown(endAt - now)
                    + (next != null ? " · 下一项 " + TimeUtil.FormatClock(nextWhen) + " " + next.Title : "");
            }
            else if (next != null)
            {
                bool tomorrow = nextWhen.Date > now.Date;
                lblStatus.Text = (tomorrow ? "明天 " : "下一项 ") + TimeUtil.FormatClock(nextWhen) + " " + next.Title
                    + " · " + TimeUtil.FormatApprox(nextWhen - now);
            }
            else
            {
                lblStatus.Text = "今天的计划已完成，好好休息";
            }

            PlanItem nextItem = next;
            foreach (RowRefs rr in rows)
            {
                bool isCurrent = rr.Item == current;
                bool isNext = nextItem != null && rr.Item == nextItem && !isCurrent;
                bool done = IsDone(rr.Item, now);
                bool disabled = !rr.Item.Enabled;
                if (disabled)
                {
                    rr.Panel.BackColor = Color.FromArgb(250, 250, 251);
                    rr.Status.Text = "已停用";
                    rr.Status.ForeColor = UiTheme.TextLight;
                    rr.Status.Font = UiTheme.F9;
                    rr.Title.ForeColor = UiTheme.TextLight;
                    rr.Time.ForeColor = UiTheme.TextLight;
                    rr.Stripe.BackColor = Color.FromArgb(229, 231, 235);
                }
                else if (isCurrent)
                {
                    rr.Panel.BackColor = UiTheme.AccentSoft;
                    rr.Status.Text = "进行中";
                    rr.Status.ForeColor = UiTheme.Accent;
                    rr.Status.Font = UiTheme.F9B;
                    rr.Title.ForeColor = UiTheme.TextPrimary;
                    rr.Time.ForeColor = UiTheme.TextPrimary;
                    rr.Stripe.BackColor = UiTheme.Accent;
                }
                else if (done)
                {
                    rr.Panel.BackColor = Color.FromArgb(252, 252, 253);
                    rr.Status.Text = "已完成";
                    rr.Status.ForeColor = UiTheme.TextLight;
                    rr.Status.Font = UiTheme.F9;
                    rr.Title.ForeColor = UiTheme.TextLight;
                    rr.Time.ForeColor = UiTheme.TextLight;
                    rr.Stripe.BackColor = Color.FromArgb(209, 213, 219);
                }
                else if (isNext)
                {
                    rr.Panel.BackColor = Color.White;
                    rr.Status.Text = "下一项";
                    rr.Status.ForeColor = UiTheme.Accent;
                    rr.Status.Font = UiTheme.F9B;
                    rr.Title.ForeColor = UiTheme.TextPrimary;
                    rr.Time.ForeColor = UiTheme.TextSecondary;
                    rr.Stripe.BackColor = UiTheme.AccentSoft;
                }
                else
                {
                    rr.Panel.BackColor = Color.White;
                    rr.Status.Text = "待开始";
                    rr.Status.ForeColor = UiTheme.TextLight;
                    rr.Status.Font = UiTheme.F9;
                    rr.Title.ForeColor = UiTheme.TextPrimary;
                    rr.Time.ForeColor = UiTheme.TextSecondary;
                    rr.Stripe.BackColor = Color.White;
                }
            }

            if (scrollPending)
            {
                scrollPending = false;
                PlanItem target = current != null ? current : next;
                if (target != null)
                {
                    foreach (RowRefs rr in rows)
                    {
                        if (rr.Item == target)
                        {
                            try { body.ScrollControlIntoView(rr.Panel); } catch { }
                            break;
                        }
                    }
                }
            }
        }

        private bool IsDone(PlanItem item, DateTime now)
        {
            if (item == null || !item.Enabled) return false;
            TimeSpan s, e;
            if (!item.TryGetTimes(out s, out e)) return false;
            if (string.IsNullOrEmpty(item.End)) return false;
            DateTime endAt = now.Date + e;
            if (endAt <= now.Date + s) return false;
            return now >= endAt;
        }

        private static string WeekdayText(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (allowClose) return;
            e.Cancel = true;
            Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { clockTimer.Stop(); clockTimer.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}