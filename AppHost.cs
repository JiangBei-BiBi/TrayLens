using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using FormsTimer = System.Windows.Forms.Timer;

namespace TrayLens;

/// <summary>托盘常驻宿主：两个托盘图标 + 1 秒采样 + 飞窗（点击图标弹出）。</summary>
internal sealed class AppHost : IDisposable
{
    private readonly NotifyIcon _netTray;
    private readonly NotifyIcon _battTray;
    private readonly DispatcherTimer _timer;
    private readonly SpeedMeter _meter = new();
    private readonly PerfMonitor _perf = new();
    private readonly PdhMonitor _pdh = new();
    private readonly HardwareMonitor _hw = new();
    private readonly ProcessMonitor _proc = new();
    private int _cpuHighTicks;
    private int _memHighTicks;
    private int _gpuHighTicks;
    private DateTime _lastCpuAlert = DateTime.MinValue;
    private DateTime _lastMemAlert = DateTime.MinValue;
    private DateTime _lastGpuAlert = DateTime.MinValue;
    private DateTime _lastGpuTempAlert = DateTime.MinValue;
    private DateTime _lastBattAlert = DateTime.MinValue;
    private readonly int _iconSize;
    private readonly FlyoutWindow _flyout;
    private readonly AppSettings _settings = AppSettings.Load();
    private SettingsWindow? _settingsWindow;

    private const int HotkeyId = 0x54A1;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const int WmHotkey = 0x0312;
    private System.Windows.Interop.HwndSource? _hotkeySource;

    private WlanProbe.WlanState _wlan = WlanProbe.WlanState.Disconnected;
    private VpnState _vpn = VpnState.Off;
    private ClashClient.ClashStatus _clash;
    private bool _clashBusy;
    private List<WirelessDevice> _devices = new();
    private int _tick;
    private bool _btScanning;
    private bool _disposed;

    private Icon? _netIcon;
    private Icon? _battIcon;

    public AppHost()
    {
        _iconSize = Math.Max(16, SystemInformation.SmallIconSize.Width);

        _netTray = new NotifyIcon { Visible = true, ContextMenuStrip = BuildMenu() };
        _netTray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };

