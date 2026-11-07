using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace TrayLens;

/// <summary>
/// 自己画的提醒浮窗。不用 NotifyIcon.ShowBalloonTip：Win11 上系统气泡经常被静默丢弃，
/// 用户根本看不到，自己弹一个反而可靠。
/// </summary>
internal sealed class NotificationWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly bool _dark;

    public NotificationWindow(string title, string message)
    {
        _dark = !IconFactory.TaskbarIsLight();

        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "TrayLens 提醒";

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x1A, 0x1A, 0x1A))
        };

        var bodyText = new TextBlock
        {
            Text = message,
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_dark
                ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xA0, 0, 0, 0))
        };

        var stack = new StackPanel();
        stack.Children.Add(titleText);
        stack.Children.Add(bodyText);

        var shell = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack,
            Background = new SolidColorBrush(_dark
                ? Color.FromArgb(0xF2, 0x22, 0x22, 0x22)
                : Color.FromArgb(0xF2, 0xF8, 0xF8, 0xF8)),
            BorderBrush = new SolidColorBrush(_dark
                ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x1F, 0, 0, 0))
        };

        Content = shell;
        MouseLeftButtonUp += (_, _) => Close();

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int dark = _dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        };

        Loaded += (_, _) => PlaceAboveTaskbar();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            FadeOutAndClose();
        };
        _timer.Start();

        Opacity = 0;
        Loaded += (_, _) =>
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>右下角、任务栏上方，和主面板同一侧。</summary>
    private void PlaceAboveTaskbar()
    {
        Rect work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 12;
        Top = work.Bottom - ActualHeight - 12;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
