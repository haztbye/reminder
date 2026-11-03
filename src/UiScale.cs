using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KaoYanReminder
{
    /// <summary>
    /// 手动 DPI 缩放：本机系统 DPI 为 144 时字体已经是 150%，
    /// 但 .NET Framework 的 DeviceDpi 在此系统上返回 96，自动缩放失效，
    /// 所以用系统真实 DPI 手动缩放所有控件的尺寸。
    /// </summary>
    public static class UiScale
    {
        [DllImport("user32.dll")]
        private static extern int GetDpiForSystem();

        public static readonly float Factor = Detect();

        private static float Detect()
        {
            try
            {
                int dpi = GetDpiForSystem();
                if (dpi <= 0) dpi = 96;
                float f = dpi / 96f;
                if (f < 1f) f = 1f;
                if (f > 3f) f = 3f;
                return f;
            }
            catch
            {
                return 1f;
            }
        }

        public static float Value
        {
            get { return Factor; }
        }

        public static int Px(int designPixels)
        {
            return (int)Math.Round(designPixels * Factor);
        }

        public static void ScaleForm(Form form)
        {
            if (form == null) return;
            if (Math.Abs(Factor - 1f) < 0.01f) return;
            form.Scale(new SizeF(Factor, Factor));
        }

        public static void UnscaleForm(Form form)
        {
            if (form == null) return;
            if (Math.Abs(Factor - 1f) < 0.01f) return;
            form.Scale(new SizeF(1f / Factor, 1f / Factor));
        }
    }
}