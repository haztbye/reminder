using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>桌面常驻倒计时条：当前任务剩余时间 + 下一项预告，可拖动。</summary>
    public class CountdownBar : Form
    {
        private readonly Label lblTitle;
        private readonly Label lblTime;
        private readonly Label lblSub;
        private readonly Label lblPrefix;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem menuPause;
        private readonly ToolStripMenuItem menuShow;
        private readonly ToolStripMenuItem menuSettings;
        private readonly ToolStripMenuItem menuHide;

        private readonly Action openMain;
        private readonly Action openSettings;
        private readonly Action pauseToggle;
        private readonly Action hideBar;
        private readonly Action positionChanged;

        private bool dragging;
        private bool dragMoved;
        private Point dragStart;
        private Point formStart;
        private float progress;
        private bool pausedNow;
        private string lastTitle = "";

        public event EventHandler DoubleClicked;

        public CountdownBar(Action openMain, Action openSettings, Action pauseToggle, Action hideBar, Action positionChanged)
        {
            this.openMain = openMain;
            this.openSettings = openSettings;
            this.pauseToggle = pauseToggle;
            this.hideBar = hideBar;
            this.positionChanged = positionChanged;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = UiTheme.DarkBar;
            Size = new Size(460, 74);
            DoubleBuffered = true;
            Font = UiTheme.F10;
            Cursor = Cursors.SizeAll;

            lblTitle = UiTheme.MakeLabel("下一项", UiTheme.F10B, Color.White);
            lblTitle.Location = new Point(18, 10);
            lblTitle.AutoSize = false;
            lblTitle.Width = 240;
            lblTitle.Height = 22;
            lblTitle.BackColor = UiTheme.DarkBar;
            lblTitle.AutoEllipsis = true;
            lblTitle.BackColor = UiTheme.DarkBar;
            Controls.Add(lblTitle);
            lblPrefix = UiTheme.MakeLabel("", UiTheme.F9, Color.FromArgb(160, 168, 184));
            lblPrefix.Location = new Point(252, 14);
            lblPrefix.AutoSize = false;
            lblPrefix.Width = 44;
            lblPrefix.Height = 20;
            lblPrefix.TextAlign = ContentAlignment.MiddleRight;
            lblPrefix.BackColor = UiTheme.DarkBar;
            Controls.Add(lblPrefix);

            lblTime = UiTheme.MakeLabel("--:--", UiTheme.Mono17B, UiTheme.Accent);
            lblTime.Location = new Point(300, 6);
            lblTime.AutoSize = false;
            lblTime.Width = 142;
            lblTime.Height = 34;
            lblTime.TextAlign = ContentAlignment.MiddleRight;
            lblTime.BackColor = UiTheme.DarkBar;
            Controls.Add(lblTime);

            lblSub = UiTheme.MakeLabel("", UiTheme.F9, Color.FromArgb(160, 168, 184));
            lblSub.Location = new Point(18, 40);
            lblSub.AutoSize = false;
            lblSub.Width = 424;
            lblSub.Height = 20;
            lblSub.AutoEllipsis = true;
            lblSub.BackColor = UiTheme.DarkBar;
            Controls.Add(lblSub);

            menu = new ContextMenuStrip();
            menu.Font = UiTheme.F10;
            ToolStripMenuItem miMain = new ToolStripMenuItem("打开今日计划");
            miMain.Click += delegate { if (openMain != null) openMain(); };
            menu.Items.Add(miMain);
            menuPause = new ToolStripMenuItem("暂停提醒 30 分钟");
            menuPause.Click += delegate { if (pauseToggle != null) pauseToggle(); };
            menu.Items.Add(menuPause);
            menuSettings = new ToolStripMenuItem("提醒设置…");
            menuSettings.Click += delegate { if (openSettings != null) openSettings(); };
            menu.Items.Add(menuSettings);
            menu.Items.Add(new ToolStripSeparator());
            menuHide = new ToolStripMenuItem("隐藏倒计时条");
            menuHide.Click += delegate { if (hideBar != null) hideBar(); };
            menu.Items.Add(menuHide);
            ContextMenuStrip = menu;

            AttachDrag(this);
            AttachDrag(lblTitle);
            AttachDrag(lblPrefix);
            AttachDrag(lblTime);
            AttachDrag(lblSub);
            DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            lblTitle.DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            lblTime.DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            lblSub.DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            lblPrefix.DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            lblTitle.Cursor = Cursors.SizeAll;
            lblTime.Cursor = Cursors.SizeAll;
            lblSub.Cursor = Cursors.SizeAll;
            lblPrefix.Cursor = Cursors.SizeAll;

            try { UiTheme.SetRounded(this, UiScale.Px(16)); } catch { }
            Paint += OnBarPaint;
            UiScale.ScaleForm(this);
            try { UiTheme.SetRounded(this, UiScale.Px(16)); } catch { }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragging)
            {
                dragging = false;
                if (dragMoved && positionChanged != null) positionChanged();
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private void AttachDrag(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragMoved = false;
                // 用屏幕坐标记录起点：即使鼠标从标签移到窗体，坐标基准也一致，避免拖动时来回跳
                dragStart = Cursor.Position;
                formStart = Location;
                Capture = true;
            };
            c.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                Point p = Cursor.Position;
                Point target = new Point(formStart.X + (p.X - dragStart.X), formStart.Y + (p.Y - dragStart.Y));
                if (target != Location)
                {
                    dragMoved = true;
                    Location = ClampToScreen(target);
                }
            };
            c.MouseUp += delegate
            {
                if (!dragging) return;
                dragging = false;
                Capture = false;
                if (dragMoved && positionChanged != null) positionChanged();
            };
            c.DoubleClick += delegate { if (DoubleClicked != null) DoubleClicked(this, EventArgs.Empty); };
            c.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    menu.Show(c, e.Location);
                }
            };
        }

        private Point ClampToScreen(Point p)
        {
            Rectangle area = Screen.FromPoint(new Point(p.X + 40, p.Y + 20)).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(p.X, area.Right - Width));
            int y = Math.Max(area.Top, Math.Min(p.Y, area.Bottom - Height));
            return new Point(x, y);
        }

        public void ApplyConfig(AppConfig config)
        {
            Point p;
            if (config.Settings.BarX >= 0 && config.Settings.BarY >= 0)
            {
                p = new Point(config.Settings.BarX, config.Settings.BarY);
            }
            else
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                p = new Point(area.Right - Width - 24, area.Top + 80);
            }
            Location = ClampToScreen(p);
        }

        public Point GetPosition()
        {
            return Location;
        }

        public void ShowBar()
        {
            if (!Visible) Show();
            try { UiTheme.SetRounded(this, 16); } catch { }
        }

        public void UpdateState(DateTime now, ScheduleEngine engine, bool paused)
        {
            if (engine == null) return;
            if (dragging) return;
            pausedNow = paused;
            menuPause.Text = paused ? "恢复提醒" : "暂停提醒 30 分钟";
            DateTime? pausedUntil = engine.PausedUntil;

            if (paused && pausedUntil.HasValue)
            {
                SetPrefix("");
                SetTitle("提醒已暂停");
                SetTimeText("至 " + TimeUtil.FormatClock(pausedUntil.Value), true);
                SetSub("右键菜单或托盘图标可恢复提醒");
                progress = 0f;
                Invalidate();
                return;
            }

            PlanProfile plan = engine.TodayProfile;
            int enabledCount = 0;
            if (plan != null && plan.Items != null)
            {
                foreach (PlanItem it in plan.Items) if (it != null && it.Enabled) enabledCount++;
            }
            if (enabledCount == 0)
            {
                SetPrefix("");
                SetTitle("今天没有计划");
                SetTimeText("", false);
                SetSub("右键打开设置，添加今天要做的事");
                progress = 0f;
                Invalidate();
                return;
            }

            PlanItem current = engine.GetCurrent(now);
            DateTime nextWhen;
            PlanItem next = engine.GetNext(now, out nextWhen);
            string planTag = "";
            if (!string.Equals(plan.Name, AppConfig.DefaultPlanName, StringComparison.Ordinal))
                planTag = "[" + plan.Name + "] ";

            if (current != null)
            {
                TimeSpan start, end;
                current.TryGetTimes(out start, out end);
                DateTime startAt = now.Date + start;
                DateTime endAt = now.Date + end;
                TimeSpan remain = endAt - now;
                SetPrefix("剩余");
                SetTitle("正在：" + current.Title);
                SetTimeText(TimeUtil.FormatCountdown(remain), false);
                string sub = planTag + "本项 " + TimeUtil.FormatRange(current);
                if (next != null)
                {
                    sub += "  ·  下一项 " + TimeUtil.FormatClock(nextWhen) + " " + next.Title;
                }
                SetSub(sub);
                double total = (endAt - startAt).TotalSeconds;
                progress = total <= 0 ? 0f : (float)Math.Max(0, Math.Min(1, (now - startAt).TotalSeconds / total));
            }
            else if (next != null)
            {
                bool tomorrow = nextWhen.Date > now.Date;
                SetPrefix("还有");
                SetTitle((tomorrow ? "明天：" : "下一项：") + next.Title);
                SetTimeText(TimeUtil.FormatCountdown(nextWhen - now), false);
                string whenText = tomorrow ? ("明天 " + TimeUtil.FormatClock(nextWhen)) : ("今天 " + TimeUtil.FormatClock(nextWhen));
                string sub = planTag + whenText + " 开始  ·  " + TimeUtil.FormatRange(next) + "  ·  共 " + enabledCount + " 项";
                SetSub(sub);
                progress = 0f;
            }
            else
            {
                SetPrefix("");
                SetTitle("今日计划已完成");
                SetTimeText("", false);
                SetSub(planTag + "好好休息，晚安");
                progress = 0f;
            }
            Invalidate();
        }

        private void SetTitle(string text)
        {
            if (lastTitle != text || lblTitle.Text != text)
            {
                lblTitle.Text = text;
                lastTitle = text;
            }
        }

        private void SetPrefix(string text)
        {
            if (lblPrefix.Text != text) lblPrefix.Text = text;
        }

        private void SetTimeText(string text, bool small)
        {
            lblTime.Text = text;
            lblTime.Font = small ? UiTheme.F12B : UiTheme.Mono17B;
        }

        private void SetSub(string text)
        {
            if (lblSub.Text != text) lblSub.Text = text;
        }

        private void OnBarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiTheme.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 16))
            {
                using (SolidBrush b = new SolidBrush(UiTheme.DarkBar)) g.FillPath(b, path);
                using (Pen p = new Pen(Color.FromArgb(52, 62, 82), 1f)) g.DrawPath(p, path);
            }
            if (progress > 0f)
            {
                int trackLeft = 18;
                int trackRight = Width - 18;
                int y = Height - 4;
                using (Pen track = new Pen(Color.FromArgb(60, 70, 92), 2f))
                {
                    g.DrawLine(track, trackLeft, y, trackRight, y);
                }
                int end = trackLeft + (int)((trackRight - trackLeft) * Math.Max(0.02, Math.Min(1f, progress)));
                using (Pen bar = new Pen(UiTheme.Accent, 2f))
                {
                    g.DrawLine(bar, trackLeft, y, end, y);
                }
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) try { UiTheme.SetRounded(this, 16); } catch { }
        }
    }
}