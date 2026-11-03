using System;
using System.Drawing;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>提醒弹窗展示的数据。</summary>
    public class ReminderInfo
    {
        public bool IsEnd;
        public bool IsSnooze;
        public bool IsTest;
        public string HeaderText;
        public string TimeText;
        public string Title;
        public string RangeText;
        public string NextText;
        public string PlanName;

        public ReminderInfo()
        {
            HeaderText = "开始提醒";
            TimeText = "";
            Title = "";
            RangeText = "";
            NextText = "";
            PlanName = "";
        }
    }

    /// <summary>到点提醒大弹窗（开始提醒 / 稍后提醒 / 测试提醒）。</summary>
    public class ReminderForm : Form
    {
        private readonly ReminderInfo info;
        private readonly Timer autoCloseTimer;
        private Button btnOk;
        private Button btnSnooze;

        public event EventHandler DismissClicked;
        public event EventHandler SnoozeClicked;

        public ReminderInfo Info { get { return info; } }

        public ReminderForm(ReminderInfo info)
        {
            this.info = info;
            BuildUi();
            autoCloseTimer = new Timer();
            autoCloseTimer.Interval = 180000;
            autoCloseTimer.Tick += delegate { autoCloseTimer.Stop(); Close(); };
            autoCloseTimer.Start();
            UiScale.ScaleForm(this);
            try { UiTheme.SetRounded(this, UiScale.Px(16)); } catch { }
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "考研提醒";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Size = new Size(460, 258);
            DoubleBuffered = true;
            KeyPreview = true;
            Font = UiTheme.F10;

            Panel accent = new Panel();
            accent.BackColor = info.IsEnd ? UiTheme.Green : UiTheme.Accent;
            accent.Dock = DockStyle.Top;
            accent.Height = 6;
            Controls.Add(accent);

            Label header = UiTheme.MakeLabel(info.HeaderText, UiTheme.F9B, info.IsEnd ? UiTheme.Green : UiTheme.Accent);
            header.Location = new Point(26, 22);
            Controls.Add(header);

            if (!string.IsNullOrEmpty(info.PlanName) && info.PlanName != AppConfig.DefaultPlanName)
            {
                Label plan = UiTheme.MakeLabel("方案：" + info.PlanName, UiTheme.F9, UiTheme.TextLight);
                plan.Location = new Point(296, 22);
                plan.AutoSize = false;
                plan.Width = 140;
                plan.Height = 20;
                plan.TextAlign = ContentAlignment.MiddleRight;
                Controls.Add(plan);
            }

            Label time = UiTheme.MakeLabel(info.TimeText, UiTheme.Mono26, UiTheme.TextPrimary);
            time.Location = new Point(22, 36);
            time.AutoSize = false;
            time.Width = 300;
            time.Height = 52;
            time.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(time);

            Label title = UiTheme.MakeLabel(info.Title, UiTheme.F16B, UiTheme.TextPrimary);
            title.Location = new Point(26, 96);
            title.AutoSize = false;
            title.Width = 390;
            title.Height = 30;
            title.AutoEllipsis = true;
            Controls.Add(title);

            Label range = UiTheme.MakeLabel(info.RangeText, UiTheme.F9, UiTheme.TextSecondary);
            range.Location = new Point(26, 132);
            Controls.Add(range);

            Label next = UiTheme.MakeLabel(info.NextText, UiTheme.F9, UiTheme.TextSecondary);
            next.Location = new Point(26, 158);
            next.AutoSize = false;
            next.Width = 418;
            next.Height = 22;
            next.AutoEllipsis = true;
            Controls.Add(next);

            btnSnooze = UiTheme.MakeButton("5 分钟后再提醒", false, 176, 38);
            btnSnooze.Location = new Point(24, 198);
            btnSnooze.Click += delegate { if (SnoozeClicked != null) SnoozeClicked(this, EventArgs.Empty); };
            Controls.Add(btnSnooze);

            btnOk = UiTheme.MakeButton("知道了", true, 110, 38);
            btnOk.Location = new Point(Width - 24 - btnOk.Width, 198);
            btnOk.Click += delegate { if (DismissClicked != null) DismissClicked(this, EventArgs.Empty); Close(); };
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            ResumeLayout(false);
            PerformLayout();
            UiTheme.SetChildLabelBackgrounds(this, Color.White);
            try { UiTheme.SetRounded(this, 16); } catch { }
            Paint += delegate(object s, PaintEventArgs e)
            {
                UiTheme.DrawCardBackground(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Color.White, UiTheme.Border, 16);
            };
            Shown += delegate { Activate(); btnOk.Focus(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { autoCloseTimer.Stop(); autoCloseTimer.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }

    /// <summary>结束轻提醒：右下角小提示，5 秒自动消失，不抢焦点。</summary>
    public class EndToastForm : Form
    {
        private readonly Timer autoCloseTimer;

        public EndToastForm(ReminderInfo info)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Size = new Size(384, 112);
            DoubleBuffered = true;
            Font = UiTheme.F10;

            Panel accent = new Panel();
            accent.BackColor = UiTheme.Green;
            accent.Dock = DockStyle.Left;
            accent.Width = 5;
            Controls.Add(accent);

            Label header = UiTheme.MakeLabel("结束提醒", UiTheme.F9B, UiTheme.Green);
            header.Location = new Point(22, 16);
            Controls.Add(header);

            Label title = UiTheme.MakeLabel(info.Title, UiTheme.F14B, UiTheme.TextPrimary);
            title.Location = new Point(22, 40);
            title.AutoSize = false;
            title.Width = 340;
            title.Height = 26;
            title.AutoEllipsis = true;
            Controls.Add(title);

            Label next = UiTheme.MakeLabel(info.NextText, UiTheme.F9, UiTheme.TextSecondary);
            next.Location = new Point(22, 72);
            next.AutoSize = false;
            next.Width = 340;
            next.Height = 22;
            next.AutoEllipsis = true;
            Controls.Add(next);

            Click += delegate { Close(); };
            foreach (Control c in Controls)
            {
                c.Click += delegate { Close(); };
            }

            UiTheme.SetChildLabelBackgrounds(this, Color.White);
            try { UiTheme.SetRounded(this, 14); } catch { }
            Paint += delegate(object s, PaintEventArgs e)
            {
                UiTheme.DrawCardBackground(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Color.White, UiTheme.Border, 14);
            };

            autoCloseTimer = new Timer();
            autoCloseTimer.Interval = 5000;
            autoCloseTimer.Tick += delegate { autoCloseTimer.Stop(); Close(); };
            autoCloseTimer.Start();
            UiScale.ScaleForm(this);
            try { UiTheme.SetRounded(this, UiScale.Px(14)); } catch { }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { autoCloseTimer.Stop(); autoCloseTimer.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}