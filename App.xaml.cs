using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace VirtualKeyboard
{
    public partial class App : Application
    {
        // Windows API 用于查找窗口
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 检查是否已有实例运行
            string windowTitle = "虚拟键盘";
            IntPtr existingWindow = FindWindow(null, windowTitle);

            if (existingWindow != IntPtr.Zero)
            {
                // 已有实例运行，激活它并退出
                ShowWindow(existingWindow, SW_RESTORE);
                SetForegroundWindow(existingWindow);
                this.Shutdown();
                return;
            }

            base.OnStartup(e);
        }
    }
}