        _battTray = new NotifyIcon { Visible = false, ContextMenuStrip = BuildMenu() };
        _battTray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleFlyout();
        };

        _flyout = new FlyoutWindow();
        _flyout.ApplySettings(_settings);
        _flyout.SettingsRequested += OpenSettings;

        RegisterHotkey();
        ApplyStartup(_settings.StartWithWindows);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Tick();
        StartBluetoothScan();

        // 独立窗口：启动就显示，不再依赖托盘唤出
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                PushFlyoutState();
                _flyout.ShowWidget();
                Log($"window shown visible={_flyout.IsVisible} rect={_flyout.Left},{_flyout.Top} " +
                    $"{_flyout.ActualWidth}x{_flyout.ActualHeight} shellW={_flyout.ShellWidth}");
            }
            catch (Exception ex)
            {
                Log("show window failed: " + ex);
            }
        }));
    }

    internal static string LogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayLens", "startup.log");

    internal static void Log(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
            System.IO.File.AppendAllText(LogPath,
                $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem("显示详情", null, (_, _) => ToggleFlyout()));
        menu.Items.Add(new ToolStripMenuItem("设置…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出 TrayLens", null, (_, _) => ExitApp()));
        return menu;
    }

    private void OpenSettings()
    {
        // 齿轮是开关：开着就关掉
        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowNear(_flyout);
    }

    private void ApplySettings(AppSettings settings)
    {
        bool hotkeyChanged = !string.Equals(_settings.HotkeyModifiers, settings.HotkeyModifiers, StringComparison.Ordinal)
                             || !string.Equals(_settings.HotkeyKey, settings.HotkeyKey, StringComparison.Ordinal);

        _flyout.ApplySettings(settings);
        UpdateBatteryIcon();

        ApplyStartup(settings.StartWithWindows);

        if (hotkeyChanged) RegisterHotkey();
        if (!settings.ShowProxyLine)
        {
            _vpn = VpnState.Off;
            _clash = default;
        }
        if (_flyout.IsVisible) PushFlyoutState();
    }

    private void ToggleFlyout()
    {
        // 可见且确实在屏幕上才隐藏；跑到屏幕外就把它拉回来
        if (_flyout.IsVisible && _flyout.IsMostlyOnScreen())
        {
            _flyout.HideWithAnimation();
            return;
        }

        PushFlyoutState();
        _flyout.ShowWidget();
    }

    private void PushFlyoutState()
    {
        string netLine = SystemDetails.NetworkLine();
        _flyout.UpdateState(_meter.DownBps, _meter.UpBps, _wlan, _vpn, _devices, netLine);
        _flyout.UpdatePerf(new PerfSnapshot(
            _perf.CpuPercent, _perf.MemoryPercent, _perf.UsedMb, _perf.TotalMb,
            _perf.CommitUsedMb, _perf.CommitTotalMb, _perf.Threads, _perf.BaseMhz,
            _pdh.DiskPercent, _pdh.DiskReadBps, _pdh.DiskWriteBps,
            _pdh.GpuPercent, _pdh.VramMb, SystemDetails.VramTotalMb, SystemDetails.GpuName,
            _hw.CpuTemp, _hw.GpuTemp,
            _proc.Top, PerfSnapshot.ReadDrives()));
    }

    private void Tick()
    {
        if (_disposed) return;

        _meter.Sample();
        _perf.Sample();
        _pdh.Sample();

        if (_tick % 2 == 0)
        {
            _hw.Sample();
            _proc.Sample();
        }

        CheckAlerts();

        if (_tick % 2 == 0)
        {
            _wlan = WlanProbe.Query();
            _vpn = _settings.ShowProxyLine ? CurrentVpn() : VpnState.Off;
        }

        if (_settings.ShowProxyLine && _tick % 2 == 0 && !_clashBusy) StartClashScan();
        if (_tick % 5 == 0) StartBluetoothScan();
        _tick++;

        UpdateNetworkIcon();

        if (_flyout.IsVisible) PushFlyoutState();
    }

    private void UpdateNetworkIcon()
    {
        string text = SpeedMeter.FormatBps(_meter.DownBps);
        Icon next = IconFactory.CreateSingle(text, _iconSize,
            IconFactory.DownLight, IconFactory.DownDark, 0.44f);

        _netTray.Icon = next;
        Icon? old = _netIcon;
        _netIcon = next;
        old?.Dispose();

        string net = _wlan.Connected
            ? $"{_wlan.Ssid}  {_wlan.Signal}%  收{_wlan.RxRateKbps / 1000}/发{_wlan.TxRateKbps / 1000}Mbps"
            : "无线：未连接";
        string vpn = _vpn.Active
            ? (_vpn.Kind == VpnKind.Tun ? $"TUN：{_vpn.Detail}" : $"系统代理：{_vpn.Detail}")
            : "代理：未启用";

        string tip =
            $"TrayLens · 网速\n" +
            $"↓{SpeedMeter.FormatBps(_meter.DownBps)}/s  ↑{SpeedMeter.FormatBps(_meter.UpBps)}/s\n" +
            $"{net}\n{vpn}";
        if (tip.Length > 120) tip = tip.Substring(0, 120);
        _netTray.Text = tip;
    }

    /// <summary>能用 Clash API 就用真实节点，拿不到就退回注册表/网卡判断。</summary>
    private VpnState CurrentVpn()
    {
        if (_clash.Ok)
        {
            if (_clash.Mode == "direct") return VpnState.Off;
            return new VpnState(true, _clash.Tun ? VpnKind.Tun : VpnKind.SystemProxy,
                _clash.Tun ? "TUN" : "系统代理", _clash.Node, _clash.Delay);
        }

        return VpnDetector.Detect();
    }

    private void StartClashScan()
    {
        _clashBusy = true;
        Task.Run(() =>
        {
            ClashClient.ClashStatus status;
            try
            {
                status = ClashClient.Query();
            }
            catch
            {
                status = default;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _clash = status;
                _clashBusy = false;
                _vpn = CurrentVpn();
                if (_flyout.IsVisible) PushFlyoutState();
            });
        });
    }

    /// <summary>注册（或重注册）全局热键。</summary>
    private void RegisterHotkey()
    {
        try
        {
            if (_hotkeySource is null)
            {
                var parameters = new System.Windows.Interop.HwndSourceParameters("TrayLensHotkey")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0
                };

                _hotkeySource = new System.Windows.Interop.HwndSource(parameters);
                _hotkeySource.AddHook(HotkeyProc);
            }

            UnregisterHotKey(_hotkeySource.Handle, HotkeyId);

            (uint modifiers, uint key) = ParseHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            string label = $"{_settings.HotkeyModifiers}+{_settings.HotkeyKey}";

            if (RegisterHotKey(_hotkeySource.Handle, HotkeyId, modifiers, key))
            {
                Log($"hotkey {label} registered");
            }
            else
            {
                Log($"hotkey {label} 注册失败（可能被别的程序占用）");
            }
        }
        catch (Exception ex)
        {
            Log("hotkey init failed: " + ex.Message);
        }
    }

    private static (uint modifiers, uint key) ParseHotkey(string? modifiers, string? key)
    {
        uint mods = 0;
        string m = modifiers ?? "";

        if (m.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= ModControl;
        if (m.Contains("Alt", StringComparison.OrdinalIgnoreCase)) mods |= ModAlt;
        if (m.Contains("Shift", StringComparison.OrdinalIgnoreCase)) mods |= ModShift;
        if (m.Contains("Win", StringComparison.OrdinalIgnoreCase)) mods |= ModWin;

        uint vk = 0;
        string k = (key ?? "N").Trim().ToUpperInvariant();
        if (k.Length == 1 && char.IsLetterOrDigit(k[0])) vk = k[0];

        if (vk == 0) vk = 'N';
        if (mods == 0) mods = ModControl | ModAlt;

        return (mods, vk);
    }

    private IntPtr HotkeyProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleFlyout();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>阈值预警：持续超线才提醒，阈值/时长/文案都可在设置里改。</summary>
    private void CheckAlerts()
    {
        if (_settings.CpuAlertEnabled)
        {
            _cpuHighTicks = _perf.CpuPercent > _settings.CpuAlertThreshold ? _cpuHighTicks + 1 : 0;

            if (_cpuHighTicks >= Math.Max(1, _settings.CpuAlertSeconds)
                && (DateTime.Now - _lastCpuAlert).TotalMinutes > 10)
            {
                _lastCpuAlert = DateTime.Now;
                _cpuHighTicks = 0;
                Notify(_settings.CpuAlertText, $"当前 {_perf.CpuPercent:0}%");
            }
        }

        if (_settings.MemAlertEnabled)
        {
            _memHighTicks = _perf.MemoryPercent > _settings.MemAlertThreshold ? _memHighTicks + 1 : 0;

            if (_memHighTicks >= Math.Max(1, _settings.MemAlertSeconds)
                && (DateTime.Now - _lastMemAlert).TotalMinutes > 10)
            {
                _lastMemAlert = DateTime.Now;
                _memHighTicks = 0;
                Notify(_settings.MemAlertText, $"当前 {_perf.MemoryPercent:0}%");
            }
        }

        if (_settings.GpuAlertEnabled)
        {
            _gpuHighTicks = _pdh.GpuPercent > _settings.GpuAlertThreshold ? _gpuHighTicks + 1 : 0;

            if (_gpuHighTicks >= Math.Max(1, _settings.GpuAlertSeconds)
                && (DateTime.Now - _lastGpuAlert).TotalMinutes > 10)
            {
                _lastGpuAlert = DateTime.Now;
                _gpuHighTicks = 0;
                Notify(_settings.GpuAlertText, $"当前 {_pdh.GpuPercent:0}%");
            }

            if (_hw.GpuTemp is double temp && temp >= _settings.GpuTempAlertThreshold
                && (DateTime.Now - _lastGpuTempAlert).TotalMinutes > 10)
            {
                _lastGpuTempAlert = DateTime.Now;
                Notify(_settings.GpuTempAlertText, $"当前 {temp:0}℃");
            }
        }

        if (!_settings.BattAlertEnabled) return;

        WirelessDevice? lowBattery = _devices
            .Where(d => d.Percent is not null && d.Percent <= _settings.BattAlertThreshold)
            .OrderBy(d => d.Percent!.Value)
            .Cast<WirelessDevice?>()
            .FirstOrDefault();

        if (lowBattery is not null && (DateTime.Now - _lastBattAlert).TotalMinutes > 30)
        {
            _lastBattAlert = DateTime.Now;
            Notify(_settings.BattAlertText, $"{lowBattery.Value.Name} 当前 {lowBattery.Value.Percent}%");
        }
    }

    /// <summary>把当前 exe 路径写进 HKCU\...\Run，实现开机自启（不需要管理员）。</summary>
    private static void ApplyStartup(bool enabled)
    {
        try
        {
            using Microsoft.Win32.RegistryKey? run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run is null) return;

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length == 0) return;
                run.SetValue("TrayLens", "\"" + exe + "\"");
                Log("startup enabled: " + exe);
            }
            else
            {
                run.DeleteValue("TrayLens", false);
                Log("startup disabled");
            }
        }
        catch (Exception ex)
        {
            Log("startup apply failed: " + ex.Message);
        }
    }

    private void Notify(string title, string text)
    {
        try
        {
            var window = new NotificationWindow(title, text);
            window.Show();
            Log($"alert: {title} - {text}");
        }
        catch (Exception ex)
        {
            Log("notify failed: " + ex.Message);
        }
    }

    private void StartBluetoothScan()
    {
        if (_btScanning) return;
        _btScanning = true;

        Task.Run(async () =>
        {
            List<WirelessDevice> list;
            try
            {
                list = await WirelessScanner.ScanAsync(CancellationToken.None);
            }
            catch
            {
                list = new List<WirelessDevice>();
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _devices = list;
                _btScanning = false;
                UpdateBatteryIcon();
            });
        });
    }

    private void UpdateBatteryIcon()
    {
        if (_devices.Count == 0 || !_settings.ShowBatteryIcon)
        {
            _battTray.Visible = false;
            return;
        }

        WirelessDevice? lowest = _devices
            .Where(d => d.Percent is not null)
            .OrderBy(d => d.Percent!.Value)
            .Cast<WirelessDevice?>()
            .FirstOrDefault();

        if (lowest is null)
        {
            _battTray.Visible = false;
            return;
        }

        int level = lowest.Value.Percent!.Value;
        bool low = level <= 20;

        Icon next = IconFactory.CreateSingle(level.ToString(), _iconSize,
            low ? IconFactory.WarnLight : IconFactory.BattLight,
            low ? IconFactory.WarnDark : IconFactory.BattDark,
            0.62f);

        _battTray.Icon = next;
        Icon? old = _battIcon;
        _battIcon = next;
        old?.Dispose();

        string tip = "TrayLens · 无线设备\n" +
                     string.Join("\n", _devices.Select(d => $"{d.Name}  {d.PercentText}"));
        if (tip.Length > 120) tip = tip.Substring(0, 120);
        _battTray.Text = tip;
        _battTray.Visible = true;

        if (_flyout.IsVisible) PushFlyoutState();
    }

    private void ExitApp()
    {
        Dispose();
        System.Windows.Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hotkeySource is not null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HotkeyId);
            _hotkeySource.Dispose();
            _hotkeySource = null;
        }

        _timer.Stop();
        _netTray.Visible = false;
        _battTray.Visible = false;
        _netTray.Dispose();
        _battTray.Dispose();
        _pdh.Dispose();
        _hw.Dispose();
        _flyout.Close();
    }
}
