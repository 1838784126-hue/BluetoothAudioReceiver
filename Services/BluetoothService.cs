using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BluetoothAudioReceiver.Services
{
    public class BluetoothDeviceInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Status { get; set; }
        public bool IsPaired { get; set; }
        public bool IsCurrentlyConnected { get; set; }
        public string StatusLabel => Status ? "✅ 已连接" : "○ 未连接";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ConnectionMode — controls which timing/reset path ForceConnectAudioAsync
    // takes.  Each mode maps to a different hardware-level strategy:
    //
    //   Manual        — user clicked a button; device is already warm, use the
    //                   existing 1 s tear-down + 2.5 s warm-up path.
    //
    //   AutoStartup   — first launch; WinRT audio graph may still be dirty from
    //                   a previous session.  Use the full Deep Reset pipeline:
    //                   GC.Collect() + 2 s cool-down + 3 s StartAsync wait +
    //                   extra 2 s breathing room before OpenAsync + one-shot
    //                   5 s auto-fix if the first open looks silent.
    //
    //   AutoReconnect — device dropped mid-session; control channel is gone but
    //                   the A2DP stack is still warm.  Use Deep Reset (GC +
    //                   2 s cool-down) but skip the extra 2 s breathing room.
    // ═══════════════════════════════════════════════════════════════════════
    public enum ConnectionMode
    {
        Manual,
        AutoStartup,
        AutoReconnect
    }

    // ═══════════════════════════════════════════════════════════════════════
    // WatchdogResult — outcome of the Watchdog verification phase
    // ═══════════════════════════════════════════════════════════════════════
    internal enum WatchdogResult
    {
        Verified,           // Channel is Opened and stable after 2 s
        FakeConnection,     // OpenAsync succeeded but State dropped to Closed
        ConnectionLost,     // State changed to Closed during verification
        Disposed            // Connection was disposed during verification
    }

    public class BluetoothService : IDisposable
    {
        // ── Public events ────────────────────────────────────────────────
        public event EventHandler<string>?              StatusChanged;
        public event EventHandler<BluetoothDeviceInfo>? DeviceConnected;
        public event EventHandler<BluetoothDeviceInfo>? DeviceDisconnected;
        public event EventHandler<Exception>?           ErrorOccurred;

        // ── Public state ─────────────────────────────────────────────────
        public BluetoothDeviceInfo? CurrentDevice { get; private set; }
        public bool IsConnected => CurrentDevice?.Status == true;
        public bool IsAutoReconnectEnabled { get; set; } = true;
        public string? LastDeviceId { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        // 公开属性：供 MainWindow 事件处理器检查是否正在连接中
        // 用于 Event Debouncer
        // ═══════════════════════════════════════════════════════════════
        public bool IsConnecting => _isConnecting;

        // ── Private fields ───────────────────────────────────────────────
        // CRITICAL: must be a class-level field — local variables get GC'd
        // and the WinRT handle is silently released, causing "connected but
        // no sound" state.
        private AudioPlaybackConnection? _connection;

        private string? _lastConnectionId;
        private CancellationTokenSource _reconnectCts = new();
        private CancellationTokenSource _watchdogCts = new();
        private bool _disposed;
        private bool _isResetting;

        // ═══════════════════════════════════════════════════════════════
        // Execution Lock — 防止并发调用 ForceConnectAudioAsync
        // 当设备变化事件快速触发时，防止多个并发连接尝试导致竞态条件
        // ═══════════════════════════════════════════════════════════════
        private bool _isConnecting = false;

        // First-launch flag: set to true on construction, cleared after the
        // first successful AutoStartup connection.  Used to gate the extra
        // 2 s breathing room and the one-shot auto-fix.
        private bool _isFirstLaunch = true;

        // One-shot auto-fix gate: prevents the 5 s delayed fix from firing
        // more than once per connection session.
        private bool _autoFixScheduled;

        // ── Watchdog state tracking ──────────────────────────────────────
        // Used to detect "fake connections" where OpenAsync returns Success
        // but the underlying channel silently drops within seconds.
        private AudioPlaybackConnectionState _lastObservedState;
        private readonly object _stateLock = new();

        private const int ReconnectDelayMs  = 4000;
        private const int WatchdogVerificationMs = 2000;  // 2 s verification window

        public BluetoothService() => LoadLastDevice();

        // ════════════════════════════════════════════════════════════════
        // SCAN
        // ════════════════════════════════════════════════════════════════
        public async Task<List<BluetoothDeviceInfo>> GetPairedDevicesAsync()
        {
            var result = new List<BluetoothDeviceInfo>();
            try
            {
                var apcSelector = AudioPlaybackConnection.GetDeviceSelector();
                var apcDevices  = await DeviceInformation.FindAllAsync(apcSelector);
                foreach (var d in apcDevices)
                    result.Add(new BluetoothDeviceInfo
                    {
                        Id       = d.Id,
                        Name     = ExtractName(d.Name, d.Id),
                        IsPaired = true
                    });

                // Only AudioPlaybackConnection devices can be opened as A2DP
                // sink sources. Generic paired Bluetooth devices are not
                // connectable through this API and would fail at TryCreateFromId.
            }
            catch (Exception ex) { ErrorOccurred?.Invoke(this, ex); }
            return result;
        }

        private static string ExtractName(string name, string id)
        {
            if (!string.IsNullOrEmpty(name) && name != "Unknown Device") return name;
            var lastHash = id.LastIndexOf('#');
            if (lastHash > 0)
            {
                var part     = id.Substring(lastHash + 1);
                var nextHash = part.LastIndexOf('#');
                if (nextHash > 0) part = part.Substring(0, nextHash);
                part = Uri.UnescapeDataString(part);
                if (!string.IsNullOrWhiteSpace(part)) return part;
            }
            return name;
        }

        // ════════════════════════════════════════════════════════════════
        // PUBLIC CONNECT / SWITCH / RESET / DISCONNECT
        // ════════════════════════════════════════════════════════════════

        /// <summary>User clicked "Connect" — Manual mode, no deep reset.</summary>
        public async Task<bool> ConnectToDeviceAsync(string deviceId)
        {
            try
            {
                StatusChanged?.Invoke(this, "正在连接...");
                return await ForceConnectAudioAsync(deviceId, ConnectionMode.Manual);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                StatusChanged?.Invoke(this, $"❌ 连接出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>User clicked "Switch" — Manual mode.</summary>
        public async Task<bool> SwitchToDeviceAsync(string newDeviceId)
        {
            try
            {
                StatusChanged?.Invoke(this, "🔄 正在切换设备...");
                return await ForceConnectAudioAsync(newDeviceId, ConnectionMode.Manual);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                StatusChanged?.Invoke(this, $"❌ 切换出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 公开接口：供 MainWindow 环境感知事件处理器调用
        /// 使用 AutoReconnect 模式重建连接（Deep Reset + Watchdog 验证）
        /// </summary>
        public async Task<bool> ForceConnectAudioAsync(string deviceId, ConnectionMode mode)
        {
            return await ForceConnectAudioAsync(deviceId, mode, retryCount: 0);
        }

        /// <summary>
        /// Called on app startup to connect the last-used device.
        /// Uses AutoStartup mode (full Deep Reset + extra breathing room).
        /// </summary>
        public async Task AutoConnectLastDeviceAsync()
        {
            if (string.IsNullOrEmpty(LastDeviceId)) return;
            StatusChanged?.Invoke(this, "⏳ 等待蓝牙栈初始化...");
            await ForceConnectAudioAsync(LastDeviceId, ConnectionMode.AutoStartup);
        }

        /// <summary>
        /// User clicked "Fix Audio" / tray "修复声音".
        /// Always Manual mode — device is warm, user is present.
        /// </summary>
        public async Task<bool> ResetConnectionAsync()
        {
            if (_isResetting)
            {
                StatusChanged?.Invoke(this, "⚠️ 重置已在进行中，请稍候...");
                return false;
            }
            if (string.IsNullOrEmpty(_lastConnectionId))
            {
                StatusChanged?.Invoke(this, "❌ 没有活跃连接可重置");
                return false;
            }

            _isResetting = true;
            try
            {
                StatusChanged?.Invoke(this, "🔧 开始修复音频...");
                bool success = await ForceConnectAudioAsync(_lastConnectionId, ConnectionMode.Manual);
                StatusChanged?.Invoke(this, success ? "✅ 音频修复成功！" : "❌ 音频修复失败");
                return success;
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "⚠️ 修复被取消");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ 修复出错: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);
                return false;
            }
            finally
            {
                _isResetting = false;
            }
        }

        public async Task DisconnectAsync()
        {
            IsAutoReconnectEnabled = false;
            _reconnectCts.Cancel();
            _reconnectCts = new CancellationTokenSource();
            _watchdogCts.Cancel();
            _watchdogCts = new CancellationTokenSource();

            CleanupConnection(destroy: true);
            CurrentDevice      = null;
            _lastConnectionId  = null;
            _autoFixScheduled  = false;

            StatusChanged?.Invoke(this, "已断开连接");
            DeviceDisconnected?.Invoke(this, new BluetoothDeviceInfo());
            await Task.CompletedTask;
        }

        // ════════════════════════════════════════════════════════════════
        // DEEP RESET — mandatory pre-emptive cleanup for automated paths
        //
        // Explicitly nulls _connection and calls GC.Collect() so the .NET
        // runtime releases any lingering WinRT COM handles before we try to
        // create a new AudioPlaybackConnection.  Without this, Windows can
        // hand back a handle that points at a half-torn-down A2DP session,
        // which looks "connected" but produces no audio.
        //
        // The 2 s cool-down after disposal gives Windows time to fully
        // finish tearing down the previous A2DP session at the driver level.
        // ════════════════════════════════════════════════════════════════
        private async Task DeepResetAsync()
        {
            StatusChanged?.Invoke(this, "🧹 [深层重置] 强制释放 WinRT 句柄...");

            if (_connection != null)
            {
                try { _connection.StateChanged -= OnConnectionStateChanged; } catch { }
                try { _connection.Dispose(); }                               catch { }
                _connection = null;   // explicit null — allow GC to collect
            }

            // Force the .NET GC to release any lingering WinRT COM handles
            // immediately rather than waiting for the next collection cycle.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            StatusChanged?.Invoke(this, "⏳ [深层重置] 等待 OS 完成 A2DP 会话拆除（2秒）...");
            await Task.Delay(2000);   // cool-down: let Windows finish at driver level
        }

        // ════════════════════════════════════════════════════════════════
        // FORCE CONNECT — single source of truth for all connection work
        //
        // Mode-specific timing matrix:
        //
        //  Step                     │ Manual │ AutoStartup │ AutoReconnect
        //  ─────────────────────────┼────────┼─────────────┼──────────────
        //  Pre-cleanup              │ light  │ DeepReset   │ DeepReset
        //  Post-dispose wait        │ 1 s    │ (in Deep)   │ (in Deep)
        //  Post-StartAsync wait     │ 2.5 s  │ 3 s         │ 3 s
        //  Extra breathing room     │ —      │ +2 s *      │ —
        //  Watchdog verification    │ 2 s    │ 2 s         │ 2 s
        //  One-shot 5 s auto-fix    │ —      │ yes *       │ —
        //
        //  * only on _isFirstLaunch == true
        // ════════════════════════════════════════════════════════════════
        private async Task<bool> ForceConnectAudioAsync(
            string         targetDeviceId,
            ConnectionMode mode,
            int            retryCount = 0)
        {
            const int MaxRetries = 2;

            // ═══════════════════════════════════════════════════════════════
            // Execution Lock — 防止并发调用
            // Manual 模式（手动修复按钮）可以绕过锁，确保原子性
            // Auto 模式（自动连接/重连）需要检查锁避免竞态条件
            // ═══════════════════════════════════════════════════════════════
            if (mode != ConnectionMode.Manual && _isConnecting)
            {
                StatusChanged?.Invoke(this, "ℹ️ [防抖] 连接已在进行中，忽略重复请求");
                return false;
            }
            _isConnecting = true;

            try
            {
                // ═══════════════════════════════════════════════════════════════
                // UI State Masking — 屏蔽内部重试过程的错误显示
                // 在重试循环中，不显示"失败"，只显示中性进度状态
                // ═══════════════════════════════════════════════════════════════
                if (retryCount > 0)
                {
                    // 只有在完全失败后才显示失败，中间过程只显示进度
                    if (mode == ConnectionMode.Manual)
                    {
                        StatusChanged?.Invoke(this, $"🔄 重试连接 ({retryCount}/{MaxRetries})...");
                    }
                    else
                    {
                        // Auto 模式下显示中性进度状态
                        StatusChanged?.Invoke(this, $"🔄 正在重建音频路由 (尝试 {retryCount + 1}/3)...");
                    }
                }

                // ── Step 1: Pre-emptive cleanup ──────────────────────────
                StatusChanged?.Invoke(this, "步骤 1/6: 清理旧连接...");

                if (mode == ConnectionMode.AutoStartup || mode == ConnectionMode.AutoReconnect)
                {
                    // DEEP RESET: GC.Collect() + 2 s cool-down
                    await DeepResetAsync();
                }
                else
                {
                    // Manual: lightweight tear-down, 1 s wait
                    if (_connection != null)
                    {
                        try { _connection.StateChanged -= OnConnectionStateChanged; } catch { }
                        try { _connection.Dispose(); }                               catch { }
                        _connection = null;
                    }
                    StatusChanged?.Invoke(this, "步骤 1/6: 等待 OS 清理音频图（1秒）...");
                    await Task.Delay(1000);
                }

                // ── Step 2: Resolve device ID ────────────────────────────
                StatusChanged?.Invoke(this, "步骤 2/6: 查找设备...");

                var apcSelector = AudioPlaybackConnection.GetDeviceSelector();
                var apcDevices  = await DeviceInformation.FindAllAsync(apcSelector);
                string? connectionId = null;
                string  deviceName   = targetDeviceId;

                foreach (var d in apcDevices)
                {
                    if (d.Id == targetDeviceId ||
                        targetDeviceId.IndexOf(d.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        d.Name.IndexOf(targetDeviceId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        connectionId = d.Id;
                        deviceName   = ExtractName(d.Name, d.Id);
                        break;
                    }
                }

                if (connectionId == null && targetDeviceId.Contains("#{"))
                    connectionId = targetDeviceId;
                if (connectionId == null)
                    connectionId = targetDeviceId;

                _lastConnectionId = connectionId;

                // ── Step 3: Create connection instance ───────────────────
                // ═══════════════════════════════════════════════════════════
                // CRITICAL: Subscribe to StateChanged IMMEDIATELY after
                // TryCreateFromId.  This lets us monitor the connection's
                // "vital signs" and detect silent drops (fake connections).
                // ═══════════════════════════════════════════════════════════
                StatusChanged?.Invoke(this, "步骤 3/6: 创建连接实例...");
                _connection = AudioPlaybackConnection.TryCreateFromId(connectionId);

                if (_connection == null)
                {
                    StatusChanged?.Invoke(this, "❌ TryCreateFromId 返回 null — 系统拒绝访问");
                    StatusChanged?.Invoke(this, "💡 请尝试：以管理员身份运行，或检查系统蓝牙权限");
                    return false;
                }

                // ═══════════════════════════════════════════════════════════
                // Subscribe to StateChanged IMMEDIATELY — monitors life signs
                // ═══════════════════════════════════════════════════════════
                _connection.StateChanged += OnConnectionStateChanged;

                // Initialize watchdog tracking state
                lock (_stateLock)
                {
                    _lastObservedState = _connection.State;
                }

                // ── Step 4: StartAsync — open control channel ────────────
                StatusChanged?.Invoke(this, "步骤 4/6: 调用 StartAsync()（打开控制通道）...");
                await _connection.StartAsync();

                // Post-StartAsync warm-up: give the A2DP stack time to
                // initialise the media channel before we call OpenAsync.
                //
                // Android opens the control channel synchronously but
                // initialises the media channel asynchronously.  Calling
                // OpenAsync too soon returns Success but hands back an
                // uninitialised (silent) media stream — no exception thrown.
                //
                // Extended congestion delay for startup: If a Bluetooth Headset
                // is already connected, the radio bandwidth is congested.
                // Increase the delay to 5-6 seconds to ensure A2DP Sink has
                // enough time to negotiate even on a congested radio.
                int warmupMs;
                if (mode == ConnectionMode.Manual)
                {
                    warmupMs = 2500;
                    StatusChanged?.Invoke(this, $"步骤 4/6: 等待媒体通道初始化（{warmupMs / 1000.0:F1}秒）...");
                }
                else if (mode == ConnectionMode.AutoStartup && _isFirstLaunch)
                {
                    // Extended 5s delay for initial boot (accounts for headset congestion)
                    warmupMs = 5000;
                    StatusChanged?.Invoke(this, $"步骤 4/6: [首次启动/拥塞] 等待媒体通道初始化（{warmupMs / 1000.0:F1}秒）...");
                    await Task.Delay(warmupMs);
                    warmupMs = 0; // already waited
                }
                else
                {
                    // AutoReconnect or subsequent AutoStartup: 3 s, no extra
                    warmupMs = 3000;
                    StatusChanged?.Invoke(this, $"步骤 4/6: 等待媒体通道初始化（{warmupMs / 1000.0:F1}秒）...");
                }

                if (warmupMs > 0)
                    await Task.Delay(warmupMs);

                // ── Step 5: OpenAsync — take over media stream ───────────
                StatusChanged?.Invoke(this, "步骤 5/6: 调用 OpenAsync()（接管媒体流）...");
                var result = await _connection.OpenAsync();

                if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
                {
                    return await HandleConnectionFailure(targetDeviceId, mode, result, retryCount, MaxRetries);
                }

                // ═══════════════════════════════════════════════════════════
                // Step 6: WATCHDOG VERIFICATION (Closed-Loop Verification)
                //
                // OpenAsync returned Success, but on Android the A2DP channel
                // can silently drop within seconds.  We MUST verify that the
                // channel is actually stable before declaring success.
                //
                // Wait 2 seconds, then check if State is still Opened.
                // If it dropped to Closed, this was a "fake connection" —
                // route back to Deep Reset / Retry loop.
                // ═══════════════════════════════════════════════════════════
                StatusChanged?.Invoke(this, "步骤 6/6: 🔍 [看门狗] 正在验证通道稳定性...");
                StatusChanged?.Invoke(this, $"            等待 {WatchdogVerificationMs / 1000} 秒让 Android A2DP 栈稳定...");

                var watchdogResult = await RunWatchdogVerificationAsync();

                switch (watchdogResult)
                {
                    case WatchdogResult.Verified:
                        // ✅ Channel is stable — proceed to success
                        break;

                    case WatchdogResult.FakeConnection:
                    case WatchdogResult.ConnectionLost:
                        // ❌ OpenAsync succeeded but channel dropped — fake connection
                        StatusChanged?.Invoke(this, "⚠️ [看门狗] 检测到假连接！通道在验证期间意外关闭");
                        StatusChanged?.Invoke(this, "🔄 [看门狗] 将执行深层重置后重试...");

                        // Clean up the broken connection
                        if (_connection != null)
                        {
                            try { _connection.StateChanged -= OnConnectionStateChanged; } catch { }
                            try { _connection.Dispose(); }                               catch { }
                            _connection = null;
                        }

                        // Retry with Deep Reset (force AutoReconnect mode for retry path)
                        if (retryCount < MaxRetries)
                        {
                            await Task.Delay(1000);
                            return await ForceConnectAudioAsync(targetDeviceId, ConnectionMode.AutoReconnect, retryCount + 1);
                        }

                        StatusChanged?.Invoke(this, "❌ 连接失败：看门狗验证失败，已达到最大重试次数");
                        return false;

                    case WatchdogResult.Disposed:
                        StatusChanged?.Invoke(this, "⚠️ [看门狗] 连接在验证期间被释放");
                        return false;
                }

                // ═══════════════════════════════════════════════════════════
                // SUCCESS — Channel verified and stable
                // ═══════════════════════════════════════════════════════════
                CurrentDevice = new BluetoothDeviceInfo
                {
                    Id       = connectionId,
                    Name     = deviceName,
                    Status   = true,
                    IsPaired = true
                };
                SaveLastDevice(connectionId);

                StatusChanged?.Invoke(this, "═══════════════════════════════════════");
                StatusChanged?.Invoke(this, "🎉 连接成功！通道已验证并稳定");
                StatusChanged?.Invoke(this, $"   设备: {deviceName}");
                StatusChanged?.Invoke(this, "   媒体通道已初始化，音频应该现在可用");
                StatusChanged?.Invoke(this, "💡 请检查：");
                StatusChanged?.Invoke(this, "   1. 系统主音量是否开启（任务栏🔊）");
                StatusChanged?.Invoke(this, "   2. 手机蓝牙设置中「媒体音频」是否开启");
                StatusChanged?.Invoke(this, "   3. 音量合成器中的蓝牙音频接收器音量");
                StatusChanged?.Invoke(this, "═══════════════════════════════════════");

                DeviceConnected?.Invoke(this, CurrentDevice);

                // ── Error-Triggered Auto-Fix (AutoStartup first launch only) ──
                // OpenAsync returned Success, but on first launch the media
                // stream can still be silent due to Android's async init.
                // Schedule a one-shot ForceConnect after 5 s to catch this
                // case without requiring the user to click "Fix Audio".
                if (mode == ConnectionMode.AutoStartup && _isFirstLaunch && !_autoFixScheduled)
                {
                    _autoFixScheduled = true;
                    _isFirstLaunch    = false;   // clear flag before the async fire
                    _ = ScheduleAutoFixAsync(connectionId);
                }
                else if (mode == ConnectionMode.AutoStartup)
                {
                    _isFirstLaunch = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ 连接异常: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);

                if (retryCount < MaxRetries)
                {
                    StatusChanged?.Invoke(this, $"准备重试... ({retryCount}/{MaxRetries})");
                    await Task.Delay(2000);
                    return await ForceConnectAudioAsync(targetDeviceId, mode, retryCount + 1);
                }
                return false;
            }
            finally
            {
                // ═══════════════════════════════════════════════════════════════
                // 释放执行锁
                // ═══════════════════════════════════════════════════════════════
                _isConnecting = false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // WATCHDOG VERIFICATION
        //
        // Waits 2 seconds after OpenAsync succeeds, then checks if the
        // connection is still in Opened state.  This catches "fake
        // connections" where Android's A2DP stack silently drops the
        // channel within seconds of a successful open.
        // ════════════════════════════════════════════════════════════════
        private async Task<WatchdogResult> RunWatchdogVerificationAsync()
        {
            _watchdogCts.Cancel();
            _watchdogCts = new CancellationTokenSource();
            var token = _watchdogCts.Token;

            try
            {
                // Reset watchdog state before the wait
                lock (_stateLock)
                {
                    if (_connection != null)
                        _lastObservedState = _connection.State;
                }

                // Wait 2 seconds for the A2DP stack to settle
                await Task.Delay(WatchdogVerificationMs, token);

                // After the delay, check the actual state
                lock (_stateLock)
                {
                    if (_connection == null)
                        return WatchdogResult.Disposed;

                    var currentState = _connection.State;
                    _lastObservedState = currentState;

                    if (currentState == AudioPlaybackConnectionState.Opened)
                    {
                        // ✅ Channel is still open after 2 seconds — verified
                        StatusChanged?.Invoke(this, "✅ [看门狗] 通道验证通过，状态稳定 (Opened)");
                        return WatchdogResult.Verified;
                    }
                    else if (currentState == AudioPlaybackConnectionState.Closed)
                    {
                        // ❌ Channel dropped to Closed — fake connection
                        StatusChanged?.Invoke(this, $"❌ [看门狗] 通道验证失败，当前状态: {currentState}");
                        return WatchdogResult.FakeConnection;
                    }
                    else
                    {
                        // Unknown state — treat as connection lost
                        StatusChanged?.Invoke(this, $"⚠️ [看门狗] 通道状态异常: {currentState}");
                        return WatchdogResult.ConnectionLost;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return WatchdogResult.Disposed;
            }
        }

        // ════════════════════════════════════════════════════════════════
        // ERROR-TRIGGERED AUTO-FIX
        //
        // Fires exactly once, 5 s after a successful AutoStartup OpenAsync.
        // Rationale: OpenAsync can return Success while the media stream is
        // still uninitialised (Android async A2DP quirk).  A 5 s delay gives
        // the OS time to surface the problem (audio graph reports silence)
        // before we do a full ForceConnect cycle to recover.
        // ════════════════════════════════════════════════════════════════
        private async Task ScheduleAutoFixAsync(string connectionId)
        {
            StatusChanged?.Invoke(this, "⏳ [自动修复] 将在 5 秒后执行一次预防性修复...");
            await Task.Delay(5000);

            // Only proceed if we are still connected to the same device and
            // the user has not manually disconnected.
            if (!IsConnected || _lastConnectionId != connectionId)
            {
                StatusChanged?.Invoke(this, "ℹ️ [自动修复] 设备已变更，跳过预防性修复");
                _autoFixScheduled = false;
                return;
            }

            StatusChanged?.Invoke(this, "🔧 [自动修复] 执行预防性音频修复（首次启动优化）...");
            bool success = await ForceConnectAudioAsync(connectionId, ConnectionMode.AutoReconnect);
            StatusChanged?.Invoke(this, success
                ? "✅ [自动修复] 预防性修复完成"
                : "⚠️ [自动修复] 预防性修复未成功，请手动点击「修复声音」");
            _autoFixScheduled = false;
        }

        // ════════════════════════════════════════════════════════════════
        // FAILURE HANDLER
        // ════════════════════════════════════════════════════════════════
        private async Task<bool> HandleConnectionFailure(
            string                          targetDeviceId,
            ConnectionMode                  mode,
            AudioPlaybackConnectionOpenResult result,
            int                             retryCount,
            int                             maxRetries)
        {
            StatusChanged?.Invoke(this, $"❌ OpenAsync 失败: {result.Status}");

            if (result.ExtendedError != null)
            {
                var hr = result.ExtendedError.HResult;
                StatusChanged?.Invoke(this, $"   ExtendedError: 0x{hr:X8}");
                ErrorOccurred?.Invoke(this, new COMException(
                    $"AudioPlaybackConnection.OpenAsync failed: {result.Status}", hr));
            }

            switch (result.Status)
            {
                case AudioPlaybackConnectionOpenResultStatus.RequestTimedOut:
                    StatusChanged?.Invoke(this, "⏳ 连接超时 — 请在手机上选择此电脑播放音频");
                    break;
                case AudioPlaybackConnectionOpenResultStatus.DeniedBySystem:
                    StatusChanged?.Invoke(this, "❌ 系统拒绝 — 请以管理员身份运行");
                    break;
                default:
                    StatusChanged?.Invoke(this, $"❌ 未知错误: {result.Status}");
                    break;
            }

            // Destroy the broken connection before retrying
            if (_connection != null)
            {
                try { _connection.StateChanged -= OnConnectionStateChanged; } catch { }
                try { _connection.Dispose(); }                               catch { }
                _connection = null;
            }

            if (retryCount < maxRetries)
            {
                StatusChanged?.Invoke(this, $"⏳ 等待 2 秒后重试... ({retryCount}/{maxRetries})");
                await Task.Delay(2000);
                return await ForceConnectAudioAsync(targetDeviceId, mode, retryCount + 1);
            }

            StatusChanged?.Invoke(this, "❌ 连接失败，已达到最大重试次数");
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        // STATE CHANGE HANDLER (Life Signs Monitor)
        //
        // Called whenever the AudioPlaybackConnection's state changes.
        // This is our "life signs" monitor — if the state drops to Closed
        // unexpectedly after we opened it, we log it and trigger UI update
        // or automatic tear-down.
        // ════════════════════════════════════════════════════════════════
        private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
        {
            var state     = sender.State;
            var stateName = state switch
            {
                AudioPlaybackConnectionState.Opened => "Opened ✅",
                AudioPlaybackConnectionState.Closed => "Closed ❌",
                _                                   => $"Unknown ({state})"
            };

            // Update tracking state for watchdog
            lock (_stateLock)
            {
                _lastObservedState = state;
            }

            // Log state change
            StatusChanged?.Invoke(this, $"📡 [生命体征] 连接状态变化: {stateName}");

            if (state == AudioPlaybackConnectionState.Closed)
            {
                // ── Unexpected closed — trigger tear-down ────────────────
                CurrentDevice     = null;
                _autoFixScheduled = false;

                StatusChanged?.Invoke(this, "⚠️ [生命体征] 连接意外断开，正在清理状态...");
                DeviceDisconnected?.Invoke(this, new BluetoothDeviceInfo { Name = "已断开" });

                // Auto-reconnect if enabled
                if (IsAutoReconnectEnabled && !string.IsNullOrEmpty(_lastConnectionId))
                {
                    StatusChanged?.Invoke(this, "🔄 [生命体征] 触发自动重连...");
                    _ = TryReconnectLoopAsync(_lastConnectionId);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // RECONNECT LOOP — uses AutoReconnect mode (Deep Reset, no extra 2 s)
        // ════════════════════════════════════════════════════════════════
        private async Task TryReconnectLoopAsync(string connectionId)
        {
            _reconnectCts.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var token   = _reconnectCts.Token;
            int attempt = 0;

            while (!token.IsCancellationRequested && IsAutoReconnectEnabled)
            {
                attempt++;
                StatusChanged?.Invoke(this, $"第 {attempt} 次重连中...");
                try
                {
                    await Task.Delay(ReconnectDelayMs, token);
                    if (await ForceConnectAudioAsync(connectionId, ConnectionMode.AutoReconnect)) return;
                }
                catch (TaskCanceledException) { return; }
                catch { /* swallow, loop will retry */ }
            }
        }

        // ════════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════════
        private void CleanupConnection(bool destroy)
        {
            if (_connection == null) return;
            try { _connection.StateChanged -= OnConnectionStateChanged; } catch { }
            if (destroy)
            {
                try { _connection.Dispose(); } catch { }
                _connection = null;
            }
        }

        // ── Persistence ──────────────────────────────────────────────────
        private static string DataFolder => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothAudioReceiver");

        private void LoadLastDevice()
        {
            try
            {
                var f = System.IO.Path.Combine(DataFolder, "lastdevice.txt");
                if (System.IO.File.Exists(f))
                    LastDeviceId = System.IO.File.ReadAllText(f).Trim();
            }
            catch { }
        }

        private void SaveLastDevice(string id)
        {
            try
            {
                System.IO.Directory.CreateDirectory(DataFolder);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(DataFolder, "lastdevice.txt"), id);
                LastDeviceId = id;
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reconnectCts.Cancel();
            _reconnectCts.Dispose();
            _watchdogCts.Cancel();
            _watchdogCts.Dispose();
            CleanupConnection(destroy: true);
        }
    }
}
