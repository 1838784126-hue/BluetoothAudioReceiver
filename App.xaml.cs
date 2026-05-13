using System;
using System.Windows;

namespace BluetoothAudioReceiver
{
    public partial class App : System.Windows.Application
    {
        // ═══════════════════════════════════════════════════════════════
        // 全局标志：是否以后台模式启动
        // ═══════════════════════════════════════════════════════════════
        public static bool StartHidden { get; private set; } = false;

        // ═══════════════════════════════════════════════════════════════
        // 应用启动事件处理器
        // ═══════════════════════════════════════════════════════════════
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ═══════════════════════════════════════════════════════════
            // 步骤 1：拦截启动参数
            // ═══════════════════════════════════════════════════════════
            // 检查命令行参数是否包含后台启动标记
            if (e.Args != null && e.Args.Length > 0)
            {
                foreach (var arg in e.Args)
                {
                    if (arg.Equals("-background", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--hidden", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        StartHidden = true;
                        break;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════
            // 步骤 2：手动创建主窗口
            // ═══════════════════════════════════════════════════════════
            MainWindow mainWindow = new MainWindow();

            if (StartHidden)
            {
                // 后台启动：不显示主窗口，只显示托盘图标
                // MainWindow 会在 Loaded 事件中初始化托盘图标
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.ShowInTaskbar = false;
                mainWindow.Show();  // 必须调用 Show() 以触发 Loaded 事件
                mainWindow.Hide();  // 然后立即隐藏
            }
            else
            {
                // 正常启动：显示主窗口
                mainWindow.Show();
            }

            this.MainWindow = mainWindow;
        }
    }
}
