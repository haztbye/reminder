using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>设置窗口：编辑计划方案、铃声、语音与开关。</summary>
    public class SettingsForm : Form
    {
        private readonly AppConfig working;
        private readonly string initialPlanName;
        private readonly AudioService audio;
        private readonly Action<AppConfig> onSaved;

        private ComboBox cboProfile;
        private Label lblActivePlan;
        private DataGridView grid;
        private Button btnDeleteProfile;
        private Button btnRenameProfile;
        private Button btnRestoreDefault;

        private TextBox txtSound;
        private CheckBox chkLoop;
        private NumericUpDown numRing;
        private CheckBox chkSpeak;
        private ComboBox cboVoice;
        private Button btnVoiceTest;
        private CheckBox chkEnd;
        private CheckBox chkBar;
        private CheckBox chkAuto;
        private TabControl tabs;
        private int lastProfileIndex = -1;
        private bool suppressProfileCommit;

        public SettingsForm(AppConfig workingConfig, string activePlanName, AudioService audioService, Action<AppConfig> savedCallback)
        {
            working = workingConfig;
            initialPlanName = activePlanName;
            audio = audioService;
            onSaved = savedCallback;
            working.Normalize();

            SuspendLayout();
            Text = "考研提醒 · 设置";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(840, 740);
            BackColor = UiTheme.Background;
            Font = UiTheme.F10;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 84;
            header.BackColor = Color.White;
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.Border, 1f)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            Controls.Add(header);
            Label title = UiTheme.MakeLabel("设置", UiTheme.F16B, UiTheme.TextPrimary);
            title.Location = new Point(24, 14);
            header.Controls.Add(title);
            Label sub = UiTheme.MakeLabel("修改计划与提醒方式，保存后立即生效", UiTheme.F9, UiTheme.TextLight);
            sub.Location = new Point(24, 52);
            header.Controls.Add(sub);

            tabs = new TabControl();
            tabs.Location = new Point(16, 98);
            tabs.Size = new Size(808, 560);
            tabs.Font = UiTheme.F10;
            Controls.Add(tabs);

            TabPage tabPlan = new TabPage("计划与方案");
            tabPlan.BackColor = UiTheme.Background;
            tabPlan.Padding = new Padding(0);
            tabs.TabPages.Add(tabPlan);

            TabPage tabSound = new TabPage("提醒与铃声");
            tabSound.BackColor = UiTheme.Background;
            tabSound.Padding = new Padding(0);
            tabs.TabPages.Add(tabSound);

            BuildPlanTab(tabPlan);
            BuildSoundTab(tabSound);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 76;
            footer.BackColor = Color.White;
            footer.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.Border, 1f)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            Controls.Add(footer);

            Label footerHint = UiTheme.MakeLabel("配置文件 config.xml 保存在程序目录", UiTheme.F9, UiTheme.TextLight);
            footerHint.Location = new Point(24, 28);
            footerHint.AutoSize = false;
            footerHint.Width = 500;
            footerHint.Height = 20;
            footerHint.AutoEllipsis = true;
            footer.Controls.Add(footerHint);

            Button btnCancel = UiTheme.MakeButton("取消", false, 88, 36);
            btnCancel.Location = new Point(footer.Width - 24 - 88, 20);
            btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnCancel.Click += delegate { Close(); };
            footer.Controls.Add(btnCancel);

            Button btnSave = UiTheme.MakeButton("保存", true, 100, 36);
            btnSave.Location = new Point(footer.Width - 24 - 88 - 12 - 100, 20);
            btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnSave.Click += delegate { TrySave(); };
            footer.Controls.Add(btnSave);
            footer.Resize += delegate
            {
                btnCancel.Left = footer.Width - 24 - btnCancel.Width;
                btnSave.Left = btnCancel.Left - 12 - btnSave.Width;
            };

            FillProfileCombo();
            lastProfileIndex = cboProfile.SelectedIndex;
            suppressProfileCommit = true;
            LoadProfileToGrid(working.GetPlan(initialPlanName));
            suppressProfileCommit = false;
            LoadSettingsToControls();
            UiScale.ScaleForm(this);

            ResumeLayout(false);
            PerformLayout();
        }

        public void SelectTab(int index)
        {
            if (tabs == null || tabs.TabPages.Count == 0) return;
            if (index < 0) index = 0;
            if (index >= tabs.TabPages.Count) index = tabs.TabPages.Count - 1;
            tabs.SelectedIndex = index;
        }

        private void BuildPlanTab(TabPage page)
        {
            Label lblPlan = UiTheme.MakeLabel("方案", UiTheme.F10, UiTheme.TextPrimary);
            lblPlan.Location = new Point(16, 20);
            page.Controls.Add(lblPlan);

            cboProfile = new ComboBox();
            cboProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            cboProfile.Font = UiTheme.F10;
            cboProfile.Location = new Point(64, 16);
            cboProfile.Width = 230;
            cboProfile.SelectedIndexChanged += delegate { OnProfileSelectionChanged(); };
            page.Controls.Add(cboProfile);

            btnRenameProfile = UiTheme.MakeButton("重命名", false, 76, 30);
            btnRenameProfile.Location = new Point(304, 15);
            btnRenameProfile.Font = UiTheme.F9;
            btnRenameProfile.Click += delegate { RenameProfile(); };
            page.Controls.Add(btnRenameProfile);

            btnDeleteProfile = UiTheme.MakeButton("删除", false, 64, 30);
            btnDeleteProfile.Location = new Point(btnRenameProfile.Right + 8, 15);
            btnDeleteProfile.Font = UiTheme.F9;
            btnDeleteProfile.Click += delegate { DeleteProfile(); };
            page.Controls.Add(btnDeleteProfile);

            Button btnNewProfile = UiTheme.MakeButton("新建方案", false, 88, 30);
            btnNewProfile.Location = new Point(btnDeleteProfile.Right + 8, 15);
            btnNewProfile.Font = UiTheme.F9;
            btnNewProfile.Click += delegate { NewProfile(); };
            page.Controls.Add(btnNewProfile);

            lblActivePlan = UiTheme.MakeLabel("", UiTheme.F9, UiTheme.TextSecondary);
            lblActivePlan.Location = new Point(btnNewProfile.Right + 16, 21);
            page.Controls.Add(lblActivePlan);

            grid = new DataGridView();
            grid.Location = new Point(16, 58);
            grid.Size = new Size(760, 358);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.Font = UiTheme.F10;
            grid.GridColor = UiTheme.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 244, 246);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.F9B;
            grid.RowTemplate.Height = 32;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            DataGridViewCheckBoxColumn colEnabled = new DataGridViewCheckBoxColumn();
            colEnabled.HeaderText = "启用";
            colEnabled.FillWeight = 10;
            colEnabled.MinimumWidth = 50;
            grid.Columns.Add(colEnabled);

            DataGridViewTextBoxColumn colStart = new DataGridViewTextBoxColumn();
            colStart.HeaderText = "开始";
            colStart.FillWeight = 15;
            colStart.MinimumWidth = 78;
            grid.Columns.Add(colStart);

            DataGridViewTextBoxColumn colEnd = new DataGridViewTextBoxColumn();
            colEnd.HeaderText = "结束";
            colEnd.FillWeight = 15;
            colEnd.MinimumWidth = 78;
            grid.Columns.Add(colEnd);

            DataGridViewTextBoxColumn colTitle = new DataGridViewTextBoxColumn();
            colTitle.HeaderText = "事项（如：学习 / 吃饭 / 晚自习 1）";
            colTitle.FillWeight = 60;
            colTitle.MinimumWidth = 220;
            grid.Columns.Add(colTitle);

            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            page.Controls.Add(grid);

            Button btnAdd = UiTheme.MakeButton("添加一行", false, 92, 32);
            btnAdd.Location = new Point(16, 434);
            btnAdd.Font = UiTheme.F9;
            btnAdd.Click += delegate { AddRow(); };
            page.Controls.Add(btnAdd);

            Button btnDelRow = UiTheme.MakeButton("删除选中行", false, 104, 32);
            btnDelRow.Location = new Point(btnAdd.Right + 8, 434);
            btnDelRow.Font = UiTheme.F9;
            btnDelRow.Click += delegate { DeleteRow(); };
            page.Controls.Add(btnDelRow);

            Button btnUp = UiTheme.MakeButton("上移", false, 64, 32);
            btnUp.Location = new Point(btnDelRow.Right + 8, 434);
            btnUp.Font = UiTheme.F9;
            btnUp.Click += delegate { MoveRow(-1); };
            page.Controls.Add(btnUp);

            Button btnDown = UiTheme.MakeButton("下移", false, 64, 32);
            btnDown.Location = new Point(btnUp.Right + 8, 434);
            btnDown.Font = UiTheme.F9;
            btnDown.Click += delegate { MoveRow(1); };
            page.Controls.Add(btnDown);

            btnRestoreDefault = UiTheme.MakeButton("恢复初始计划", false, 118, 32);
            btnRestoreDefault.Location = new Point(btnDown.Right + 8, 434);
            btnRestoreDefault.Font = UiTheme.F9;
            btnRestoreDefault.Click += delegate { RestoreDefaultPlan(); };
            page.Controls.Add(btnRestoreDefault);

            Label hint = UiTheme.MakeLabel("提示：休息日可在托盘右键「今日计划方案」切换，只影响当天，第二天自动回到每日计划。", UiTheme.F9, UiTheme.TextSecondary);
            hint.Location = new Point(16, 478);
            hint.AutoSize = false;
            hint.Width = 760;
            hint.Height = 24;
            page.Controls.Add(hint);
        }

        private void BuildSoundTab(TabPage page)
        {
            GroupBox gbSound = new GroupBox();
            gbSound.Text = "铃声";
            gbSound.Font = UiTheme.F10B;
            gbSound.ForeColor = UiTheme.TextPrimary;
            gbSound.Location = new Point(16, 14);
            gbSound.Size = new Size(760, 190);
            page.Controls.Add(gbSound);

            Label lblSound = UiTheme.MakeLabel("铃声文件", UiTheme.F10, UiTheme.TextPrimary);
            lblSound.Location = new Point(18, 36);
            gbSound.Controls.Add(lblSound);

            txtSound = new TextBox();
            txtSound.Font = UiTheme.F9;
            txtSound.Location = new Point(96, 32);
            txtSound.Width = 430;
            txtSound.ReadOnly = true;
            gbSound.Controls.Add(txtSound);

            Button btnBrowse = UiTheme.MakeButton("浏览…", false, 70, 30);
            btnBrowse.Location = new Point(txtSound.Right + 10, 31);
            btnBrowse.Font = UiTheme.F9;
            btnBrowse.Click += delegate { BrowseSound(); };
            gbSound.Controls.Add(btnBrowse);

            Button btnPreview = UiTheme.MakeButton("试听", false, 58, 30);
            btnPreview.Location = new Point(btnBrowse.Right + 8, 31);
            btnPreview.Font = UiTheme.F9;
            btnPreview.Click += delegate { PreviewSound(); };
            gbSound.Controls.Add(btnPreview);

            Button btnResetSound = UiTheme.MakeButton("恢复系统铃声", false, 112, 30);
            btnResetSound.Location = new Point(btnBrowse.Left, 74);
            btnResetSound.Font = UiTheme.F9;
            btnResetSound.Click += delegate { txtSound.Text = ""; };
            gbSound.Controls.Add(btnResetSound);

            Label lblSoundHint = UiTheme.MakeLabel("支持 .wav / .mp3；留空则使用系统闹钟声（Alarm01.wav）", UiTheme.F9, UiTheme.TextLight);
            lblSoundHint.Location = new Point(18, 78);
            gbSound.Controls.Add(lblSoundHint);

            chkLoop = new CheckBox();
            chkLoop.Text = "循环响铃，直到点“知道了”";
            chkLoop.Font = UiTheme.F10;
            chkLoop.AutoSize = true;
            chkLoop.Location = new Point(18, 116);
            gbSound.Controls.Add(chkLoop);

            numRing = new NumericUpDown();
            numRing.Font = UiTheme.F10;
            numRing.Location = new Point(330, 114);
            numRing.Width = 64;
            numRing.Minimum = 5;
            numRing.Maximum = 300;
            numRing.Value = 30;
            gbSound.Controls.Add(numRing);

            Label lblRing = UiTheme.MakeLabel("秒后自动停止（不循环时只响一次）", UiTheme.F9, UiTheme.TextLight);
            lblRing.Location = new Point(400, 118);
            gbSound.Controls.Add(lblRing);

            Label lblSoundNote = UiTheme.MakeLabel("语音播报开启时，铃声会同时作为背景提示音；可在下方单独关闭语音。", UiTheme.F9, UiTheme.TextLight);
            lblSoundNote.Location = new Point(18, 150);
            gbSound.Controls.Add(lblSoundNote);

            GroupBox gbVoice = new GroupBox();
            gbVoice.Text = "语音与显示";
            gbVoice.Font = UiTheme.F10B;
            gbVoice.ForeColor = UiTheme.TextPrimary;
            gbVoice.Location = new Point(16, 216);
            gbVoice.Size = new Size(760, 250);
            page.Controls.Add(gbVoice);

            chkSpeak = new CheckBox();
            chkSpeak.Text = "中文语音播报（例如：现在是 9 点 10 分，学习时间到了）";
            chkSpeak.Font = UiTheme.F10;
            chkSpeak.AutoSize = true;
            chkSpeak.Location = new Point(18, 30);
            gbVoice.Controls.Add(chkSpeak);

            Label lblVoice = UiTheme.MakeLabel("语音", UiTheme.F10, UiTheme.TextPrimary);
            lblVoice.Location = new Point(18, 72);
            gbVoice.Controls.Add(lblVoice);

            cboVoice = new ComboBox();
            cboVoice.DropDownStyle = ComboBoxStyle.DropDownList;
            cboVoice.Font = UiTheme.F9;
            cboVoice.Location = new Point(64, 68);
            cboVoice.Width = 470;
            gbVoice.Controls.Add(cboVoice);

            btnVoiceTest = UiTheme.MakeButton("试听语音", false, 88, 30);
            btnVoiceTest.Location = new Point(cboVoice.Right + 12, 67);
            btnVoiceTest.Font = UiTheme.F9;
            btnVoiceTest.Click += delegate { PreviewVoice(); };
            gbVoice.Controls.Add(btnVoiceTest);

            Label lblVoiceHint = UiTheme.MakeLabel("找不到中文语音包时只会响铃", UiTheme.F9, UiTheme.TextLight);
            lblVoiceHint.Location = new Point(18, 104);
            gbVoice.Controls.Add(lblVoiceHint);

            chkEnd = new CheckBox();
            chkEnd.Text = "结束时间轻提醒（右下角小提示 + 短提示音，不弹大窗）";
            chkEnd.Font = UiTheme.F10;
            chkEnd.AutoSize = true;
            chkEnd.Location = new Point(18, 136);
            gbVoice.Controls.Add(chkEnd);

            chkBar = new CheckBox();
            chkBar.Text = "显示桌面倒计时条（可拖动，右键可隐藏）";
            chkBar.Font = UiTheme.F10;
            chkBar.AutoSize = true;
            chkBar.Location = new Point(18, 170);
            gbVoice.Controls.Add(chkBar);

            chkAuto = new CheckBox();
            chkAuto.Text = "开机自动启动（登录 Windows 后自动运行，无需管理员权限）";
            chkAuto.Font = UiTheme.F10;
            chkAuto.AutoSize = true;
            chkAuto.Location = new Point(18, 204);
            gbVoice.Controls.Add(chkAuto);

            Label lblConfigNote = UiTheme.MakeLabel("所有设置和日志都保存在本程序目录，不写 C 盘其他位置。", UiTheme.F9, UiTheme.TextLight);
            lblConfigNote.Location = new Point(18, 480);
            page.Controls.Add(lblConfigNote);
        }

        // ---------------- 计划方案 ----------------

        private void FillProfileCombo()
        {
            cboProfile.Items.Clear();
            foreach (PlanProfile p in working.Plans) cboProfile.Items.Add(p.Name);
            int index = 0;
            for (int i = 0; i < working.Plans.Count; i++)
            {
                if (string.Equals(working.Plans[i].Name, initialPlanName, StringComparison.Ordinal)) { index = i; break; }
                if (string.Equals(working.Plans[i].Name, AppConfig.DefaultPlanName, StringComparison.Ordinal)) index = i;
            }
            if (cboProfile.Items.Count > 0) cboProfile.SelectedIndex = index;
        }

        private PlanProfile SelectedProfile
        {
            get
            {
                int i = cboProfile.SelectedIndex;
                if (i < 0 || i >= working.Plans.Count) return null;
                return working.Plans[i];
            }
        }

        private void OnProfileSelectionChanged()
        {
            if (suppressProfileCommit)
            {
                PlanProfile current = SelectedProfile;
                if (current != null) lastProfileIndex = cboProfile.SelectedIndex;
                return;
            }
            if (lastProfileIndex >= 0 && lastProfileIndex < working.Plans.Count && lastProfileIndex != cboProfile.SelectedIndex)
            {
                PlanProfile previous = working.Plans[lastProfileIndex];
                if (!CommitGridToProfile(previous))
                {
                    suppressProfileCommit = true;
                    cboProfile.SelectedIndex = lastProfileIndex;
                    suppressProfileCommit = false;
                    return;
                }
            }
            lastProfileIndex = cboProfile.SelectedIndex;
            PlanProfile p = SelectedProfile;
            if (p == null) return;
            LoadProfileToGrid(p);
            UpdateProfileButtons();
        }

        private void UpdateProfileButtons()
        {
            PlanProfile p = SelectedProfile;
            bool isDefault = p != null && string.Equals(p.Name, AppConfig.DefaultPlanName, StringComparison.Ordinal);
            btnDeleteProfile.Enabled = p != null && !isDefault && working.Plans.Count > 1;
            btnRenameProfile.Enabled = p != null;
            btnRestoreDefault.Enabled = isDefault;
            string active = working.ActivePlanName;
            lblActivePlan.Text = "当前：" + active;
        }

        private bool CommitGridToProfile()
        {
            return CommitGridToProfile(SelectedProfile);
        }

        private bool CommitGridToProfile(PlanProfile p)
        {
            if (p == null) return true;
            List<PlanItem> items = new List<PlanItem>();
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow row = grid.Rows[i];
                bool enabled = false;
                object ev = row.Cells[0].Value;
                if (ev is bool) enabled = (bool)ev;
                else if (ev != null) bool.TryParse(ev.ToString(), out enabled);
                string start = Convert.ToString(row.Cells[1].Value);
                string end = Convert.ToString(row.Cells[2].Value);
                string title = Convert.ToString(row.Cells[3].Value);
                start = start == null ? "" : start.Trim().Replace('：', ':').Replace(" ", "");
                end = end == null ? "" : end.Trim().Replace('：', ':').Replace(" ", "");
                title = title == null ? "" : title.Trim();
                if (start.Length == 0 && title.Length == 0 && end.Length == 0)
                {
                    continue;
                }
                TimeSpan st, et;
                if (!TimeUtil.TryParse(start, out st))
                {
                    MessageBox.Show(this, "第 " + (i + 1).ToString() + " 行的开始时间格式不对。\n请使用 24 小时制，例如 08:00。", "时间格式错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    grid.CurrentCell = row.Cells[1];
                    return false;
                }
                if (title.Length == 0)
                {
                    MessageBox.Show(this, "第 " + (i + 1).ToString() + " 行还没有填写事项。", "缺少事项",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    grid.CurrentCell = row.Cells[3];
                    return false;
                }
                PlanItem item = new PlanItem();
                item.Enabled = enabled;
                item.Start = TimeUtil.Format(st);
                item.Title = title;
                if (end.Length > 0)
                {
                    if (!TimeUtil.TryParse(end, out et))
                    {
                        MessageBox.Show(this, "第 " + (i + 1).ToString() + " 行的结束时间格式不对。\n请使用 24 小时制，例如 09:00；留空表示没有结束时间。", "时间格式错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        grid.CurrentCell = row.Cells[2];
                        return false;
                    }
                    if (et <= st)
                    {
                        MessageBox.Show(this, "第 " + (i + 1).ToString() + " 行的结束时间必须晚于开始时间。", "时间顺序错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        grid.CurrentCell = row.Cells[2];
                        return false;
                    }
                    item.End = TimeUtil.Format(et);
                }
                else
                {
                    item.End = "";
                }
                items.Add(item);
            }
            if (items.Count == 0)
            {
                MessageBox.Show(this, "计划里至少要有一项。", "计划为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            items.Sort(PlanItemComparer.Instance);
            p.Items = items;
            return true;
        }

        private void LoadProfileToGrid(PlanProfile p)
        {
            grid.Rows.Clear();
            if (p == null || p.Items == null) return;
            List<PlanItem> items = new List<PlanItem>();
            foreach (PlanItem it in p.Items) if (it != null) items.Add(it);
            items.Sort(PlanItemComparer.Instance);
            foreach (PlanItem it in items)
            {
                int idx = grid.Rows.Add();
                grid.Rows[idx].Cells[0].Value = it.Enabled;
                grid.Rows[idx].Cells[1].Value = it.Start;
                grid.Rows[idx].Cells[2].Value = it.End;
                grid.Rows[idx].Cells[3].Value = it.Title;
            }
        }

        private string AskName(string title, string prompt, string initial)
        {
            using (InputDialog dlg = new InputDialog(title, prompt, initial))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) return dlg.Value.Trim();
            }
            return null;
        }

        private bool ProfileNameExists(string name, PlanProfile except)
        {
            foreach (PlanProfile p in working.Plans)
            {
                if (p == except) continue;
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void NewProfile()
        {
            if (!CommitGridToProfile()) return;
            string name = AskName("新建方案", "给新方案起个名字（例如：休息日）：", "休息日");
            if (string.IsNullOrEmpty(name)) return;
            if (ProfileNameExists(name, null))
            {
                MessageBox.Show(this, "已经有同名方案了，请换一个名字。", "方案重名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PlanProfile source = SelectedProfile;
            PlanProfile np = new PlanProfile();
            np.Name = name;
            np.Items = source != null ? source.CloneProfile().Items : AppConfig.CreateDefaultDailyItems();
            working.Plans.Add(np);
            suppressProfileCommit = true;
            FillProfileCombo();
            cboProfile.SelectedIndex = working.Plans.Count - 1;
            lastProfileIndex = cboProfile.SelectedIndex;
            suppressProfileCommit = false;
        }

        private void RenameProfile()
        {
            PlanProfile p = SelectedProfile;
            if (p == null) return;
            if (!CommitGridToProfile()) return;
            string name = AskName("重命名方案", "新的方案名称：", p.Name);
            if (string.IsNullOrEmpty(name) || string.Equals(name, p.Name, StringComparison.Ordinal)) return;
            if (ProfileNameExists(name, p))
            {
                MessageBox.Show(this, "已经有同名方案了，请换一个名字。", "方案重名", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            CommitGridToProfile(p);
            string oldName = p.Name;
            p.Name = name;
            if (working.TodayPlan != null && string.Equals(working.TodayPlan.PlanName, oldName, StringComparison.Ordinal))
                working.TodayPlan.PlanName = name;
            int i = cboProfile.SelectedIndex;
            FillProfileCombo();
            cboProfile.SelectedIndex = Math.Min(i, cboProfile.Items.Count - 1);
            UpdateProfileButtons();
        }

        private void DeleteProfile()
        {
            PlanProfile p = SelectedProfile;
            if (p == null) return;
            if (string.Equals(p.Name, AppConfig.DefaultPlanName, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "默认的“每日计划”不能删除。", "无法删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (working.Plans.Count <= 1) return;
            if (MessageBox.Show(this, "确定要删除方案“" + p.Name + "”吗？", "删除方案",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (working.TodayPlan != null && string.Equals(working.TodayPlan.PlanName, p.Name, StringComparison.Ordinal))
                working.TodayPlan.PlanName = AppConfig.DefaultPlanName;
            working.Plans.Remove(p);
            suppressProfileCommit = true;
            FillProfileCombo();
            cboProfile.SelectedIndex = 0;
            lastProfileIndex = 0;
            suppressProfileCommit = false;
            UpdateProfileButtons();
        }

        private void AddRow()
        {
            string start = "08:00";
            string end = "09:00";
            if (grid.Rows.Count > 0)
            {
                DataGridViewRow last = grid.Rows[grid.Rows.Count - 1];
                TimeSpan st, et;
                if (TimeUtil.TryParse(Convert.ToString(last.Cells[2].Value), out et) && et < TimeSpan.FromHours(23))
                {
                    start = TimeUtil.Format(et);
                    end = TimeUtil.Format(et.Add(TimeSpan.FromHours(1)));
                }
                else if (TimeUtil.TryParse(Convert.ToString(last.Cells[1].Value), out st) && st < TimeSpan.FromHours(23))
                {
                    start = TimeUtil.Format(st.Add(TimeSpan.FromHours(1)));
                    end = TimeUtil.Format(st.Add(TimeSpan.FromHours(2)));
                }
            }
            int idx = grid.Rows.Add();
            grid.Rows[idx].Cells[0].Value = true;
            grid.Rows[idx].Cells[1].Value = start;
            grid.Rows[idx].Cells[2].Value = end;
            grid.Rows[idx].Cells[3].Value = "学习";
            try { grid.CurrentCell = grid.Rows[idx].Cells[3]; } catch { }
        }

        private void DeleteRow()
        {
            if (grid.CurrentRow == null) return;
            grid.Rows.Remove(grid.CurrentRow);
        }

        private void MoveRow(int delta)
        {
            if (grid.CurrentRow == null) return;
            int index = grid.CurrentRow.Index;
            int target = index + delta;
            if (target < 0 || target >= grid.Rows.Count) return;
            object[] values = new object[4];
            for (int c = 0; c < 4; c++) values[c] = grid.Rows[index].Cells[c].Value;
            grid.Rows.RemoveAt(index);
            grid.Rows.Insert(target, values);
            try { grid.CurrentCell = grid.Rows[target].Cells[3]; } catch { }
        }

        private void RestoreDefaultPlan()
        {
            PlanProfile p = SelectedProfile;
            if (p == null) return;
            if (!string.Equals(p.Name, AppConfig.DefaultPlanName, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "“恢复初始计划”只对默认的“每日计划”生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this, "确定恢复成你最初提供的每日计划吗？当前修改会丢失。", "恢复初始计划",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            p.Items = AppConfig.CreateDefaultDailyItems();
            LoadProfileToGrid(p);
        }

        // ---------------- 提醒设置 ----------------

        private void LoadSettingsToControls()
        {
            txtSound.Text = working.Settings.SoundFile == null ? "" : working.Settings.SoundFile;
            chkLoop.Checked = working.Settings.LoopSound;
            int ring = working.Settings.RingSeconds;
            if (ring < (int)numRing.Minimum) ring = 30;
            numRing.Value = Math.Min((int)numRing.Maximum, Math.Max((int)numRing.Minimum, ring));
            chkSpeak.Checked = working.Settings.SpeakEnabled;
            chkEnd.Checked = working.Settings.RemindAtEnd;
            chkBar.Checked = working.Settings.ShowCountdownBar;
            chkAuto.Checked = working.Settings.AutoStart;

            List<VoiceOption> voices = new List<VoiceOption>();
            try { voices = audio == null ? new List<VoiceOption>() : audio.GetVoices(); } catch { }
            cboVoice.Items.Clear();
            foreach (VoiceOption v in voices) cboVoice.Items.Add(v);
            if (cboVoice.Items.Count == 0)
            {
                chkSpeak.Enabled = false;
                cboVoice.Enabled = false;
                btnVoiceTest.Enabled = false;
                chkSpeak.Text = "中文语音播报（此电脑未找到语音包，暂不可用）";
            }
            else
            {
                int selected = 0;
                for (int i = 0; i < voices.Count; i++)
                {
                    if (voices[i].Description == working.Settings.VoiceName) { selected = i; break; }
                    if (voices[i].IsChinese && selected == 0) selected = i;
                }
                cboVoice.SelectedIndex = selected;
            }
            UpdateProfileButtons();
        }

        private void BrowseSound()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择提醒铃声";
                dlg.Filter = "音频文件 (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (!AudioService.IsSupportedExtension(dlg.FileName))
                    {
                        MessageBox.Show(this, "目前支持 .wav / .mp3 / .wma 格式。\n可以先用格式转换工具转成 mp3 再选择。", "格式不支持",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    txtSound.Text = dlg.FileName;
                }
            }
        }

        private void PreviewSound()
        {
            try
            {
                if (audio == null) return;
                string file = txtSound.Text.Trim();
                if (file.Length > 0 && !File.Exists(file))
                {
                    MessageBox.Show(this, "找不到这个铃声文件，请重新选择。", "文件不存在", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!audio.Preview(file))
                {
                    MessageBox.Show(this, "试听失败：系统无法播放这个文件。\n可以换一个 .wav 或 .mp3 试试。", "试听失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "试听失败：" + ex.Message, "试听失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PreviewVoice()
        {
            try
            {
                if (audio == null) return;
                VoiceOption v = cboVoice.SelectedItem as VoiceOption;
                bool ok = audio.Speak("现在是九点十分，学习时间到了", v == null ? "" : v.Description, 100, 0);
                if (!ok)
                {
                    MessageBox.Show(this, "语音试听失败：此电脑的语音组件暂时不可用。\n可以只使用铃声提醒。", "语音不可用",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "语音试听失败：" + ex.Message, "语音不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TrySave()
        {
            if (!CommitGridToProfile()) return;
            AppSettings s = working.Settings;
            string sound = txtSound.Text.Trim();
            if (sound.Length > 0 && !File.Exists(sound))
            {
                MessageBox.Show(this, "铃声文件不存在，请重新选择，或点“恢复系统铃声”。", "铃声不可用",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            s.SoundFile = sound;
            s.LoopSound = chkLoop.Checked;
            s.RingSeconds = (int)numRing.Value;
            s.SpeakEnabled = chkSpeak.Checked && chkSpeak.Enabled;
            VoiceOption voice = cboVoice.SelectedItem as VoiceOption;
            s.VoiceName = voice == null ? "" : voice.Description;
            s.RemindAtEnd = chkEnd.Checked;
            s.ShowCountdownBar = chkBar.Checked;
            s.AutoStart = chkAuto.Checked;

            if (onSaved != null) onSaved(working);
            Close();
        }
    }

    /// <summary>简单的文本输入对话框（新建/重命名方案用）。</summary>
    public class InputDialog : Form
    {
        private readonly TextBox txt;

        public string Value
        {
            get { return txt.Text; }
        }

        public InputDialog(string title, string prompt, string initial)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = UiTheme.F10;
            ClientSize = new Size(400, 150);

            Label lbl = UiTheme.MakeLabel(prompt, UiTheme.F10, UiTheme.TextPrimary);
            lbl.Location = new Point(20, 18);
            lbl.AutoSize = false;
            lbl.Width = 360;
            lbl.Height = 24;
            Controls.Add(lbl);

            txt = new TextBox();
            txt.Font = UiTheme.F10;
            txt.Location = new Point(20, 50);
            txt.Width = 360;
            txt.Text = initial == null ? "" : initial;
            Controls.Add(txt);

            Button ok = UiTheme.MakeButton("确定", true, 88, 34);
            ok.Location = new Point(200, 100);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = UiTheme.MakeButton("取消", false, 88, 34);
            cancel.Location = new Point(296, 100);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { txt.SelectAll(); txt.Focus(); };
        }
    }
}