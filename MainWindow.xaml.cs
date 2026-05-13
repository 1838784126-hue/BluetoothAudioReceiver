using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BluetoothAudioReceiver.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Windows.Media.Devices;

namespace BluetoothAudioReceiver
{
    public partial class MainWindow : Window
    {
        private readonly BluetoothService _bt;
        private readonly ObservableCollection<BluetoothDeviceInfo> _devices = new();
        private bool _minimizeToTray = true;
        private bool _isExiting = false;
        private TaskbarIcon TrayIcon;

        public MainWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = _devices;

            // 从 Resources 中获取 TaskbarIcon
            TrayIcon = (Hardcodet.Wpf.TaskbarNotification.TaskbarIcon)this.Resources["TrayIcon"];
            TrayIcon.DataContext = this;
            
            // ═══════════════════════════════════════════════════════════════
            // 安全加载自定义图标（通过 IconLoader）
            // ═══════════════════════════════════════════════════════════════
            try
            {
                // 使用 IconLoader 安全加载图标（带缓存和回退）
                TrayIcon.Icon = IconLoader.GetTrayIcon();
            }
            catch
            {
                // 安全回退：防止任何图标加载失败导致崩溃
                TrayIcon.Icon = SystemIcons.Application;
            }

            _bt = new BluetoothService();
            _bt.StatusChanged      += (_, msg)  => Dispatch(() => StatusText.Text = msg);
            _bt.DeviceConnected    += (_, dev)  => Dispatch(() => OnConnected(dev));
            _bt.DeviceDisconnected += (_, _)    => Dispatch(() => OnDisconnected());
            _bt.ErrorOccurred      += (_, ex)   => Dispatch(() =>
                System.Windows.MessageBox.Show(ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning));

            // ═══════════════════════════════════════════════════════════════
            // 环境感知：订阅 Windows 默认音频设备变化事件
            // 当 PC 连接新的蓝牙耳机时，Windows 会改变默认音频渲染设备，
            // 此时需要自动重建手机连接以路由到新的蓝牙耳机
            // ═══════════════════════════════════════════════════════════════
            MediaDevice.DefaultAudioRenderDeviceChanged += MediaDevice_DefaultAudioRenderDeviceChanged;

            LoadSettings();
            Loaded += async (_, _) =>
            {
                await AutoScanOnStartAsync();
            };
        }

        // ── 启动自动扫描 + 自动连接 ──────────────────────────
        private async Task AutoScanOnStartAsync()
        {
            ScanBtn.IsEnabled = false;
            ScanBtn.Content   = "🔄  扫描中...";
            StatusText.Text   = "正在扫描设备...";
            EmptyHint.Visibility = Visibility.Collapsed;

            var list = await _bt.GetPairedDevicesAsync();
            foreach (var d in list) _devices.Add(d);

            if (_devices.Count == 0)
            {
                EmptyHint.Text       = "未找到已配对设备，请先在系统蓝牙设置中配对手机";
                EmptyHint.Visibility = Visibility.Visible;
                StatusText.Text      = "未找到设备";
            }
            else
            {
                EmptyHint.Visibility = Visibility.Collapsed;
                StatusText.Text      = $"找到 {_devices.Count} 个设备";

                // 如果有上次连接的设备，自动高亮并连接
                if (AutoStartCB.IsChecked == true && !string.IsNullOrEmpty(_bt.LastDeviceId))
                {
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        if (_devices[i].Id == _bt.LastDeviceId)
                        {
                            DeviceList.SelectedIndex = i;
                            break;
                        }
                    }

                    // ═══════════════════════════════════════════════════════════
                    // AutoConnectLastDeviceAsync uses AutoStartup mode internally:
                    //   • Deep Reset (GC.Collect + 2 s cool-down)
                    //   • 3 s + 2 s breathing room before OpenAsync
                    //   • One-shot 5 s auto-fix scheduled after success
                    // Do NOT call ResetConnectionAsync() here — that would fire a
                    // second full ForceConnect cycle on top of the first one and
                    // race against the already-scheduled auto-fix.
                    // ═══════════════════════════════════════════════════════════
                    await _bt.AutoConnectLastDeviceAsync();
                }
            }

