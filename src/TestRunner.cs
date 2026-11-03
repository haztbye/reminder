using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace KaoYanReminder
{
    /// <summary>自检与界面截图（隐藏命令行参数，供开发/验证使用）。</summary>
    public static class TestRunner
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        private static StringBuilder report;
        private static int passCount;
        private static int failCount;
        private static int warnCount;

        public static void RunSelfTest()
        {
            report = new StringBuilder();
            report.AppendLine("考研提醒 · 自检报告");
            report.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("程序目录：" + AppPaths.BaseDir);
            report.AppendLine("系统：" + Environment.OSVersion.VersionString + " / .NET " + Environment.Version);
            report.AppendLine(new string('-', 64));

            try
            {
                TestTimeParse();
                TestDefaultConfig();
                TestConfigRoundTrip();
                TestPlanLookup();
                TestScheduleBasics();
                TestDueStart();
                TestDueEnd();
                TestPause();
                TestSnooze();
                TestSoundAvailability();
                TestSpeechAvailability();
            }
            catch (Exception ex)
            {
                failCount++;
                report.AppendLine("[FAIL] 自检异常中止：" + ex.GetType().Name + " " + ex.Message);
                Log.Error("自检", ex);
            }

            report.AppendLine(new string('-', 64));
            report.AppendLine("通过：" + passCount + "，失败：" + failCount + "，提示：" + warnCount);
            report.AppendLine(failCount == 0 ? "结论：核心功能自检通过。" : "结论：存在失败项，请查看日志。");
            try
            {
                File.WriteAllText(AppPaths.Combine("自检报告.txt"), report.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("写自检报告", ex);
            }
            Log.Write("自检完成：通过 " + passCount + "，失败 " + failCount + "，提示 " + warnCount);
            Environment.ExitCode = failCount == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok)
            {
                passCount++;
                report.AppendLine("[PASS] " + name);
            }
            else
            {
                failCount++;
                report.AppendLine("[FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
            }
        }

        private static void Warn(string name, string detail)
        {
            warnCount++;
            report.AppendLine("[WARN] " + name + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
        }

        private static AppConfig BuildConfig(params PlanItem[] items)
        {
            AppConfig c = AppConfig.CreateDefault();
            c.Plans.Clear();
            PlanProfile p = new PlanProfile();
            p.Name = AppConfig.DefaultPlanName;
            p.Items = new List<PlanItem>();
            foreach (PlanItem it in items) p.Items.Add(it);
            c.Plans.Add(p);
            c.TodayPlan.Date = DateTime.Today.ToString("yyyy-MM-dd");
            c.TodayPlan.PlanName = AppConfig.DefaultPlanName;
            c.Normalize();
            return c;
        }

        private static PlanItem Item(string start, string end, string title)
        {
            PlanItem it = new PlanItem();
            it.Enabled = true;
            it.Start = start;
            it.End = end;
            it.Title = title;
            return it;
        }

        private static void TestTimeParse()
        {
            TimeSpan t;
            Check("解析 6:30", TimeUtil.TryParse("6:30", out t) && t.Hours == 6 && t.Minutes == 30);
            Check("解析 06:30", TimeUtil.TryParse("06:30", out t) && TimeUtil.Format(t) == "06:30");
            Check("解析全角冒号 9：10", TimeUtil.TryParse("9：10", out t) && t.Hours == 9 && t.Minutes == 10);
            Check("拒绝 25:00", !TimeUtil.TryParse("25:00", out t));
            Check("拒绝 09:70", !TimeUtil.TryParse("09:70", out t));
            Check("拒绝空字符串", !TimeUtil.TryParse("", out t));
            Check("倒计时格式 01:23", TimeUtil.FormatCountdown(TimeSpan.FromSeconds(83)) == "01:23", TimeUtil.FormatCountdown(TimeSpan.FromSeconds(83)));
            Check("倒计时格式 1:02:03", TimeUtil.FormatCountdown(new TimeSpan(1, 2, 3)) == "1:02:03", TimeUtil.FormatCountdown(new TimeSpan(1, 2, 3)));
            Check("口语剩余 1 小时 5 分钟", TimeUtil.FormatApprox(TimeSpan.FromMinutes(65)) == "还有 1 小时 5 分钟", TimeUtil.FormatApprox(TimeSpan.FromMinutes(65)));
        }

        private static void TestDefaultConfig()
        {
            AppConfig c = AppConfig.CreateDefault();
            Check("默认方案数 = 2", c.Plans.Count == 2, "实际 " + c.Plans.Count);
            PlanProfile daily = c.GetPlan(AppConfig.DefaultPlanName);
            Check("默认方案存在", daily != null);
            Check("默认计划 20 项", daily != null && daily.Items.Count == 20, daily == null ? "" : "实际 " + daily.Items.Count);
            if (daily != null && daily.Items.Count >= 20)
            {
                Check("第一项 06:30 起床", daily.Items[0].Start == "06:30" && daily.Items[0].Title == "起床 + 早自习");
                Check("最后一项 23:00 上床睡觉", daily.Items[19].Start == "23:00" && daily.Items[19].Title == "上床睡觉");
            }
            AppConfig cloned = c.Clone();
            Check("配置克隆后计划一致", cloned.GetPlan(AppConfig.DefaultPlanName).Items.Count == 20);
            cloned.Normalize();
            Check("默认方案顺序正确", cloned.Plans[0].Name == AppConfig.DefaultPlanName);
        }

        private static void TestConfigRoundTrip()
        {
            AppConfig c = AppConfig.CreateDefault();
            c.Settings.SoundFile = "";
            c.Settings.SpeakEnabled = true;
            c.Settings.RemindAtEnd = true;
            string tmp = AppPaths.Combine("自检_config.xml");
            try
            {
                c.Save(tmp);
                Check("配置文件可写入程序目录", File.Exists(tmp));
                bool created;
                AppConfig loaded = AppConfig.Load(tmp, out created);
                Check("配置文件可读回", loaded != null && loaded.Plans.Count == 2);
                Check("中文内容无损", loaded.GetPlan(AppConfig.DefaultPlanName).Items[0].Title == "起床 + 早自习",
                    loaded.GetPlan(AppConfig.DefaultPlanName).Items[0].Title);
                Check("设置项读回", loaded.Settings.RingSeconds == c.Settings.RingSeconds);
                string xml = File.ReadAllText(tmp, Encoding.UTF8);
                Check("XML 为 UTF-8 中文", xml.Contains("起床 + 早自习"));
            }
            catch (Exception ex)
            {
                Check("配置读写测试", false, ex.Message);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static void TestPlanLookup()
        {
            AppConfig c = AppConfig.CreateDefault();
            c.TodayPlan.PlanName = AppConfig.RestPlanName;
            Check("切换休息日方案", c.ActivePlanName == AppConfig.RestPlanName, c.ActivePlanName);
            c.TodayPlan.PlanName = "不存在的方案";
            c.Normalize();
            Check("无效方案回退默认", c.ActivePlanName == AppConfig.DefaultPlanName, c.ActivePlanName);
            c.TodayPlan.Date = "2000-01-01";
            c.EnsureTodayPlan(DateTime.Today);
            Check("跨天恢复每日计划", c.TodayPlan.PlanName == AppConfig.DefaultPlanName);
        }

        private static void TestScheduleBasics()
        {
            AppConfig c = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"), Item("11:10", "12:00", "休息"));
            ScheduleEngine engine = new ScheduleEngine(c);
            DateTime day = DateTime.Today;

            PlanItem cur = engine.GetCurrent(day.AddHours(9).AddMinutes(30));
            Check("09:30 当前是学习一", cur != null && cur.Title == "学习一");

            DateTime when;
            PlanItem next = engine.GetNext(day.AddHours(10).AddMinutes(5), out when);
            Check("10:05 下一项是 10:10 学习二", next != null && next.Title == "学习二" && TimeUtil.FormatClock(when) == "10:10");

            cur = engine.GetCurrent(day.AddHours(10).AddMinutes(5));
            Check("10:05 没有当前任务", cur == null);

            next = engine.GetNext(day.AddHours(12).AddMinutes(30), out when);
            Check("12:30 下一项是明天 09:00", next != null && when.Date == day.AddDays(1) && TimeUtil.FormatClock(when) == "09:00");

            next = engine.GetNext(day.AddHours(8), out when);
            Check("08:00 下一项是今天 09:00", next != null && when.Date == day && TimeUtil.FormatClock(when) == "09:00");
        }

        private static void TestDueStart()
        {
            AppConfig c = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"));
            DateTime day = DateTime.Today;

            ScheduleEngine e1 = new ScheduleEngine(c);
            List<DueReminder> due = e1.GetDue(day.AddHours(9).AddSeconds(5));
            Check("09:00:05 触发开始提醒", due.Count == 1 && due[0].Kind == ReminderKind.Start, "数量 " + due.Count);
            due = e1.GetDue(day.AddHours(9).AddSeconds(6));
            Check("同一提醒不重复触发", due.Count == 0, "数量 " + due.Count);

            ScheduleEngine e2 = new ScheduleEngine(c);
            due = e2.GetDue(day.AddHours(9).AddSeconds(119));
            Check("09:01:59 仍在宽限内", due.Count == 1, "数量 " + due.Count);

            ScheduleEngine e3 = new ScheduleEngine(c);
            due = e3.GetDue(day.AddHours(9).AddSeconds(121));
            Check("09:02:01 超出宽限不补提醒", due.Count == 0, "数量 " + due.Count);

            ScheduleEngine e4 = new ScheduleEngine(c);
            due = e4.GetDue(day.AddHours(10).AddMinutes(9));
            Check("10:09 不提前触发 10:10", due.Count == 0, "数量 " + due.Count);
        }

        private static void TestDueEnd()
        {
            AppConfig c = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"));
            c.Settings.RemindAtEnd = true;
            DateTime day = DateTime.Today;
            ScheduleEngine e = new ScheduleEngine(c);
            List<DueReminder> due = e.GetDue(day.AddHours(10).AddSeconds(5));
            Check("10:00:05 触发结束提醒", due.Count == 1 && due[0].Kind == ReminderKind.End, "数量 " + due.Count);

            AppConfig c2 = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:00", "11:00", "学习二"));
            ScheduleEngine e2 = new ScheduleEngine(c2);
            due = e2.GetDue(day.AddHours(10).AddSeconds(5));
            Check("相邻任务隐藏多余结束提醒", due.Count == 1 && due[0].Kind == ReminderKind.Start, "数量 " + due.Count);

            AppConfig c3 = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"));
            c3.Settings.RemindAtEnd = false;
            ScheduleEngine e3 = new ScheduleEngine(c3);
            due = e3.GetDue(day.AddHours(10).AddSeconds(5));
            Check("关闭结束提醒后不触发", due.Count == 0, "数量 " + due.Count);
        }

        private static void TestPause()
        {
            AppConfig c = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"));
            DateTime day = DateTime.Today;
            ScheduleEngine e = new ScheduleEngine(c);
            e.PauseUntil(day.AddHours(9).AddSeconds(30));
            List<DueReminder> due = e.GetDue(day.AddHours(9).AddSeconds(10));
            Check("暂停期间不提醒", due.Count == 0, "数量 " + due.Count);
            due = e.GetDue(day.AddHours(9).AddSeconds(45));
            Check("暂停结束后补提醒", due.Count == 1 && due[0].Kind == ReminderKind.Start, "数量 " + due.Count);
            Check("暂停状态自动清除", !e.IsPaused(day.AddHours(9).AddSeconds(45)));
        }

        private static void TestSnooze()
        {
            AppConfig c = BuildConfig(Item("09:00", "10:00", "学习一"), Item("10:10", "11:00", "学习二"));
            DateTime day = DateTime.Today;
            ScheduleEngine e = new ScheduleEngine(c);
            e.AddSnooze(c.GetPlan(AppConfig.DefaultPlanName).Items[0], day.AddHours(9).AddMinutes(30), AppConfig.DefaultPlanName);
            List<DueReminder> due = e.GetDue(day.AddHours(9).AddMinutes(29));
            Check("稍后提醒未到点不触发", due.Count == 0, "数量 " + due.Count);
            due = e.GetDue(day.AddHours(9).AddMinutes(31));
            Check("稍后提醒到点触发", due.Count == 1 && due[0].Kind == ReminderKind.Snooze, "数量 " + due.Count);
        }

        private static void TestSoundAvailability()
        {
            string def = AudioService.DefaultAlarmPath;
            Check("系统默认铃声存在", def != null && File.Exists(def), def);
            Check("wav/mp3 扩展名识别", AudioService.IsSupportedExtension("a.mp3") && AudioService.IsSupportedExtension("a.WAV") && !AudioService.IsSupportedExtension("a.txt"));
        }

        private static void TestSpeechAvailability()
        {
            AudioService audio = null;
            try
            {
                audio = new AudioService(null);
                List<VoiceOption> voices = audio.GetVoices();
                if (voices.Count == 0)
                {
                    Warn("系统语音检测", "未检测到语音组件（在受限测试环境中可能出现，正式运行时应可用）");
                    return;
                }
                bool chinese = false;
                foreach (VoiceOption v in voices) if (v.IsChinese) chinese = true;
                report.AppendLine("[INFO] 检测到语音：" + string.Join("；", voices.ConvertAll(delegate(VoiceOption v) { return v.Description; }).ToArray()));
                Check("系统语音可用", true);
                Check("检测到中文语音", chinese, "可选英文语音");
            }
            catch (Exception ex)
            {
                Warn("系统语音检测", ex.Message);
            }
            finally
            {
                if (audio != null) audio.Dispose();
            }
        }

        // ---------------- 截图 ----------------

        public static void RunScreenshots(string dir)
        {
            try
            {
                if (!Path.IsPathRooted(dir)) dir = Path.Combine(AppPaths.BaseDir, dir);
                Directory.CreateDirectory(dir);
                AppConfig config = AppConfig.CreateDefault();
                config.Normalize();
                config.Settings.ShowCountdownBar = true;
                ScheduleEngine engine = new ScheduleEngine(config);
                AudioService audio = new AudioService(null);
                DateTime now = DateTime.Now;
                List<string> saved = new List<string>();

                MainForm main = new MainForm(config, engine);
                DumpLayout(main, Path.Combine(dir, "布局报告_今日计划.txt"));
                saved.Add(Render(main, Path.Combine(dir, "1_今日计划.png")));

                CountdownBar bar = new CountdownBar(null, null, null, null, null);
                bar.ApplyConfig(config);
                bar.Location = new Point(-32000, -32000);
                bar.ShowBar();
                bar.UpdateState(now, engine, false);
                saved.Add(Render(bar, Path.Combine(dir, "2_桌面倒计时条.png")));

                ReminderInfo ri = new ReminderInfo();
                ri.HeaderText = "开始提醒";
                ri.TimeText = "09:10";
                ri.Title = "学习";
                ri.RangeText = "09:10 – 10:10";
                ri.NextText = "下一项：10:30 学习 · 还有 1 小时 20 分钟";
                ri.PlanName = AppConfig.DefaultPlanName;
                ReminderForm rf = new ReminderForm(ri);
                saved.Add(Render(rf, Path.Combine(dir, "3_开始提醒.png")));

                ReminderInfo ri2 = new ReminderInfo();
                ri2.IsSnooze = true;
                ri2.HeaderText = "稍后提醒";
                ri2.TimeText = "09:15";
                ri2.Title = "学习";
                ri2.RangeText = "09:10 – 10:10";
                ri2.NextText = "下一项：10:30 学习 · 还有 1 小时 15 分钟";
                ri2.PlanName = AppConfig.RestPlanName;
                ReminderForm rf2 = new ReminderForm(ri2);
                saved.Add(Render(rf2, Path.Combine(dir, "4_稍后提醒.png")));

                ReminderInfo ei = new ReminderInfo();
                ei.IsEnd = true;
                ei.HeaderText = "结束提醒";
                ei.Title = "学习 结束";
                ei.NextText = "下一项：10:30 学习 · 还有 30 分钟";
                EndToastForm et = new EndToastForm(ei);
                saved.Add(Render(et, Path.Combine(dir, "5_结束轻提醒.png")));

                SettingsForm sf = new SettingsForm(config.Clone(), engine.ActivePlanName, audio, delegate { });
                sf.SelectTab(0);
                DumpLayout(sf, Path.Combine(dir, "布局报告_设置.txt"));
                saved.Add(Render(sf, Path.Combine(dir, "6_设置_计划.png")));
                SettingsForm sf2 = new SettingsForm(config.Clone(), engine.ActivePlanName, audio, delegate { });
                sf2.SelectTab(1);
                saved.Add(Render(sf2, Path.Combine(dir, "7_设置_提醒.png")));

                audio.Dispose();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("截图生成完成：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (string s in saved) sb.AppendLine(s);
                File.WriteAllText(Path.Combine(dir, "截图说明.txt"), sb.ToString(), new UTF8Encoding(false));
                Log.Write("界面截图已生成：" + dir);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Log.Error("生成截图", ex);
                try
                {
                    string dir2 = Path.IsPathRooted(dir) ? dir : Path.Combine(AppPaths.BaseDir, dir);
                    Directory.CreateDirectory(dir2);
                    File.WriteAllText(Path.Combine(dir2, "截图失败.txt"), ex.ToString(), new UTF8Encoding(false));
                }
                catch { }
                Environment.ExitCode = 1;
            }
        }

        private static void DumpLayout(Form form, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Form=" + form.GetType().Name + " ClientSize=" + form.ClientSize + " Size=" + form.Size
                + " AutoScaleMode=" + form.AutoScaleMode + " AutoScaleDimensions=" + form.AutoScaleDimensions
                + " CurrentAutoScale=" + form.CurrentAutoScaleDimensions + " DeviceDpi=" + form.DeviceDpi
                + " Font=" + form.Font.Name + "/" + form.Font.Size + "pt");
            DumpControls(form, sb, 0);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void DumpControls(Control parent, StringBuilder sb, int depth)
        {
            foreach (Control c in parent.Controls)
            {
                string info = "";
                if (!string.IsNullOrEmpty(c.Text))
                {
                    Size measured = TextRenderer.MeasureText(c.Text, c.Font);
                    info = " text=[" + c.Text + "] measured=" + measured.Width + "x" + measured.Height
                        + " fitsWidth=" + (measured.Width <= c.Width) + " fitsHeight=" + (measured.Height <= c.Height);
                }
                sb.AppendLine(new string(' ', depth * 2) + c.GetType().Name + " bounds=" + c.Bounds
                    + " font=" + c.Font.Name + "/" + c.Font.Size + "pt" + info);
                DumpControls(c, sb, depth + 1);
            }
        }

                public static void RunLiveCapture(string dir)
        {
            try
            {
                if (!Path.IsPathRooted(dir)) dir = Path.Combine(AppPaths.BaseDir, dir);
                Directory.CreateDirectory(dir);
                AppConfig config = AppConfig.CreateDefault();
                config.Normalize();
                ScheduleEngine engine = new ScheduleEngine(config);
                AudioService audio = new AudioService(null);
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                StringBuilder report = new StringBuilder();
                report.AppendLine("实时窗口截图（PrintWindow）：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("WorkingArea=" + wa);

                MainForm main = new MainForm(config, engine);
                main.StartPosition = FormStartPosition.Manual;
                main.Location = new Point(wa.Left + 40, wa.Top + 40);
                Capture(main, Path.Combine(dir, "live_main.png"), report);
                DumpLayout(main, Path.Combine(dir, "live_main_layout.txt"));
                main.Close();

                ReminderInfo ri = new ReminderInfo();
                ri.HeaderText = "开始提醒";
                ri.TimeText = "09:10";
                ri.Title = "学习";
                ri.RangeText = "09:10 – 10:10";
                ri.NextText = "下一项：10:30 学习 · 还有 1 小时 20 分钟";
                ri.PlanName = AppConfig.DefaultPlanName;
                ReminderForm rf = new ReminderForm(ri);
                rf.StartPosition = FormStartPosition.Manual;
                rf.Location = new Point(wa.Left + 100, wa.Top + 100);
                Capture(rf, Path.Combine(dir, "live_reminder.png"), report);
                DumpLayout(rf, Path.Combine(dir, "live_reminder_layout.txt"));
                rf.Close();

                CountdownBar bar = new CountdownBar(null, null, null, null, null);
                bar.ApplyConfig(config);
                bar.Location = new Point(wa.Right - bar.Width - 30, wa.Top + 30);
                bar.ShowBar();
                bar.UpdateState(DateTime.Now, engine, false);
                Capture(bar, Path.Combine(dir, "live_bar.png"), report);
                DumpLayout(bar, Path.Combine(dir, "live_bar_layout.txt"));
                bar.Close();

                SettingsForm sf = new SettingsForm(config.Clone(), engine.ActivePlanName, audio, delegate { });
                sf.StartPosition = FormStartPosition.Manual;
                sf.Location = new Point(wa.Left + 30, wa.Top + 20);
                sf.SelectTab(0);
                Capture(sf, Path.Combine(dir, "live_settings.png"), report);
                sf.Close();

                audio.Dispose();
                File.WriteAllText(Path.Combine(dir, "实时截图说明.txt"), report.ToString(), new UTF8Encoding(false));
                Log.Write("实时窗口截图完成：" + dir);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Log.Error("实时窗口截图", ex);
                Environment.ExitCode = 1;
            }
        }

        private static void Capture(Form form, string path, StringBuilder report)
        {
            form.ShowInTaskbar = false;
            if (!form.Visible) form.Show();
            form.TopMost = true;
            Application.DoEvents();
            for (int i = 0; i < 8; i++)
            {
                Thread.Sleep(60);
                Application.DoEvents();
            }
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                bool screenOk = false;
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(form.Left, form.Top, 0, 0, form.Size);
                    }
                    screenOk = true;
                    report.AppendLine(form.GetType().Name + " screenCopy=True Size=" + form.Size + " Location=" + form.Location);
                }
                catch (Exception ex)
                {
                    report.AppendLine(form.GetType().Name + " screenCopy=False " + ex.Message);
                }
                if (!screenOk)
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        bool ok = PrintWindow(form.Handle, hdc, 2);
                        if (!ok) ok = PrintWindow(form.Handle, hdc, 0);
                        g.ReleaseHdc(hdc);
                        report.AppendLine(form.GetType().Name + " PrintWindow=" + ok);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private static string Render(Form form, string path)
        {
            if (!form.Visible)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.ShowInTaskbar = false;
                form.Show();
            }
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            Application.DoEvents();
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(80);
                Application.DoEvents();
            }
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            form.Close();
            form.Dispose();
            return path;
        }
    }
}