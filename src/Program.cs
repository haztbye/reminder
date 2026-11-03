using System;
using System.Threading;
using System.Windows.Forms;

namespace KaoYanReminder
{
    internal static class Program
    {
        private const string MutexName = "Local\\KaoYanReminder.SingleInstance";
        private const string EventShowMainName = "Local\\KaoYanReminder.ShowMain";
        private const string EventShowSettingsName = "Local\\KaoYanReminder.ShowSettings";
        private const string EventShowTestName = "Local\\KaoYanReminder.ShowTest";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = HasArg(args, "--silent");
            bool openSettings = HasArg(args, "--open-settings") || HasArg(args, "--settings");
            bool testReminder = HasArg(args, "--test-reminder");
            bool mute = HasArg(args, "--mute");
            int smoke = GetIntArg(args, "--smoke-seconds", 0);
            string screenshotDir = GetStringArg(args, "--screenshot");
            string captureDir = GetStringArg(args, "--capture");

            if (HasArg(args, "--selftest"))
            {
                TestRunner.RunSelfTest();
                return;
            }
            if (!string.IsNullOrEmpty(screenshotDir))
            {
                TestRunner.RunScreenshots(screenshotDir);
                return;
            }
            if (!string.IsNullOrEmpty(captureDir))
            {
                TestRunner.RunLiveCapture(captureDir);
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    SignalExisting(openSettings, testReminder);
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Error("界面线程异常", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Error("未捕获异常", e.ExceptionObject as Exception);
                };

                EventWaitHandle evShowMain = new EventWaitHandle(false, EventResetMode.AutoReset, EventShowMainName);
                EventWaitHandle evShowSettings = new EventWaitHandle(false, EventResetMode.AutoReset, EventShowSettingsName);
                EventWaitHandle evShowTest = new EventWaitHandle(false, EventResetMode.AutoReset, EventShowTestName);

                try
                {
                    using (TrayApp app = new TrayApp(evShowMain, evShowSettings, evShowTest, silent, openSettings, testReminder, mute, smoke))
                    {
                        Application.Run(app);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("程序启动", ex);
                    MessageBox.Show("考研提醒启动失败：\n" + ex.Message + "\n\n详细信息已写入提醒日志.txt。",
                        "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    evShowMain.Dispose();
                    evShowSettings.Dispose();
                    evShowTest.Dispose();
                    GC.KeepAlive(mutex);
                }
            }
        }

        private static void SignalExisting(bool openSettings, bool testReminder)
        {
            bool signaled = false;
            for (int i = 0; i < 24 && !signaled; i++)
            {
                try
                {
                    if (testReminder)
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(EventShowTestName)) ev.Set();
                        signaled = true;
                    }
                    else if (openSettings)
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(EventShowSettingsName)) ev.Set();
                        signaled = true;
                    }
                    else
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(EventShowMainName)) ev.Set();
                        signaled = true;
                    }
                }
                catch
                {
                    Thread.Sleep(120);
                }
            }
            if (!signaled)
            {
                MessageBox.Show("考研提醒已经在运行了，请在屏幕右下角的托盘图标处查看。", "考研提醒",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetStringArg(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        private static int GetIntArg(string[] args, string name, int fallback)
        {
            string s = GetStringArg(args, name);
            int v;
            if (s != null && int.TryParse(s, out v) && v > 0) return v;
            return fallback;
        }
    }
}