            ScanBtn.IsEnabled = true;
            ScanBtn.Content   = "🔍  扫描设备";
        }

        // ── 扫描 ──────────────────────────────────────────────
        private async void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            ScanBtn.IsEnabled = false;
            ScanBtn.Content   = "🔄  扫描中...";
            _devices.Clear();
            EmptyHint.Visibility = Visibility.Collapsed;

            var list = await _bt.GetPairedDevicesAsync();
            foreach (var d in list) _devices.Add(d);

            if (_devices.Count == 0)
            {
                EmptyHint.Text       = "未找到已配对设备，请先在系统蓝牙设置中配对";
                EmptyHint.Visibility = Visibility.Visible;
            }

            ScanBtn.IsEnabled = true;
            ScanBtn.Content   = "🔍  扫描设备";
            UpdateActionButton();
        }

        // ── 合并的连接/断开/切换按钮 ──────────────────────────
        // 关键：手动点击时不需要额外延迟（用户已经等待）
        // 但设备切换需要等待 OS 处理硬件变化
        private async void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceList.SelectedItem is not BluetoothDeviceInfo selectedDev) return;
            
            ActionBtn.IsEnabled = false;
            
            try
            {
                if (_bt.IsConnected && selectedDev.Id == _bt.CurrentDevice?.Id)
                {
                    // 已连接且选中的是当前设备 → 断开
                    ActionBtn.Content = "🔄  断开中...";
                    await _bt.DisconnectAsync();
                }
                else if (_bt.IsConnected && selectedDev.Id != _bt.CurrentDevice?.Id)
                {
                    // 已连接但选中的是不同设备 → 使用"撕裂和重建"切换
                    ActionBtn.Content = "🔄  切换中...";
                    StatusText.Text = "🔄 正在切换设备并重新路由音频...";
                    
                    // ═══════════════════════════════════════════════════════════
                    // 关键：设备切换前等待 OS 处理硬件变化
                    // ═══════════════════════════════════════════════════════════
                    StatusText.Text = "⏳ 等待 Windows Audio 子系统处理硬件变化（3秒）...";
                    await Task.Delay(3000);  // 给 OS 3 秒时间处理硬件变化
                    
                    // 使用专门的切换方法（包含完整的撕裂和重建流程）
                    bool success = await _bt.SwitchToDeviceAsync(selectedDev.Id);
                    
                    if (success)
                    {
                        StatusText.Text = "✅ 设备切换成功！";
                    }
                    else
                    {
                        StatusText.Text = "❌ 设备切换失败，请重试";
                    }
                }
                else
                {
                    // 未连接 → 连接
                    ActionBtn.Content = "🔄  连接中...";
                    StatusText.Text = "正在连接设备...";
                    
                    bool success = await _bt.ConnectToDeviceAsync(selectedDev.Id);
                    
                    if (success)
                    {
                        StatusText.Text = "✅ 连接成功！";
                    }
                    else
                    {
                        StatusText.Text = "❌ 连接失败，请重试";
                    }
                }
            }
            finally
            {
                UpdateActionButton();
            }
        }

        // ── 修复声音（强制重置连接） ──────────────────────
        // 关键：手动修复不需要额外延迟，直接调用 ForceConnectAudioAsync
        private async void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetBtn.IsEnabled = false;
            ResetBtn.Content = "🔄  重置中...";
            StatusText.Text = "🔧 正在修复音频...";
            
            bool success = await _bt.ResetConnectionAsync();
            
            ResetBtn.Content = success ? "✅  修复成功" : "❌  修复失败";
            StatusText.Text = success ? "✅ 音频修复成功！" : "❌ 音频修复失败，请重试";
            ResetBtn.IsEnabled = _bt.IsConnected;
            
            // 2秒后恢复按钮文本
            await Task.Delay(2000);
            ResetBtn.Content = "🔧  修复声音";
        }

        // ── 列表选中 ──────────────────────────────────────────
        private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButton();
        }

        // ── 更新操作按钮文本 ─────────────────────────────────
        private void UpdateActionButton()
        {
            if (DeviceList.SelectedItem is not BluetoothDeviceInfo selectedDev)
            {
                // 没有选中任何设备
                ActionBtn.Content = "🔗  连接";
                ActionBtn.IsEnabled = false;
                return;
            }
            
            if (_bt.IsConnected)
            {
                // 有设备连接
                if (selectedDev.Id == _bt.CurrentDevice?.Id)
                {
                    // 选中的就是当前连接的设备 → 显示"断开"
                    ActionBtn.Content = "✖  断开";
                }
                else
                {
                    // 选中的不是当前连接的设备 → 显示"切换"
                    ActionBtn.Content = "🔄  切换";
                }
                ActionBtn.IsEnabled = true;
            }
            else
            {
                // 未连接但选中了设备 → 显示"连接"
                ActionBtn.Content = "🔗  连接";
                ActionBtn.IsEnabled = true;
            }
        }

        // ── 选项变更 ──────────────────────────────────────────
        private void Options_Changed(object sender, RoutedEventArgs e)
        {
            if (_bt == null) return;
            _bt.IsAutoReconnectEnabled = AutoReconnectCB.IsChecked == true;
            _minimizeToTray            = MinimizeToCB.IsChecked == true;
            AutoReconnectBadge.Text    = _bt.IsAutoReconnectEnabled ? "自动重连 ON" : "自动重连 OFF";
            AutoReconnectBadge.Foreground = _bt.IsAutoReconnectEnabled
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
            
            // 保存开机自启设置
            SaveAutoLaunch(AutoLaunchCB.IsChecked == true);
        }

        // ── 连接成功回调 ──────────────────────────────────────
        private void OnConnected(BluetoothDeviceInfo dev)
        {
            StatusDot.Fill      = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            StatusText.Text     = "✅  已连接";
            DeviceNameText.Text = dev.Name;
            ResetBtn.IsEnabled      = true;
            DeviceList.Items.Refresh();
            TrayIcon.ToolTipText = $"蓝牙音频接收器 · {dev.Name}";
            UpdateActionButton();
        }

        // ── 断开回调 ──────────────────────────────────────────
        private void OnDisconnected()
        {
            StatusDot.Fill      = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            StatusText.Text     = "⚠️  已断开";
            DeviceNameText.Text = _bt.IsAutoReconnectEnabled ? "正在尝试重连..." : "点击连接";
            ResetBtn.IsEnabled      = false;
            TrayIcon.ToolTipText    = "蓝牙音频接收器 · 未连接";
            UpdateActionButton();
        }

        // ── 窗口最小化 ────────────────────────────────────────
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _minimizeToTray)
            {
                Hide();
                TrayIcon.ShowBalloonTip("蓝牙音频接收器", "已最小化到系统托盘，双击图标可恢复",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }

        // ── 关闭窗口 ──────────────────────────────────────────
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExiting && _minimizeToTray)
            {
                e.Cancel = true;
                Hide();
                TrayIcon.ShowBalloonTip("蓝牙音频接收器", "程序仍在后台运行，右键托盘图标可退出",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
            else
            {
                SaveSettings();
                _bt.Dispose();
                TrayIcon.Dispose();
            }
        }

        // ── 托盘图标左键点击 ─────────────────────────────────
        private void TrayIcon_LeftClick(object sender, RoutedEventArgs e)
        {
            if (Visibility == Visibility.Visible && WindowState == WindowState.Normal)
            {
                Hide();
                WindowState = WindowState.Minimized;
            }
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        // ── 托盘菜单 ──────────────────────────────────────────
        private void TrayShow_Click(object sender, RoutedEventArgs e)
        {
            Show(); WindowState = WindowState.Normal; Activate();
        }

        private async void TrayReconnect_Click(object sender, RoutedEventArgs e)
        {
            // AutoConnectLastDeviceAsync uses AutoStartup mode:
            // Deep Reset + proper warm-up + one-shot auto-fix.
            // Do NOT chain ResetConnectionAsync() after this.
            await _bt.AutoConnectLastDeviceAsync();
        }

        private async void TrayRepairAudio_Click(object sender, RoutedEventArgs e)
        {
            if (!_bt.IsConnected)
            {
                TrayIcon.ShowBalloonTip("修复声音", "当前没有已连接的设备，请先连接设备",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                return;
            }

            TrayIcon.ShowBalloonTip("修复声音", "正在修复音频，请稍候...",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);

            bool success = await _bt.ResetConnectionAsync();

            TrayIcon.ShowBalloonTip("修复声音",
                success ? "✅ 音频修复成功！" : "❌ 音频修复失败，请打开主窗口重试",
                success ? Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info
                        : Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Error);
        }

        // ═══════════════════════════════════════════════════════════════
        // 环境感知：Windows 默认音频设备变化事件处理器
        //
        // 注意：已禁用激进自动修复。让 Windows 原生处理音频图路由。
        // 仅记录设备变化，不自动触发 ForceConnectAudioAsync。
        // 用户可以通过手动点击「修复声音」按钮处理严重的路由锁定。
        // ═══════════════════════════════════════════════════════════════
        private async void MediaDevice_DefaultAudioRenderDeviceChanged(object sender, object args)
        {
            // 事件在非 UI 线程触发，必须使用 Dispatcher 切换到 UI 线程
            await Dispatcher.InvokeAsync(() =>
            {
                // 记录设备变化，但不自动修复
                StatusText.Text = "🔔 [环境感知] 检测到系统音频设备变化";
                TrayIcon.ShowBalloonTip("音频设备变化",
                    "系统默认音频设备已变更。如果手机音频中断，请手动点击「修复声音」",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);

                // 不再自动触发 ForceConnectAudioAsync
                // 交给用户手动处理或等待 Windows 原生恢复
            });
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true; Close();
        }

        // ── 设置持久化 ────────────────────────────────────────
        private static string SettingsPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothAudioReceiver", "settings.ini");

        private static string AutoLaunchKey => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private static string AppName => "BluetoothAudioReceiver";

        private void LoadSettings()
        {
            try
            {
                if (System.IO.File.Exists(SettingsPath))
                {
                    foreach (var line in System.IO.File.ReadAllLines(SettingsPath))
                    {
                        var p = line.Split('=');
                        if (p.Length != 2) continue;
                        switch (p[0])
                        {
                            case "AutoReconnect": AutoReconnectCB.IsChecked = p[1] == "1"; break;
                            case "AutoStart":     AutoStartCB.IsChecked     = p[1] == "1"; break;
                            case "AutoLaunch":    AutoLaunchCB.IsChecked     = p[1] == "1"; break;
                            case "MinimizeToTray":MinimizeToCB.IsChecked    = p[1] == "1"; break;
                        }
                    }
                }
                
                // 检查注册表中的开机自启状态
                using var key = Registry.CurrentUser.OpenSubKey(AutoLaunchKey, false);
                AutoLaunchCB.IsChecked = key?.GetValue(AppName) != null;
                
                _bt.IsAutoReconnectEnabled = AutoReconnectCB.IsChecked == true;
                _minimizeToTray            = MinimizeToCB.IsChecked == true;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(SettingsPath)!;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllLines(SettingsPath, new[]
                {
                    $"AutoReconnect={(AutoReconnectCB.IsChecked == true ? "1" : "0")}",
                    $"AutoStart={(AutoStartCB.IsChecked == true ? "1" : "0")}",
                    $"AutoLaunch={(AutoLaunchCB.IsChecked == true ? "1" : "0")}",
                    $"MinimizeToTray={(MinimizeToCB.IsChecked == true ? "1" : "0")}"
                });
            }
            catch { }
        }

        private void SaveAutoLaunch(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutoLaunchKey, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        // 关键：添加 -background 参数，后台启动不显示窗口
                        key.SetValue(AppName, $"\"{exePath}\" -background");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        // ── 工具方法 ──────────────────────────────────────────
        private void Dispatch(Action a) => Dispatcher.Invoke(a);
    }
}
