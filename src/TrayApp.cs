using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KaoYanReminder
{
    /// <summary>托盘程序主逻辑：调度、提醒弹窗、桌面倒计时条、方案切换。</summary>
    public class TrayApp : ApplicationContext
    {
        private AppConfig config;
        private readonly ScheduleEngine engine;
        private readonly AudioService audio;
        private NotifyIcon tray;
        private readonly System.Windows.Forms.Timer tick;
        private readonly Control marshal;
        private readonly EventWaitHandle evShowMain;
        private readonly EventWaitHandle evShowSettings;
        private readonly EventWaitHandle evShowTest;
        
        private readonly bool silentStart;
        private readonly bool mute;
        private readonly int smokeSeconds;

        private ContextMenuStrip trayMenu;
        private CountdownBar bar;
        private MainForm mainForm;
        private SettingsForm settingsForm;
        private readonly List<ReminderForm> activeReminders = new List<ReminderForm>();
        private readonly List<EndToastForm> activeToasts = new List<EndToastForm>();

        private DateTime lastTooltipUpdate = DateTime.MinValue;
        private DateTime lastTickDate = DateTime.MinValue;
        private bool wasPaused;
        private bool exiting;
        private bool disposed;

        public TrayApp(EventWaitHandle showMain, EventWaitHandle showSettings, EventWaitHandle showTest,
            bool silent, bool openSettings, bool testReminder, bool muteSound, int smoke)
        {
            evShowMain = showMain;
            evShowSettings = showSettings;
            evShowTest = showTest;
            silentStart = silent;
            mute = muteSound;
            smokeSeconds = smoke;

            bool createdNew;
            config = AppConfig.Load(AppPaths.ConfigPath, out createdNew);
            if (createdNew) Log.Write("已生成默认配置：" + AppPaths.ConfigPath);
            config.Normalize();
            config.EnsureTodayPlan(DateTime.Today);
            Log.Write("程序启动，方案：" + config.ActivePlanName + "，计划项：" + CountItems(config) + "，配置：" + AppPaths.ConfigPath);

            engine = new ScheduleEngine(config);
            audio = new AudioService(Log.Write);

            marshal = new Control();
            IntPtr handle = marshal.Handle; // 创建消息句柄，供跨线程调用

            SyncAutoStartState();
            BuildTray();
            CreateBar();

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += OnTick;
            tick.Start();

            lastTickDate = DateTime.Today;
            UpdateTrayText(DateTime.Now);
            if (bar != null && config.Settings.ShowCountdownBar)
            {
                bar.ApplyConfig(config);
                bar.UpdateState(DateTime.Now, engine, engine.IsPaused(DateTime.Now));
                bar.ShowBar();
            }

            StartListener();

            if (!silentStart)
            {
                string nextText = BuildNextLine(DateTime.Now);
                ShowBalloon("考研提醒已启动", "今天共 " + CountEnabledItems() + " 项计划。" + nextText, ToolTipIcon.Info);
            }

            if (openSettings)
            {
                try { marshal.BeginInvoke((Action)OpenSettings); } catch { }
            }
            if (testReminder)
            {
                try { marshal.BeginInvoke((Action)ShowTestReminder); } catch { }
            }
            if (smokeSeconds > 0)
            {
                System.Windows.Forms.Timer smokeTimer = new System.Windows.Forms.Timer();
                smokeTimer.Interval = Math.Max(1, smokeSeconds) * 1000;
                smokeTimer.Tick += delegate
                {
                    smokeTimer.Stop();
                    smokeTimer.Dispose();
                    Log.Write("冒烟测试计时结束，正常退出");
                    ExitApp();
                };
                smokeTimer.Start();
            }
        }

        private int CountItems(AppConfig cfg)
        {
            int n = 0;
            if (cfg != null && cfg.Plans != null) foreach (PlanProfile p in cfg.Plans) if (p != null && p.Items != null) n += p.Items.Count;
            return n;
        }

        private int CountEnabledItems()
        {
            int n = 0;
            PlanProfile p = config.GetTodayProfile();
            if (p != null && p.Items != null) foreach (PlanItem it in p.Items) if (it != null && it.Enabled) n++;
            return n;
        }

        // ---------------- 托盘 ----------------

        private void BuildTray()
        {
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (icon == null) icon = SystemIcons.Information;

            tray = new NotifyIcon();
            tray.Icon = icon;
            tray.Text = "考研提醒";
            tray.Visible = true;
            tray.DoubleClick += delegate { OpenMain(); };
            tray.BalloonTipClicked += delegate { OpenMain(); };
            RebuildMenu();
        }

        private void RebuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = UiTheme.F10;
            menu.ShowImageMargin = false;

            ToolStripMenuItem miMain = new ToolStripMenuItem("打开今日计划");
            miMain.Font = new Font(menu.Font, FontStyle.Bold);
            miMain.Click += delegate { OpenMain(); };
            menu.Items.Add(miMain);

            ToolStripMenuItem miProfiles = new ToolStripMenuItem("今日计划方案");
            foreach (PlanProfile p in config.Plans)
            {
                if (p == null) continue;
                string name = p.Name;
                ToolStripMenuItem mi = new ToolStripMenuItem(name);
                mi.Checked = string.Equals(name, engine.ActivePlanName, StringComparison.Ordinal);
                mi.Click += delegate { SwitchTodayPlan(name); };
                miProfiles.DropDownItems.Add(mi);
            }
            miProfiles.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem miEditPlans = new ToolStripMenuItem("编辑计划方案…");
            miEditPlans.Click += delegate { OpenSettings(); };
            miProfiles.DropDownItems.Add(miEditPlans);
            menu.Items.Add(miProfiles);

            ToolStripMenuItem miSettings = new ToolStripMenuItem("设置提醒…");
            miSettings.Click += delegate { OpenSettings(); };
            menu.Items.Add(miSettings);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miPause = new ToolStripMenuItem();
            DateTime now = DateTime.Now;
            if (engine.IsPaused(now) && engine.PausedUntil.HasValue)
            {
                miPause.Text = "恢复提醒（已暂停至 " + TimeUtil.FormatClock(engine.PausedUntil.Value) + "）";
            }
            else
            {
                miPause.Text = "暂停提醒 30 分钟";
            }
            miPause.Click += delegate { PauseToggle(); };
            menu.Items.Add(miPause);

            ToolStripMenuItem miTest = new ToolStripMenuItem("立即测试提醒");
            miTest.Click += delegate { ShowTestReminder(); };
            menu.Items.Add(miTest);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miBar = new ToolStripMenuItem("显示桌面倒计时条");
            miBar.Checked = bar != null && bar.Visible;
            miBar.Click += delegate { ToggleBar(); };
            menu.Items.Add(miBar);

            ToolStripMenuItem miAuto = new ToolStripMenuItem("开机自动启动");
            miAuto.Checked = config.Settings.AutoStart;
            miAuto.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(miAuto);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miLog = new ToolStripMenuItem("打开提醒日志");
            miLog.Click += delegate { OpenLog(); };
            menu.Items.Add(miLog);

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出考研提醒");
            miExit.Click += delegate { ConfirmExit(); };
            menu.Items.Add(miExit);

            ContextMenuStrip old = trayMenu;
            trayMenu = menu;
            tray.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
        }

        private void ToggleAutoStart()
        {
            bool target = !config.Settings.AutoStart;
            try
            {
                AutostartManager.SetEnabled(target);
                config.Settings.AutoStart = target;
                SaveConfigSafe();
            }
            catch (Exception ex)
            {
                Log.Error("设置开机自启", ex);
                MessageBox.Show("设置开机自启失败：" + ex.Message + "\n\n可以手动把程序快捷方式放进启动文件夹。",
                    "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RebuildMenu();
            ShowBalloon("开机自启", target ? "已开启：登录 Windows 后自动在托盘运行。" : "已关闭开机自动启动。", ToolTipIcon.Info);
            Log.Write("开机自启 = " + target);
        }

        private void SyncAutoStartState()
        {
            try
            {
                bool actual = AutostartManager.IsEnabled();
                if (config.Settings.AutoStart != actual)
                {
                    config.Settings.AutoStart = actual;
                    SaveConfigSafe();
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取开机自启状态", ex);
            }
        }

        private void ToggleBar()
        {
            bool show = bar == null || !bar.Visible;
            config.Settings.ShowCountdownBar = show;
            SaveConfigSafe();
            if (show) CreateBar();
            if (bar != null)
            {
                if (show)
                {
                    bar.ApplyConfig(config);
                    bar.UpdateState(DateTime.Now, engine, engine.IsPaused(DateTime.Now));
                    bar.ShowBar();
                }
                else
                {
                    bar.Hide();
                }
            }
            RebuildMenu();
        }

        private void CreateBar()
        {
            if (bar != null && !bar.IsDisposed) return;
            bar = new CountdownBar(OpenMain, OpenSettings, PauseToggle, HideBar, OnBarMoved);
            bar.DoubleClicked += delegate { OpenMain(); };
            try { bar.Icon = tray.Icon; } catch { }
        }

        private void HideBar()
        {
            config.Settings.ShowCountdownBar = false;
            SaveConfigSafe();
            if (bar != null) bar.Hide();
            RebuildMenu();
            ShowBalloon("倒计时条已隐藏", "可在托盘菜单里重新显示。", ToolTipIcon.Info);
        }

        private void OnBarMoved()
        {
            if (bar == null) return;
            config.Settings.BarX = bar.Left;
            config.Settings.BarY = bar.Top;
            SaveConfigSafe();
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(AppPaths.LogPath))
                {
                    MessageBox.Show("还没有日志文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start("notepad.exe", "\"" + AppPaths.LogPath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开日志：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- 定时循环 ----------------

        private void OnTick(object sender, EventArgs e)
        {
            if (exiting) return;
            try
            {
                DateTime now = DateTime.Now;
                if (now.Date != lastTickDate)
                {
                    lastTickDate = now.Date;
                    bool changed = config.EnsureTodayPlan(now);
                    if (changed) SaveConfigSafe();
                    RefreshUi(now);
                }

                bool paused = engine.IsPaused(now);
                if (wasPaused && !paused)
                {
                    RebuildMenu();
                    ShowBalloon("提醒已恢复", "继续按计划提醒。", ToolTipIcon.Info);
                }
                wasPaused = paused;

                List<DueReminder> due = engine.GetDue(now);
                foreach (DueReminder d in due)
                {
                    if (d.Kind == ReminderKind.End) ShowEndToast(d);
                    else ShowStartReminder(d);
                }

                if (bar != null && !bar.IsDisposed && bar.Visible)
                {
                    bar.UpdateState(now, engine, engine.IsPaused(now));
                }
                if ((now - lastTooltipUpdate).TotalSeconds >= 5)
                {
                    lastTooltipUpdate = now;
                    UpdateTrayText(now);
                }
            }
            catch (Exception ex)
            {
                Log.Error("定时循环", ex);
            }
        }

        private void RefreshUi(DateTime now)
        {
            if (bar != null && !bar.IsDisposed && bar.Visible) bar.UpdateState(now, engine, engine.IsPaused(now));
            if (mainForm != null && !mainForm.IsDisposed) mainForm.ApplyConfig(config);
            RebuildMenu();
            UpdateTrayText(now);
        }

        private void UpdateTrayText(DateTime now)
        {
            if (tray == null) return;
            string text;
            if (engine.IsPaused(now) && engine.PausedUntil.HasValue)
            {
                text = "考研提醒 · 已暂停至 " + TimeUtil.FormatClock(engine.PausedUntil.Value);
            }
            else
            {
                PlanItem current = engine.GetCurrent(now);
                DateTime nextWhen;
                PlanItem next = engine.GetNext(now, out nextWhen);
                if (current != null)
                {
                    TimeSpan s, e;
                    current.TryGetTimes(out s, out e);
                    text = "考研提醒 · " + current.Title + " 剩余 " + TimeUtil.FormatCountdown(now.Date + e - now);
                }
                else if (next != null)
                {
                    string day = nextWhen.Date > now.Date ? "明天 " : "";
                    text = "考研提醒 · 下一项 " + day + TimeUtil.FormatClock(nextWhen) + " " + next.Title;
                }
                else
                {
                    text = "考研提醒 · 今日计划已完成";
                }
            }
            if (text.Length > 63) text = text.Substring(0, 63);
            try { tray.Text = text; } catch { }
        }

        // ---------------- 提醒展示 ----------------

        private string BuildNextLine(DateTime now)
        {
            DateTime when;
            PlanItem next = engine.GetNext(now, out when);
            if (next == null) return "今天计划已完成，好好休息。";
            if (when.Date > now.Date)
                return "下一项：明天 " + TimeUtil.FormatClock(when) + " " + next.Title;
            return "下一项：" + TimeUtil.FormatClock(when) + " " + next.Title + " · " + TimeUtil.FormatApprox(when - now);
        }

        private static string CleanForSpeech(string title)
        {
            if (string.IsNullOrEmpty(title)) return "计划事项";
            return title.Replace("+", "和").Replace("、", "，").Replace("/", "、").Trim();
        }

        private string BuildSpeakText(PlanItem item, DateTime now, bool snooze)
        {
            string prefix = snooze ? "稍后提醒，" : "现在是" + TimeUtil.ChineseTime(now) + "，";
            return prefix + CleanForSpeech(item.Title) + "，开始吧";
        }

        private void ShowStartReminder(DueReminder due)
        {
            PlanItem item = due.Item;
            if (item == null) return;
            DateTime now = DateTime.Now;
            bool snooze = due.Kind == ReminderKind.Snooze;
            ReminderInfo info = new ReminderInfo();
            info.IsSnooze = snooze;
            info.HeaderText = snooze ? "稍后提醒" : "开始提醒";
            info.TimeText = TimeUtil.FormatClock(due.DueAt);
            info.Title = item.Title;
            info.RangeText = TimeUtil.FormatRange(item);
            info.NextText = BuildNextLine(now);
            info.PlanName = due.PlanName;
            Log.Write("触发提醒：" + (snooze ? "稍后提醒 " : "") + info.TimeText + " " + item.Title);

            audio.PlayRingtone(config.Settings.SoundFile, config.Settings.LoopSound, config.Settings.RingSeconds, mute);
            if (config.Settings.SpeakEnabled && !mute)
            {
                audio.Speak(BuildSpeakText(item, now, snooze), config.Settings.VoiceName, 100, 0);
            }

            ReminderForm form = new ReminderForm(info);
            form.DismissClicked += delegate
            {
                audio.StopAudio();
                audio.StopSpeech();
            };
            form.SnoozeClicked += delegate
            {
                audio.StopAudio();
                audio.StopSpeech();
                engine.AddSnooze(item, DateTime.Now.AddMinutes(5), due.PlanName);
                Log.Write("稍后提醒 5 分钟：" + item.Title);
                ShowBalloon("稍后提醒", item.Title + "：5 分钟后再提醒你。", ToolTipIcon.Info);
            };
            form.FormClosed += delegate
            {
                activeReminders.Remove(form);
                PositionActiveWindows();
                if (activeReminders.Count == 0)
                {
                    audio.StopAudio();
                    audio.StopSpeech();
                }
            };
            activeReminders.Add(form);
            PositionActiveWindows();
            try
            {
                form.Show();
                form.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("显示提醒窗口", ex);
            }
        }

        private void ShowEndToast(DueReminder due)
        {
            PlanItem item = due.Item;
            if (item == null) return;
            ReminderInfo info = new ReminderInfo();
            info.IsEnd = true;
            info.HeaderText = "结束提醒";
            info.Title = item.Title + " 结束";
            info.NextText = BuildNextLine(DateTime.Now);
            info.PlanName = due.PlanName;
            Log.Write("结束提醒：" + TimeUtil.FormatClock(due.DueAt) + " " + item.Title);

            if (activeReminders.Count == 0)
            {
                audio.PlayOnce(config.Settings.SoundFile, 4, mute);
            }

            EndToastForm form = new EndToastForm(info);
            form.FormClosed += delegate
            {
                activeToasts.Remove(form);
                PositionActiveWindows();
            };
            activeToasts.Add(form);
            PositionActiveWindows();
            try { form.Show(); } catch (Exception ex) { Log.Error("显示结束提示", ex); }
        }

        private void PositionActiveWindows()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int right = area.Right - 24;
            int y = area.Bottom - 24;
            foreach (EndToastForm t in activeToasts)
            {
                if (t == null || t.IsDisposed) continue;
                t.Left = right - t.Width;
                t.Top = y - t.Height;
                y -= t.Height + 10;
            }
            foreach (ReminderForm r in activeReminders)
            {
                if (r == null || r.IsDisposed) continue;
                r.Left = right - r.Width;
                r.Top = y - r.Height;
                y -= r.Height + 10;
            }
        }

        private void ShowTestReminder()
        {
            DateTime now = DateTime.Now;
            ReminderInfo info = new ReminderInfo();
            info.IsTest = true;
            info.HeaderText = "测试提醒";
            info.TimeText = TimeUtil.FormatClock(now);
            info.Title = "学习";
            info.RangeText = "这是一条测试提醒，用来检查弹窗、铃声和语音是否正常";
            info.NextText = BuildNextLine(now);
            info.PlanName = engine.ActivePlanName;

            audio.PlayRingtone(config.Settings.SoundFile, config.Settings.LoopSound, config.Settings.RingSeconds, mute);
            if (config.Settings.SpeakEnabled && !mute)
            {
                audio.Speak("这是一条测试语音，提醒功能正常", config.Settings.VoiceName, 100, 0);
            }

            ReminderForm form = new ReminderForm(info);
            form.DismissClicked += delegate
            {
                audio.StopAudio();
                audio.StopSpeech();
            };
            form.FormClosed += delegate
            {
                activeReminders.Remove(form);
                PositionActiveWindows();
                if (activeReminders.Count == 0)
                {
                    audio.StopAudio();
                    audio.StopSpeech();
                }
            };
            activeReminders.Add(form);
            PositionActiveWindows();
            try { form.Show(); form.Activate(); } catch (Exception ex) { Log.Error("显示测试提醒", ex); }
        }

        // ---------------- 菜单动作 ----------------

        private void PauseToggle()
        {
            DateTime now = DateTime.Now;
            if (engine.IsPaused(now))
            {
                engine.Resume();
                ShowBalloon("提醒已恢复", "继续按计划提醒。", ToolTipIcon.Info);
                Log.Write("恢复提醒");
            }
            else
            {
                DateTime until = now.AddMinutes(30);
                engine.PauseUntil(until);
                ShowBalloon("已暂停提醒", "暂停至 " + TimeUtil.FormatClock(until) + "，到时会自动恢复。", ToolTipIcon.Info);
                Log.Write("暂停提醒至 " + TimeUtil.FormatClock(until));
            }
            wasPaused = engine.IsPaused(DateTime.Now);
            RebuildMenu();
            UpdateTrayText(now);
            if (bar != null && !bar.IsDisposed && bar.Visible) bar.UpdateState(now, engine, engine.IsPaused(now));
        }

        private void SwitchTodayPlan(string planName)
        {
            PlanProfile p = config.GetPlan(planName);
            if (p == null) return;
            config.TodayPlan.Date = DateTime.Today.ToString("yyyy-MM-dd");
            config.TodayPlan.PlanName = p.Name;
            SaveConfigSafe();
            RefreshUi(DateTime.Now);
            Log.Write("切换今日方案：" + p.Name);
            ShowBalloon("今日方案已切换", p.Name + "（明天会自动回到" + AppConfig.DefaultPlanName + "）", ToolTipIcon.Info);
        }

        private void OpenMain()
        {
            try
            {
                if (mainForm == null || mainForm.IsDisposed)
                {
                    mainForm = new MainForm(config, engine);
                    try { mainForm.Icon = tray.Icon; } catch { }
                    mainForm.SettingsRequested += delegate { OpenSettings(); };
                    mainForm.TestRequested += delegate { ShowTestReminder(); };
                    mainForm.FormClosed += delegate { };
                }
                mainForm.Show();
                mainForm.WindowState = FormWindowState.Normal;
                mainForm.Activate();
                mainForm.BringToFront();
            }
            catch (Exception ex)
            {
                Log.Error("打开主窗口", ex);
                MessageBox.Show("打开今日计划失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSettings()
        {
            try
            {
                if (settingsForm != null && !settingsForm.IsDisposed)
                {
                    settingsForm.Show();
                    settingsForm.Activate();
                    return;
                }
                AppConfig clone = config.Clone();
                settingsForm = new SettingsForm(clone, engine.ActivePlanName, audio, OnSettingsSaved);
                try { settingsForm.Icon = tray.Icon; } catch { }
                settingsForm.FormClosed += delegate { settingsForm = null; };
                settingsForm.Show();
                settingsForm.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("打开设置窗口", ex);
                MessageBox.Show("打开设置失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSettingsSaved(AppConfig newConfig)
        {
            bool autoChanged = newConfig.Settings.AutoStart != config.Settings.AutoStart;
            bool autoValue = newConfig.Settings.AutoStart;
            if (autoChanged)
            {
                try
                {
                    AutostartManager.SetEnabled(autoValue);
                }
                catch (Exception ex)
                {
                    Log.Error("保存开机自启", ex);
                    newConfig.Settings.AutoStart = config.Settings.AutoStart;
                    MessageBox.Show("开机自启设置失败：" + ex.Message + "\n其余设置会正常保存。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            newConfig.Normalize();
            newConfig.EnsureTodayPlan(DateTime.Today);
            config = newConfig;
            engine.Config = config;
            SaveConfigSafe();
            RebuildMenu();
            UpdateTrayText(DateTime.Now);
            if (mainForm != null && !mainForm.IsDisposed) mainForm.ApplyConfig(config);
            if (config.Settings.ShowCountdownBar)
            {
                CreateBar();
                if (bar != null)
                {
                    bar.ApplyConfig(config);
                    bar.UpdateState(DateTime.Now, engine, engine.IsPaused(DateTime.Now));
                    bar.ShowBar();
                }
            }
            else if (bar != null)
            {
                bar.Hide();
            }
            ShowBalloon("设置已保存", "新的计划和提醒方式已生效。", ToolTipIcon.Info);
            Log.Write("设置已保存，方案：" + config.ActivePlanName + "，铃声：" + (string.IsNullOrEmpty(config.Settings.SoundFile) ? "系统默认" : config.Settings.SoundFile));
        }

        // ---------------- IPC ----------------

        private void StartListener()
        {
            Thread t = new Thread(delegate()
            {
                WaitHandle[] handles = new WaitHandle[] { evShowMain, evShowSettings, evShowTest };
                while (!exiting)
                {
                    int idx;
                    try { idx = WaitHandle.WaitAny(handles, 500); }
                    catch { break; }
                    if (idx == WaitHandle.WaitTimeout) continue;
                    if (exiting) break;
                    Action action;
                    if (idx == 0) action = OpenMain;
                    else if (idx == 1) action = OpenSettings;
                    else action = ShowTestReminder;
                    try { marshal.BeginInvoke(action); } catch { }
                }
            });
            t.IsBackground = true;
            t.Name = "KaoYanReminder-Listener";
            t.Start();
        }

        // ---------------- 消息/保存/退出 ----------------

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try { tray.ShowBalloonTip(4000, title, text, icon); } catch { }
        }

        private void SaveConfigSafe()
        {
            try
            {
                config.Save(AppPaths.ConfigPath);
            }
            catch (Exception ex)
            {
                Log.Error("保存配置", ex);
            }
        }

        private void ConfirmExit()
        {
            DialogResult r = MessageBox.Show("确定退出考研提醒吗？\n退出后到时间就不会再提醒了。", "退出确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r == DialogResult.Yes) ExitApp();
        }

        private void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            Log.Write("程序退出");
            try { tick.Stop(); tick.Dispose(); } catch { }
            try { SaveConfigSafe(); } catch { }
            try { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close(); } catch { }
            try { if (mainForm != null && !mainForm.IsDisposed) { mainForm.AllowClose = true; mainForm.Close(); } } catch { }
            try { if (bar != null && !bar.IsDisposed) bar.Close(); } catch { }
            foreach (ReminderForm f in new List<ReminderForm>(activeReminders)) { try { f.Close(); } catch { } }
            foreach (EndToastForm f in new List<EndToastForm>(activeToasts)) { try { f.Close(); } catch { } }
            try { audio.Dispose(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            try { marshal.Dispose(); } catch { }
            try { Application.ExitThread(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !exiting)
            {
                exiting = true;
                try { tick.Stop(); tick.Dispose(); } catch { }
                try { audio.Dispose(); } catch { }
                try { tray.Visible = false; tray.Dispose(); } catch { }
                try { marshal.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>开机自启（写 HKCU Run，不需要管理员）。</summary>
    public static class AutostartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KaoYanReminder";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key == null) return false;
                object v = key.GetValue(ValueName);
                return v != null && Convert.ToString(v).Length > 0;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("无法打开注册表启动项");
                if (enabled)
                {
                    string exe = Application.ExecutablePath;
                    key.SetValue(ValueName, "\"" + exe + "\" --silent", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }
}