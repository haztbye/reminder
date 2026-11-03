using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>程序所在目录相关路径。所有数据都写在 exe 旁边。</summary>
    public static class AppPaths
    {
        public static string BaseDir
        {
            get
            {
                try { return Path.GetDirectoryName(Application.ExecutablePath); }
                catch { return Environment.CurrentDirectory; }
            }
        }

        public static string ConfigPath { get { return Path.Combine(BaseDir, "config.xml"); } }
        public static string LogPath { get { return Path.Combine(BaseDir, "提醒日志.txt"); } }
        public static string ScreenshotDir { get { return Path.Combine(BaseDir, "截图"); } }

        public static string Combine(string name) { return Path.Combine(BaseDir, name); }
    }

    public static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxSize = 1024 * 1024;

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string path = AppPaths.LogPath;
                    if (File.Exists(path))
                    {
                        FileInfo fi = new FileInfo(path);
                        if (fi.Length > MaxSize)
                        {
                            string old = Path.Combine(AppPaths.BaseDir, "提醒日志.old.txt");
                            try { if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                            catch { }
                        }
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        public static void Error(string where, Exception ex)
        {
            Write("[错误] " + where + ": " + (ex == null ? "(null)" : ex.GetType().Name + " " + ex.Message));
        }
    }
}