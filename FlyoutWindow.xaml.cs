using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TrayLens;

internal partial class FlyoutWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int GclStyle = -26;
    private const int CsDropShadow = 0x20000;

    private readonly Queue<(double down, double up)> _history = new();
    private const int HistoryLength = 60;

    private bool _backdropOk;
    private bool _positioned;
    private bool _lightTheme;
    private bool _userVisible;
    private bool _pinned;
    private readonly TranslateTransform _slide = new();

    private Color _down = Color.FromRgb(0x10, 0x84, 0x6A);
    private Color _up = Color.FromRgb(0x1C, 0x5F, 0xB8);
    private Color _batt = Color.FromRgb(0x9A, 0x5A, 0x00);
    private Color _battLow = Color.FromRgb(0xB4, 0x1E, 0x28);
    private Color _textPrimary = Color.FromArgb(0xE4, 0, 0, 0);

    public FlyoutWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ApplyWindowChrome();
            ApplyTheme(IsSystemLight());
        };
        Loaded += (_, _) =>
        {
            RestorePosition();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        };
        SizeChanged += (_, _) =>
        {
            // 内容高度变了（展开详情、模块增删）就重新贴底，避免压到任务栏
            if (_pinned && IsVisible) PinToBottomRight();
        };
        Deactivated += (_, _) =>
        {
            // 点别处自动收起；但从面板打开设置页时别再收，不然用户会以为面板丢了
            if (!IsVisible) return;
            if (System.Windows.Application.Current?.Windows
                    .OfType<SettingsWindow>()
                    .Any(w => w.IsVisible) == true)
            {
                return;
            }

            HideWithAnimation();
        };
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SavePosition();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General
            or Microsoft.Win32.UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(() => ApplyTheme(IsSystemLight()));
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_pinned) Dispatcher.BeginInvoke(MoveToDefaultCorner);
    }

    private static bool IsSystemLight()
    {
        try
        {
            object? v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return !(v is int i) || i == 1;
        }
        catch
        {
            return true;
        }
    }

    private void ApplyWindowChrome()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));

        int backdrop = 3; // 亚克力
        int hr = DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int));
        _backdropOk = hr == 0;

        // 无边框窗口想要投影，只能自己挂 CS_DROPSHADOW
        int style = GetClassLong(hwnd, GclStyle);
        SetClassLong(hwnd, GclStyle, style | CsDropShadow);
    }

    private void ApplyTheme(bool light)
    {
        _lightTheme = light;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int dark = light ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        }

        if (light)
        {
            SetBrush("PanelBg", Color.FromArgb(0xE8, 0xF6, 0xF6, 0xF6));
            SetBrush("PanelBorder", Color.FromArgb(0x18, 0, 0, 0));
            SetBrush("CardBg", Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF));
            SetBrush("GraphBg", Color.FromArgb(0x0D, 0, 0, 0));
            SetBrush("TextPrimary", Color.FromArgb(0xE4, 0x1A, 0x1A, 0x1A));
            SetBrush("TextSecondary", Color.FromArgb(0x8A, 0, 0, 0));
            SetBrush("TextTertiary", Color.FromArgb(0x73, 0, 0, 0));
            _down = Color.FromRgb(0x0E, 0x7C, 0x62);
            _up = Color.FromRgb(0x18, 0x5A, 0xB4);
            _batt = Color.FromRgb(0x96, 0x55, 0x00);
            _battLow = Color.FromRgb(0xB0, 0x1C, 0x26);
            _textPrimary = Color.FromArgb(0xE4, 0x1A, 0x1A, 0x1A);
        }
        else
        {
            SetBrush("PanelBg", Color.FromArgb(0xB8, 0x1C, 0x1C, 0x1C));
            SetBrush("PanelBorder", Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            SetBrush("CardBg", Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            SetBrush("GraphBg", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            SetBrush("TextPrimary", Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            SetBrush("TextSecondary", Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
            SetBrush("TextTertiary", Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));
            _down = Color.FromRgb(0x7E, 0xF0, 0xC8);
            _up = Color.FromRgb(0x92, 0xCA, 0xFF);
            _batt = Color.FromRgb(0xFF, 0xC7, 0x78);
            _battLow = Color.FromRgb(0xFF, 0x82, 0x8C);
            _textPrimary = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
        }

        SetBrush("AccentDown", _down);
        SetBrush("AccentUp", _up);
        SetBrush("BattColor", _batt);
        SetBrush("BattLow", _battLow);

        if (!_backdropOk)
        {
            Shell.Background = new SolidColorBrush(light
                ? Color.FromArgb(0xF4, 0xF3, 0xF3, 0xF3)
                : Color.FromArgb(0xF2, 0x1E, 0x1E, 0x1E));
        }

        ApplyBackdrop();
        RedrawGraph();
    }

    /// <summary>面板背景：亚克力 / 纯色 / 自定义图片，外加统一的不透明度。</summary>
    private void ApplyBackdrop()
    {
        if (_settings is null || Shell is null) return;

        double opacity = Math.Clamp(_settings.PanelOpacity, 20, 100) / 100.0;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        if (_settings.BackdropStyle == "image" && File.Exists(_settings.BackgroundImage))
        {
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(_settings.BackgroundImage);
                image.EndInit();

                Shell.Background = new ImageBrush(image)
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = opacity
                };
                SetBackdropType(hwnd, 1); // 纯图片，不要系统材质
                return;
            }
            catch
            {
                // 图片读不了就退回亚克力
            }
        }

        if (_settings.BackdropStyle == "solid")
        {
            Shell.Background = new SolidColorBrush(_lightTheme
                ? Color.FromArgb((byte)(opacity * 255), 0xF6, 0xF6, 0xF6)
                : Color.FromArgb((byte)(opacity * 255), 0x1C, 0x1C, 0x1C));
            SetBackdropType(hwnd, 1);
            return;
        }

        SetBackdropType(hwnd, 3); // 亚克力
        Shell.Background = new SolidColorBrush(_lightTheme
            ? Color.FromArgb((byte)(opacity * 0xE8), 0xF6, 0xF6, 0xF6)
            : Color.FromArgb((byte)(opacity * 0xB8), 0x1C, 0x1C, 0x1C));
    }

    private static void SetBackdropType(IntPtr hwnd, int type)
    {
        if (hwnd == IntPtr.Zero) return;
        DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref type, sizeof(int));
    }

    private void SetBrush(string key, Color color) => Resources[key] = new SolidColorBrush(color);

    /// <summary>自检渲染用：不显示窗口也能套用主题。</summary>
    internal void ApplyThemeForPreview(bool light) => ApplyTheme(light);

    internal double ShellWidth => Shell.ActualWidth;

    // ---------- 窗口交互 ----------

    public event Action? SettingsRequested;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    /// <summary>拖到屏幕外就再也找不回来，所以每次移动完都夹一次。</summary>
    private void ClampToWorkArea()
    {
        Rect work = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 200;

        double maxLeft = Math.Max(work.Left + 8, work.Right - w - 8);
        double maxTop = Math.Max(work.Top + 8, work.Bottom - h - 8);

        Left = Math.Min(Math.Max(Left, work.Left + 8), maxLeft);
        Top = Math.Min(Math.Max(Top, work.Top + 8), maxTop);
    }

    private void MoveToDefaultCorner()
    {
        PinToBottomRight();
    }

    /// <summary>贴住工作区右下角。面板高度会随内容变化，所以每次尺寸变化都要重新贴。</summary>
    private void PinToBottomRight()
    {
        Rect work = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : 200;

        double targetLeft = work.Right - w - 8;
        double targetTop = work.Bottom - h - 8;

        if (Math.Abs(Left - targetLeft) > 0.5) Left = targetLeft;
        if (Math.Abs(Top - targetTop) > 0.5) Top = targetTop;

        SavePosition();
    }

    /// <summary>窗口和可视区是否还有像样的交集——用于判断「点托盘图标时该隐藏还是该拉回来」。</summary>
    public bool IsMostlyOnScreen()
    {
        Rect work = SystemParameters.WorkArea;
        var self = new Rect(Left, Top, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
        Rect overlap = Rect.Intersect(self, work);
        if (overlap.IsEmpty) return false;
        return overlap.Width * overlap.Height >= 0.35 * self.Width * self.Height;
    }

    public void OnShellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (_pinned) return; // 焊死了就别拖
        if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;

        try
        {
            DragMove();
        }
        catch
        {
            // 鼠标已松开时 DragMove 会抛异常
        }
        ClampToWorkArea();
        SavePosition();
    }

    /// <summary>点在按钮或标了 nodrag 的卡片上时不算拖窗口，否则行点击会被 DragMove 吃掉。</summary>
    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (source is FrameworkElement fe && fe.Tag as string == "nodrag") return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnShellMenuItem(string action)
    {
        switch (action)
        {
            case "settings":
                SettingsRequested?.Invoke();
                break;
            case "reset":
                MoveToDefaultCorner();
                break;
            case "hide":
                Hide();
                break;
            case "topmost":
                Topmost = !Topmost;
                if (_settings is not null)
                {
                    _settings.Topmost = Topmost;
                    _settings.Save();
                }
                break;
            case "exit":
                System.Windows.Application.Current?.Shutdown();
                break;
        }
    }

    private void OnShellRightClick(object sender, MouseButtonEventArgs e)
    {
        bool light = _lightTheme;
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(light
                ? Color.FromArgb(0xF2, 0xFA, 0xFA, 0xFA)
                : Color.FromArgb(0xF2, 0x2A, 0x2A, 0x2A)),
            Foreground = new SolidColorBrush(light ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0x80, 0x80, 0x80)),
            PlacementTarget = Shell
        };

        menu.Items.Add(MenuItem("设置…", "settings"));
        menu.Items.Add(MenuItem("重置位置", "reset"));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("隐藏到托盘", "hide"));
        menu.Items.Add(MenuItem(Topmost ? "取消置顶" : "始终置顶", "topmost"));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("退出 TrayLens", "exit"));

        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem MenuItem(string header, string action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => OnShellMenuItem(action);
        return item;
    }

    // ---------- 位置记忆 ----------

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayLens", "window.json");

    private sealed record PlacementState(double Left, double Top);

    private void SavePosition()
    {
        // 预览/离屏渲染的窗口不许写位置文件，否则正式启动会被假坐标带偏
        if (!_userVisible) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new PlacementState(Left, Top)));
        }
        catch
        {
            // 存档失败不影响使用
        }
    }

    private void RestorePosition()
    {
        if (_positioned) return;
        _positioned = true;

        Rect work = SystemParameters.WorkArea;

        try
        {
            if (File.Exists(StatePath))
            {
                var st = JsonSerializer.Deserialize<PlacementState>(File.ReadAllText(StatePath));
                if (st is not null && st.Left > work.Left - ActualWidth + 40 && st.Left < work.Right - 40
                    && st.Top > work.Top - 10 && st.Top < work.Bottom - 40)
                {
                    Left = st.Left;
                    Top = st.Top;
                    return;
                }
            }
        }
        catch
        {
            // 存档坏了就回默认位置
        }

        Left = work.Right - ActualWidth - 24;
        Top = work.Bottom - ActualHeight - 24;
    }

    // ---------- 数据显示 ----------

    public void UpdateState(double downBps, double upBps, WlanProbe.WlanState wlan,
        VpnState vpn, IReadOnlyList<WirelessDevice> devices, string netLine = "")
    {
        DownValue.Text = SpeedMeter.FormatBps(downBps);
        UpValue.Text = SpeedMeter.FormatBps(upBps);

        VpnTag.Text = (_settings?.ShowProxyLine ?? true) && vpn.Active ? DescribeVpn(vpn) : "";

        WlanSsid.Text = wlan.Connected ? wlan.Ssid : "未连接";
        WlanSignal.Text = wlan.Connected ? $"信号 {wlan.Signal}%" : "";
        WlanDetail.Text = wlan.Connected
            ? $"接收 {wlan.RxRateKbps / 1000} Mbps · 发送 {wlan.TxRateKbps / 1000} Mbps"
            : "未连接到任何无线网络";
        WlanMore.Text = netLine;

        _history.Enqueue((downBps, upBps));
        while (_history.Count > HistoryLength) _history.Dequeue();
        RedrawGraph();

        UpdateDevices(devices);
    }

    private static string DescribeVpn(VpnState vpn)
    {
        if (!string.IsNullOrEmpty(vpn.Node))
        {
            string node = vpn.Node.Length > 20 ? vpn.Node[..20] + "…" : vpn.Node;
            return vpn.Delay >= 0 ? $"{node} · {vpn.Delay}ms" : node;
        }

        return vpn.Kind == VpnKind.Tun ? $"TUN · {vpn.Detail}" : $"代理 · {vpn.Detail}";
    }

    private void UpdateDevices(IReadOnlyList<WirelessDevice> devices)
    {
        _deviceCount = devices.Count;
        DeviceList.Children.Clear();

        foreach (WirelessDevice d in devices.OrderBy(x => x.Percent ?? int.MaxValue))
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

            var pct = new TextBlock
            {
                Text = d.PercentText,
                FontSize = 12,
                Width = 44,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            pct.SetResourceReference(TextBlock.ForegroundProperty,
                d.Percent is null ? "TextTertiary" : (d.Percent <= 20 ? "BattLow" : "BattColor"));
            DockPanel.SetDock(pct, Dock.Right);
            row.Children.Add(pct);

            var name = new TextBlock
            {
                Text = d.Name,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var namePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            namePanel.Children.Add(name);

            if (!string.IsNullOrEmpty(d.Source))
            {
                var source = new TextBlock
                {
                    Text = "· " + d.Source,
                    FontSize = 9.5,
                    Margin = new Thickness(6, 0, 0, 1),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                source.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
                namePanel.Children.Add(source);
            }

            row.Children.Add(namePanel);

            DeviceList.Children.Add(row);
        }

        Reflow();
    }

    // ---------------- 模块化 ----------------

    private AppSettings? _settings;
    private int _deviceCount;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        Topmost = settings.Topmost;
        _hoverEnabled = settings.HoverExpand;
        _hoverShowMs = Math.Max(0, settings.HoverShowDelayMs);
        _hoverHideMs = Math.Max(0, settings.HoverHideDelayMs);

        bool wasPinned = _pinned;
        _pinned = settings.PinnedToTaskbar;
        if (_pinned && !wasPinned) MoveToDefaultCorner();

        VpnTag.Visibility = settings.ShowProxyLine ? Visibility.Visible : Visibility.Collapsed;
        if (!settings.ShowProxyLine) VpnTag.Text = "";

        ApplyBackdrop();
        Reflow();
    }

    private FrameworkElement? SectionFor(string id) => id switch
    {
        "network" => SecNetwork,
        "wlan" => SecWlan,
        "perf" => SecPerf,
        "devices" => SecDevices,
        _ => null
    };

    /// <summary>按设置里的顺序重排、按开关显隐；设备模块还要看有没有设备。</summary>
    private void Reflow()
    {
        if (_settings is null) return;

        Modules.Children.Clear();
        int index = 0;

        foreach (ModuleSetting module in _settings.Modules)
        {
            if (!module.Enabled) continue;

            FrameworkElement? section = SectionFor(module.Id);
            if (section is null) continue;
            if (module.Id == "devices" && _deviceCount == 0)
            {
                section.Visibility = Visibility.Collapsed;
                continue;
            }

            section.Visibility = Visibility.Visible;
            section.Margin = index == 0 ? new Thickness(0) : new Thickness(0, 8, 0, 0);
            Modules.Children.Add(section);
            index++;
        }
    }

    // ---------- 系统性能 ----------

    private sealed class PerfTile
    {
        public System.Windows.Shapes.Ellipse Ring = null!;
        public TextBlock Percent = null!;
        public string Title = "";
        public int Index;
        public Color Accent;
    }

    /// <summary>每个指标各自持有一个延迟器，共用一个会互相取消。</summary>
    private sealed class HoverDelay
    {
        private System.Windows.Threading.DispatcherTimer? _timer;

        public void Schedule(Action action, int delayMs)
        {
            Cancel();
            if (delayMs <= 0)
            {
                action();
                return;
            }

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            _timer = timer;
            timer.Start();
        }

        public void Cancel()
        {
            _timer?.Stop();
            _timer = null;
        }
    }

    private readonly List<PerfTile> _perfTiles = new();
    private readonly Queue<double> _cpuHist = new();
    private readonly Queue<double> _memHist = new();
    private readonly Queue<double> _diskHist = new();
    private readonly Queue<double> _gpuHist = new();
    private readonly Queue<double> _cpuTempHist = new();
    private readonly Queue<double> _gpuTempHist = new();
    private int _selectedPerf = -1;
    private PerfSnapshot _lastPerf;
    private bool _hoverEnabled = true;
    private int _hoverShowMs = 200;
    private int _hoverHideMs = 600;
    private bool _wlanPinned;
    private readonly HoverDelay _wlanHover = new();

    private void OnWlanMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_hoverEnabled) return;
        _wlanHover.Schedule(() => WlanMore.Visibility = Visibility.Visible, _hoverShowMs);
    }

    private void OnWlanMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_wlanPinned) return;
        if (!_hoverEnabled) return;
        _wlanHover.Schedule(() => WlanMore.Visibility = Visibility.Collapsed, _hoverHideMs);
    }

    private void OnWlanClick(object sender, MouseButtonEventArgs e)
    {
        _wlanPinned = !_wlanPinned;
        WlanMore.Visibility = _wlanPinned ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPerfTiles()
    {
        if (_perfTiles.Count > 0) return;

        AddPerfTile(0, "CPU", _down);
        AddPerfTile(1, "内存", _up);
        AddPerfTile(2, "磁盘", Color.FromArgb(0x99, 0x80, 0x80, 0x80));
        AddPerfTile(3, "GPU", _batt);
    }

    private void AddPerfTile(int index, string title, Color accent)
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 8, 4, 6),
            Margin = new Thickness(3, 0, 3, 0),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = "nodrag",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        root.SetResourceReference(Border.BackgroundProperty, "GraphBg");
        root.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");

        var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

        var ringGrid = new Grid { Width = 40, Height = 40 };

        var track = new System.Windows.Shapes.Ellipse
        {
            Width = 40,
            Height = 40,
            StrokeThickness = 3.5,
            StrokeDashArray = new DoubleCollection { 1, 0 }
        };
        track.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TrackBg");

        var ring = new System.Windows.Shapes.Ellipse
        {
            Width = 40,
            Height = 40,
            StrokeThickness = 3.5,
            StrokeDashArray = new DoubleCollection { 0, 100 },
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = new SolidColorBrush(accent),
            RenderTransform = new RotateTransform(-90, 20, 20)
        };

        var pct = new TextBlock
        {
            Text = "--",
            FontSize = 10.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        pct.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");

        ringGrid.Children.Add(track);
        ringGrid.Children.Add(ring);
        ringGrid.Children.Add(pct);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 9.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");

        stack.Children.Add(ringGrid);
        stack.Children.Add(label);
        root.Child = stack;

        root.MouseLeftButtonUp += (_, _) => SelectPerf(index);

        _perfTiles.Add(new PerfTile { Ring = ring, Percent = pct, Title = title, Index = index, Accent = accent });
        PerfTiles.Children.Add(root);
    }

    private void SelectPerf(int index)
    {
        _selectedPerf = _selectedPerf == index ? -1 : index;

        bool expanded = _selectedPerf >= 0;
        // 详情直接占掉四宫格的位置，面板不会因为展开而变高
        PerfTiles.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        PerfDetail.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        if (expanded)
        {
            PerfDetailTitle.Text = _perfTiles[_selectedPerf].Title;

            // 内存/磁盘：左边曲线、右边列表（曲线常年一条直线，没必要占满）
            bool sideList = _selectedPerf is 1 or 2;
            PerfSideList.Visibility = sideList ? Visibility.Visible : Visibility.Collapsed;
            System.Windows.Controls.Grid.SetColumnSpan(PerfChartHost, sideList ? 1 : 2);

            UpdatePerfDetail();
            RedrawPerfChart();
            FadeIn(PerfDetail);
        }
        else
        {
            FadeIn(PerfTiles);
        }
    }

    /// <summary>淡入用的通用小动画。</summary>
    private static void FadeIn(UIElement element)
    {
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void OnPerfDetailClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedPerf >= 0) SelectPerf(_selectedPerf);
    }

    internal void PreviewSelectTile(int index) => SelectPerf(index);

    private void UpdatePerfDetail()
    {
        if (_selectedPerf < 0) return;

        PerfSnapshot s = _lastPerf;
        RenderSideList(s);

        switch (_selectedPerf)
        {
            case 0:
                PerfDetailValue.Text = TempSuffix(s.CpuPercent, s.CpuTempC);
                PerfDetailText.Text = $"{s.Threads} 线程 · 基础 {s.BaseMhz:0} MHz";
                break;
            case 1:
                double availGb = (s.MemTotalMb - s.MemUsedMb) / 1024.0;
                PerfDetailValue.Text = s.MemPercent.ToString("0") + "%";
                PerfDetailText.Text =
                    $"{s.MemUsedMb / 1024:0.0} / {s.MemTotalMb / 1024:0.0} GB · 可用 {availGb:0.0} GB · " +
                    $"提交 {s.CommitUsedMb / 1024:0.0} / {s.CommitTotalMb / 1024:0.0} GB";
                break;
            case 2:
                (double freeGb, double totalGb) = s.SystemDisk();
                PerfDetailValue.Text = s.DiskPercent.ToString("0") + "%";
                PerfDetailText.Text =
                    $"读 {SpeedMeter.FormatBps(s.DiskReadBps)}/s · 写 {SpeedMeter.FormatBps(s.DiskWriteBps)}/s · " +
                    $"{PerfSnapshot.SystemDrive.TrimEnd('\\')} 剩 {freeGb:0} / {totalGb:0} GB";
                break;
            case 3:
                PerfDetailValue.Text = TempSuffix(s.GpuPercent, s.GpuTempC);
                PerfDetailText.Text = s.VramTotalMb > 0
                    ? $"{s.GpuName} · 显存 {s.VramMb / 1024:0.0} / {s.VramTotalMb / 1024:0.0} GB"
                    : $"{s.GpuName} · 显存 {s.VramMb / 1024:0.0} GB";
                break;
        }
    }

    private static string TempSuffix(double percent, double? temp) =>
        temp is double t ? $"{percent:0}% · {t:0}℃" : percent.ToString("0") + "%";

    /// <summary>内存页显示进程 Top、磁盘页显示分区；其它指标不显示侧栏。</summary>
    private void RenderSideList(PerfSnapshot s)
    {
        if (_selectedPerf is not (1 or 2)) return;

        PerfSideList.Children.Clear();

        if (_selectedPerf == 1)
        {
            foreach (ProcInfo p in s.TopProcs.Take(3))
            {
                PerfSideList.Children.Add(SideRow(
                    p.Name.Length > 11 ? p.Name[..11] : p.Name,
                    $"{p.Cpu:0}%"));
            }
        }
        else
        {
            foreach (DriveStat d in s.Drives.Take(4))
            {
                PerfSideList.Children.Add(SideRow(d.Name, $"{d.FreeGb:0}/{d.TotalGb:0}G"));
            }
        }
    }

    private DockPanel SideRow(string left, string right)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var value = new TextBlock
        {
            Text = right,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);

        var name = new TextBlock
        {
            Text = left,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiary");
        row.Children.Add(name);

        return row;
    }

    public void UpdatePerf(PerfSnapshot s)
    {
        BuildPerfTiles();
        if (_perfTiles.Count < 4) return;

        _lastPerf = s;

        SetRing(_perfTiles[0], s.CpuPercent);
        SetRing(_perfTiles[1], s.MemPercent);
        SetRing(_perfTiles[2], s.DiskPercent);
        SetRing(_perfTiles[3], s.GpuPercent);
        Push(_cpuHist, s.CpuPercent);
        Push(_memHist, s.MemPercent);
        Push(_diskHist, s.DiskPercent);
        Push(_gpuHist, s.GpuPercent);
        if (s.CpuTempC is double ct) Push(_cpuTempHist, ct);
        if (s.GpuTempC is double gt) Push(_gpuTempHist, gt);

        if (_selectedPerf >= 0)
        {
            UpdatePerfDetail();
            RedrawPerfChart();
        }
    }

    private static void SetRing(PerfTile tile, double value)
    {
        value = Math.Clamp(value, 0, 100);
        tile.Percent.Text = value.ToString("0") + "%";

        // 圆环用 StrokeDashArray 画弧：周长按直径 40、线宽 3.5 算
        const double diameter = 40;
        const double thickness = 3.5;
        double radius = (diameter - thickness) / 2;
        double circumference = 2 * Math.PI * radius;
        double units = circumference / thickness;

        tile.Ring.StrokeDashArray = new DoubleCollection
        {
            value / 100.0 * units,
            units
        };
    }

    private static void Push(Queue<double> queue, double value)
    {
        queue.Enqueue(Math.Clamp(value, 0, 100));
        while (queue.Count > 60) queue.Dequeue();
    }

    private void RedrawPerfChart()
    {
        if (PerfCanvas is null || _selectedPerf < 0) return;

        PerfCanvas.Children.Clear();
        double w = PerfChartHost.ActualWidth > 0 ? PerfChartHost.ActualWidth : Width - 70;
        const double h = 56;

        (Queue<double> history, Color color) = _selectedPerf switch
        {
            0 => (_cpuHist, Color.FromArgb(0xFF, _down.R, _down.G, _down.B)),
            1 => (_memHist, Color.FromArgb(0xFF, _up.R, _up.G, _up.B)),
            2 => (_diskHist, Color.FromArgb(0xCC, 0x80, 0x80, 0x80)),
            _ => (_gpuHist, Color.FromArgb(0xFF, _batt.R, _batt.G, _batt.B))
        };

        if (history.Count >= 2)
        {
            PerfCanvas.Children.Add(Line(history, w, h, color, 1.6));
        }

        // CPU / GPU 页把温度叠在同一条时间轴上（0-100℃ 直接映射到 0-100%）
        Queue<double>? temp = _selectedPerf switch
        {
            0 => _cpuTempHist,
            3 => _gpuTempHist,
            _ => null
        };

        if (temp is { Count: >= 2 })
        {
            PerfCanvas.Children.Add(Line(temp, w, h, Color.FromRgb(0xE8, 0x6A, 0x3C), 1.1));
        }
    }

    private static System.Windows.Shapes.Polyline Line(Queue<double> history, double width, double height,
        Color color, double thickness)
    {
        var line = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round
        };

        double step = width / 59.0;
        int offset = 60 - history.Count;
        int i = 0;
        foreach (double value in history)
        {
            double y = height - 2 - (value / 100.0) * (height - 4);
            line.Points.Add(new Point((offset + i) * step, y));
            i++;
        }

        return line;
    }

    private void RedrawGraph()
    {
        if (GraphCanvas is null) return;
        GraphCanvas.Children.Clear();
        if (_history.Count < 2) return;

        double w = Math.Max(GraphCanvas.ActualWidth, Width - 70);
        const double h = 50;
        double max = Math.Max(_history.Max(p => Math.Max(p.down, p.up)), 1024);

        GraphCanvas.Children.Add(BuildPolyline(p => p.down, max, w, h, _down, 1.6));
        GraphCanvas.Children.Add(BuildPolyline(p => p.up, max, w, h, _up, 1.2));
    }

    private System.Windows.Shapes.Polyline BuildPolyline(Func<(double down, double up), double> pick, double max,
        double width, double height, Color color, double thickness)
    {
        var line = new System.Windows.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round
        };

        double step = width / (HistoryLength - 1);
        int i = 0;
        int offset = HistoryLength - _history.Count;
        foreach (var sample in _history)
        {
            double value = Math.Min(pick(sample) / max, 1.0);
            double x = (offset + i) * step;
            double y = height - 2 - value * (height - 4);
            line.Points.Add(new Point(x, y));
            i++;
        }
        return line;
    }

    public void ShowWidget()
    {
        _userVisible = true;
        if (!IsVisible) Show();
        UpdateLayout();
        if (!_positioned) RestorePosition();
        if (_pinned) MoveToDefaultCorner();
        else ClampToWorkArea();

        PlayIntro();

        // 内容布局完成后再贴一次，确保底部不越界
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_pinned)
            {
                PinToBottomRight();
                AppHost.Log($"panel pinned: top={Top:0} height={ActualHeight:0} bottom={Top + ActualHeight:0} " +
                            $"workBottom={SystemParameters.WorkArea.Bottom:0}");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>淡入 + 轻微上浮，180ms 缓出。</summary>
    private void PlayIntro()
    {
        if (Shell is null) return;

        Shell.RenderTransform = _slide;
        _slide.Y = 10;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

        Shell.BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>收起时淡出再隐藏，避免硬切。</summary>
    public void HideWithAnimation()
    {
        if (!IsVisible) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            Shell.BeginAnimation(OpacityProperty, null);
            Shell.Opacity = 1;
            Hide();
        };

        Shell.BeginAnimation(OpacityProperty, fade);
    }

    // ---------- 自检渲染 ----------

    public void RenderToPng(string path)
    {
        var root = (FrameworkElement)Content;
        root.Measure(new Size(Width, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(Width, root.DesiredSize.Height)));
        root.UpdateLayout();

        double h = root.DesiredSize.Height;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(h),
            96, 96, PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(_lightTheme ? Colors.White : Color.FromRgb(0x1B, 0x1B, 0x1B)),
                null, new Rect(0, 0, Width, h));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, Width, h));
        }

        bmp.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(path);
        encoder.Save(fs);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetClassLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetClassLong(IntPtr hwnd, int index, int value);
}
