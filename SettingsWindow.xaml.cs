using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using Size = System.Windows.Size;

namespace TrayLens;

internal partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onChanged;
    private readonly bool _light;
    private bool _initializing;
    private bool _ready;
    private int _tabIndex;

    public SettingsWindow(AppSettings settings, Action<AppSettings> onChanged)
    {
        InitializeComponent();
        _settings = settings;
        _onChanged = onChanged;
        _light = IconFactory.TaskbarIsLight();
        ApplyPalette();
        BuildRows();
        TopmostCheck.IsChecked = settings.Topmost;
        BatteryIconCheck.IsChecked = settings.ShowBatteryIcon;
        HoverCheck.IsChecked = settings.HoverExpand;
        ProxyCheck.IsChecked = settings.ShowProxyLine;
        PinCheck.IsChecked = settings.PinnedToTaskbar;
        StartupCheck.IsChecked = settings.StartWithWindows;

        int[] delays = { 0, 100, 200, 400, 600, 800, 1200 };
        foreach (int d in delays)
        {
            ShowDelayBox.Items.Add(d + " ms");
            HideDelayBox.Items.Add(d + " ms");
        }

        _initializing = true;

        ShowDelayBox.SelectedIndex = Math.Max(0, Array.IndexOf(delays, settings.HoverShowDelayMs));
        HideDelayBox.SelectedIndex = Math.Max(0, Array.IndexOf(delays, settings.HoverHideDelayMs));

        BackdropBox.Items.Add("亚克力（系统材质）");
        BackdropBox.Items.Add("纯色");
        BackdropBox.Items.Add("自定义图片");
        BackdropBox.SelectedIndex = settings.BackdropStyle switch
        {
            "solid" => 1,
            "image" => 2,
            _ => 0
        };

        OpacitySlider.Value = Math.Clamp(settings.PanelOpacity, 20, 100);
        OpacityValue.Text = settings.PanelOpacity + "%";
        ImagePathText.Text = string.IsNullOrEmpty(settings.BackgroundImage) ? "未选择" : settings.BackgroundImage;

        CpuAlertCheck.IsChecked = settings.CpuAlertEnabled;
        CpuThresholdBox.Text = settings.CpuAlertThreshold.ToString();
        CpuSecondsBox.Text = settings.CpuAlertSeconds.ToString();
        CpuTextBox.Text = settings.CpuAlertText;

        MemAlertCheck.IsChecked = settings.MemAlertEnabled;
        MemThresholdBox.Text = settings.MemAlertThreshold.ToString();
        MemSecondsBox.Text = settings.MemAlertSeconds.ToString();
        MemTextBox.Text = settings.MemAlertText;

        GpuAlertCheck.IsChecked = settings.GpuAlertEnabled;
        GpuThresholdBox.Text = settings.GpuAlertThreshold.ToString();
        GpuSecondsBox.Text = settings.GpuAlertSeconds.ToString();
        GpuTextBox.Text = settings.GpuAlertText;
        GpuTempBox.Text = settings.GpuTempAlertThreshold.ToString("0");
        GpuTempTextBox.Text = settings.GpuTempAlertText;

        BattAlertCheck.IsChecked = settings.BattAlertEnabled;
        BattThresholdBox.Text = settings.BattAlertThreshold.ToString();
        BattTextBox.Text = settings.BattAlertText;

        HotkeyModBox.Items.Add("Ctrl+Alt");
        HotkeyModBox.Items.Add("Ctrl+Shift");
        HotkeyModBox.Items.Add("Alt+Shift");
        HotkeyModBox.Items.Add("无");

        for (char c = 'A'; c <= 'Z'; c++) HotkeyKeyBox.Items.Add(c.ToString());
        for (char c = '0'; c <= '9'; c++) HotkeyKeyBox.Items.Add(c.ToString());

        HotkeyModBox.SelectedItem = settings.HotkeyModifiers;
        if (HotkeyModBox.SelectedIndex < 0) HotkeyModBox.SelectedIndex = 0;

        HotkeyKeyBox.SelectedItem = settings.HotkeyKey.ToUpperInvariant();
        if (HotkeyKeyBox.SelectedIndex < 0) HotkeyKeyBox.SelectedIndex = 'N' - 'A';

        _initializing = false;

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND，和面板一致的 10px 圆角
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));

            int dark = _light ? 0 : 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));

            // 句柄有了才能设材质，构造函数里调用是空转
            ApplyBackdrop();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        Loaded += (_, _) =>
        {
            Shell.Opacity = 0;
            Shell.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                });
        };

        _ready = true;
        ApplyBackdrop();
    }

    /// <summary>和主面板保持同一套材质：亚克力/纯色同步；自定义图片不同步（避免选图时看不清控件）。</summary>
    private void ApplyBackdrop()
    {
        if (_settings is null) return;

        string style = _settings.BackdropStyle == "image" ? "acrylic" : _settings.BackdropStyle;
        double opacity = Math.Clamp(_settings.PanelOpacity, 20, 100) / 100.0;

        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int type = style == "solid" ? 1 : 3;
            DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int));
        }

        Shell.Background = new SolidColorBrush(_light
            ? Color.FromArgb((byte)(opacity * (style == "solid" ? 255 : 0xE8)), 0xF6, 0xF6, 0xF6)
            : Color.FromArgb((byte)(opacity * (style == "solid" ? 255 : 0xB8)), 0x1C, 0x1C, 0x1C));
    }

    private void OnAlertChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _initializing || _settings is null) return;

        _settings.CpuAlertEnabled = CpuAlertCheck.IsChecked == true;
        _settings.CpuAlertThreshold = ParseNumber(CpuThresholdBox.Text, 90, 1, 100);
        _settings.CpuAlertSeconds = ParseNumber(CpuSecondsBox.Text, 30, 1, 3600);
        _settings.CpuAlertText = CpuTextBox.Text;

        _settings.MemAlertEnabled = MemAlertCheck.IsChecked == true;
        _settings.MemAlertThreshold = ParseNumber(MemThresholdBox.Text, 90, 1, 100);
        _settings.MemAlertSeconds = ParseNumber(MemSecondsBox.Text, 30, 1, 3600);
        _settings.MemAlertText = MemTextBox.Text;

        _settings.GpuAlertEnabled = GpuAlertCheck.IsChecked == true;
        _settings.GpuAlertThreshold = ParseNumber(GpuThresholdBox.Text, 90, 1, 100);
        _settings.GpuAlertSeconds = ParseNumber(GpuSecondsBox.Text, 30, 1, 3600);
        _settings.GpuTempAlertThreshold = ParseNumber(GpuTempBox.Text, 85, 30, 120);
        _settings.GpuAlertText = GpuTextBox.Text;
        _settings.GpuTempAlertText = GpuTempTextBox.Text;

        _settings.BattAlertEnabled = BattAlertCheck.IsChecked == true;
        _settings.BattAlertThreshold = ParseNumber(BattThresholdBox.Text, 20, 1, 100);
        _settings.BattAlertText = BattTextBox.Text;

        Notify();
    }

    private static int ParseNumber(string? text, int fallback, int min, int max) =>
        int.TryParse(text, out int value) ? Math.Clamp(value, min, max) : fallback;

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        if (!int.TryParse(button.Tag?.ToString(), out int index)) return;
        SelectTab(index);
    }

    internal void SelectTab(int index)
    {
        PageDisplay.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageLook.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageAlerts.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        FrameworkElement? page = index switch
        {
            0 => PageDisplay,
            1 => PageLook,
            _ => PageAlerts
        };

        if (page is not null)
        {
            page.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                });
        }

        UpdateTabVisuals(index);
    }

    private void UpdateTabVisuals(int index)
    {
        _tabIndex = index;
        var tabs = new[] { TabDisplay, TabLook, TabAlerts };
        bool light = _light;

        for (int i = 0; i < tabs.Length; i++)
        {
            bool selected = i == index;
            tabs[i].Background = selected
                ? new SolidColorBrush(light ? Color.FromArgb(0x22, 0, 0, 0) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF))
                : System.Windows.Media.Brushes.Transparent;
            tabs[i].BorderBrush = selected
                ? new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
                : System.Windows.Media.Brushes.Transparent;
            tabs[i].Foreground = new SolidColorBrush(selected
                ? (light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF0, 0xF0, 0xF0))
                : (light ? Color.FromArgb(0x99, 0, 0, 0) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
        }
    }

    private void OnHotkeyChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || _initializing || _settings is null) return;

        if (HotkeyModBox.SelectedItem is string mods && HotkeyKeyBox.SelectedItem is string key)
        {
            _settings.HotkeyModifiers = mods;
            _settings.HotkeyKey = key;
            Notify();
        }
    }

    private void OnBackdropChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || _initializing || _settings is null) return;

        _settings.BackdropStyle = BackdropBox.SelectedIndex switch
        {
            1 => "solid",
            2 => "image",
            _ => "acrylic"
        };
        Notify();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 载入时 Minimum/Maximum 变化也会触发，那时 _settings 还没赋值
        if (!_ready || _initializing || _settings is null) return;

        _settings.PanelOpacity = (int)Math.Round(OpacitySlider.Value);
        if (OpacityValue is not null) OpacityValue.Text = _settings.PanelOpacity + "%";
        Notify();
    }

    private void OnPickImage(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Title = "选择面板背景图片"
        };

        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        _settings.BackgroundImage = dlg.FileName;
        _settings.BackdropStyle = "image";

        _initializing = true;
        BackdropBox.SelectedIndex = 2;
        _initializing = false;

        ImagePathText.Text = dlg.FileName;
        Notify();
    }

    private void ApplyPalette()
    {
        Color fg = _light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF0, 0xF0, 0xF0);
        Color muted = _light ? Color.FromArgb(0x8A, 0, 0, 0) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

        Background = System.Windows.Media.Brushes.Transparent;
        Shell.BorderBrush = new SolidColorBrush(_light ? Color.FromArgb(0x18, 0, 0, 0) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

        TitleText.Foreground = new SolidColorBrush(fg);
        SectionText.Foreground = new SolidColorBrush(muted);
        TopmostCheck.Foreground = new SolidColorBrush(fg);
        BatteryIconCheck.Foreground = new SolidColorBrush(fg);

        foreach (TextBlock label in new[]
                 {
                     LookSectionText, BackdropLabel, OpacityLabel, ShowDelayLabel, HideDelayLabel,
                     ImageHintText, AlertHintText, OpacityValue
                 })
        {
            label.Foreground = new SolidColorBrush(muted);
        }

        UpdateTabVisuals(_tabIndex);

        // 控件圆角风格与面板保持一致
        Resources["CtlBg"] = new SolidColorBrush(_light
            ? Color.FromArgb(0x12, 0, 0, 0)
            : Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        Resources["CtlBorder"] = new SolidColorBrush(_light
            ? Color.FromArgb(0x1F, 0, 0, 0)
            : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Resources["CtlHover"] = new SolidColorBrush(_light
            ? Color.FromArgb(0x1C, 0, 0, 0)
            : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        Resources["CtlPopup"] = new SolidColorBrush(_light
            ? Color.FromRgb(0xF8, 0xF8, 0xF8)
            : Color.FromRgb(0x2A, 0x2A, 0x2A));
    }

    private void BuildRows()
    {
        ModuleList.Children.Clear();

        Color fg = _light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF0, 0xF0, 0xF0);

        for (int i = 0; i < _settings.Modules.Count; i++)
        {
            ModuleSetting module = _settings.Modules[i];
            var meta = AppSettings.Catalog.First(c => c.Id == module.Id);

            // 关键：for 的循环变量会被闭包共享，必须复制一份给闭包用
            int rowIndex = i;

            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = true };

            // 右：上移 / 下移
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var up = MakeButton("\uE70E", rowIndex > 0, () => Move(rowIndex, -1));
            var down = MakeButton("\uE70D", rowIndex < _settings.Modules.Count - 1, () => Move(rowIndex, 1));
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);

            var check = new CheckBox
            {
                IsChecked = module.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            check.Click += (_, _) =>
            {
                module.Enabled = check.IsChecked == true;
                Notify();
            };
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);

            var textPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var glyph = new TextBlock
            {
                Text = meta.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(fg)
            };
            var label = new TextBlock
            {
                Text = meta.Title,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(fg)
            };
            textPanel.Children.Add(glyph);
            textPanel.Children.Add(label);
            row.Children.Add(textPanel);

            ModuleList.Children.Add(row);
        }
    }

    private System.Windows.Controls.Button MakeButton(string glyph, bool enabled, Action action)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 11,
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            IsEnabled = enabled
        };
        b.Click += (_, _) => action();
        return b;
    }

    private void Move(int index, int delta)
    {
        if (!_settings.MoveModule(index, delta)) return;

        BuildRows();
        Notify();
    }

    private void Notify()
    {
        _settings.Save();
        AppHost.Log("settings: " + string.Join(" > ",
            _settings.Modules.Select(m => m.Id + (m.Enabled ? "" : "(off)"))));
        ApplyBackdrop();
        _onChanged(_settings);
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        _settings.Topmost = TopmostCheck.IsChecked == true;
        _settings.ShowBatteryIcon = BatteryIconCheck.IsChecked == true;
        _settings.HoverExpand = HoverCheck.IsChecked == true;
        _settings.ShowProxyLine = ProxyCheck.IsChecked == true;
        _settings.PinnedToTaskbar = PinCheck.IsChecked == true;
        _settings.StartWithWindows = StartupCheck.IsChecked == true;
        Notify();
    }

    private void OnDelayChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready || _initializing || _settings is null) return;
        if (ShowDelayBox.SelectedIndex < 0 || HideDelayBox.SelectedIndex < 0) return;

        _settings.HoverShowDelayMs = ParseDelay(ShowDelayBox.SelectedItem as string);
        _settings.HoverHideDelayMs = ParseDelay(HideDelayBox.SelectedItem as string);
        Notify();
    }

    private static int ParseDelay(string? text) =>
        int.TryParse(text?.Replace(" ms", ""), out int v) ? v : 200;

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        // 点在按钮/复选框上时不能拖窗口，否则会抢掉它们的点击
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        try { DragMove(); } catch { }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.ToggleButton
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>先量好内容高度、定好位置再显示，避免在默认位置闪一下。</summary>
    public void ShowNear(Window anchor)
    {
        if (Content is FrameworkElement root)
        {
            root.Measure(new Size(Width, double.PositiveInfinity));
            double measured = root.DesiredSize.Height;
            PositionNear(anchor, measured);
        }

        Show();
    }

    /// <summary>优先放左边，放不下就放右边，再夹进工作区。</summary>
    public void PositionNear(Window anchor, double knownHeight = 0)
    {
        Rect work = SystemParameters.WorkArea;
        double w = Width;
        double h = knownHeight > 0 ? knownHeight : (ActualHeight > 0 ? ActualHeight : 340);

        double left = anchor.Left - w - 10;
        if (left < work.Left + 8) left = anchor.Left + anchor.ActualWidth + 10;
        if (left + w > work.Right - 8) left = work.Right - w - 8;

        double top = Math.Min(Math.Max(anchor.Top, work.Top + 8), work.Bottom - h - 8);

        Left = left;
        Top = top;
    }

    /// <summary>自检渲染：套用主题后把设置页画成 PNG。</summary>
    public void RenderToPng(string path)
    {
        var root = (FrameworkElement)Content;
        root.Measure(new Size(Width, double.PositiveInfinity));
        root.Arrange(new Rect(new Size(Width, root.DesiredSize.Height)));
        root.UpdateLayout();

        double h = root.DesiredSize.Height;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(Width), (int)Math.Ceiling(h), 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);

        var visual = new System.Windows.Media.DrawingVisual();
        using (System.Windows.Media.DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(_light ? Color.FromRgb(0xF6, 0xF6, 0xF6) : Color.FromRgb(0x20, 0x20, 0x20)),
                null, new Rect(0, 0, Width, h));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, Width, h));
        }

        bmp.Render(visual);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using System.IO.FileStream fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }
}
