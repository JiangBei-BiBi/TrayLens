using System;
using System.Threading;
using System.Windows;

namespace TrayLens;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--make-icon")
        {
            IconBuilder.BuildIco(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--preview")
        {
            IconFactory.RenderPreview(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--compare")
        {
            IconFactory.RenderCompare(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--glyphs2")
        {
            var list = new[]
            {
                0xE701, 0xE702, 0xE703, 0xE704, 0xE705, 0xE706, 0xE708, 0xE70A,
                0xE850, 0xE851, 0xE854, 0xE85C, 0xE85E, 0xE85F, 0xE83F, 0xE7E8,
                0xE950, 0xE951, 0xE952, 0xE953, 0xE954, 0xE955, 0xE956, 0xE957,
                0xE958, 0xE959, 0xE95A, 0xE95B, 0xE95C, 0xE95D, 0xE95E, 0xE95F,
                0xE990, 0xE991, 0xE9A0, 0xE9A1, 0xE9A2, 0xE9A3, 0xE9A4, 0xE9A5,
                0xE9D9, 0xE9DA, 0xE9E9, 0xEA0C, 0xE917, 0xE92C, 0xE9F5, 0xE7C1,
            };
            GlyphSheet.RenderList(args[1], list);
            return;
        }

        if (args.Length >= 2 && args[0] == "--glyphs")
        {
            int start = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0xE700;
            int count = args.Length > 3 ? Convert.ToInt32(args[3]) : 256;
            GlyphSheet.Render(args[1], start, count);
            return;
        }

        if (args.Length >= 2 && args[0] == "--demo-settings")
        {
            var app2 = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var win = new SettingsWindow(AppSettings.Load(), _ => { });
            win.Show();
            var killer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            killer.Tick += (_, _) => { killer.Stop(); app2.Shutdown(); };
            killer.Start();
            app2.Run();
            return;
        }

        if (args.Length >= 2 && args[0] == "--settings-move")
        {
            var s = AppSettings.Load();
            string before = string.Join(" > ", s.Modules.Select(m => m.Id));
            s.MoveModule(1, -1); // 把第二个模块上移
            string after = string.Join(" > ", s.Modules.Select(m => m.Id));
            string after2 = (s.MoveModule(0, -1) ? "moved" : "rejected-out-of-range");
            System.IO.File.WriteAllText(args[1], $"before={before}\nafter={after}\nedge={after2}\n");
            return;
        }

        if (args.Length >= 2 && args[0] == "--settings-shot")
        {
            var app0 = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var settings = AppSettings.Load();
            var window = new SettingsWindow(settings, _ => { });
            if (args.Length > 2 && int.TryParse(args[2], out int tab)) window.SelectTab(tab);
            window.RenderToPng(args[1]);
            app0.Shutdown();
            return;
        }

        if (args.Length >= 2 && args[0] == "--flyout-shot")
        {
            FlyoutShot.Render(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--test")
        {
            var meter = new SpeedMeter();
            meter.Sample();
            Thread.Sleep(1200);
            meter.Sample();
            var wlan = WlanProbe.Query();
            var devices = WirelessScanner.ScanAsync().GetAwaiter().GetResult();
            var vpn = VpnDetector.Detect();
            string idle = VpnDetector.DetectIdleApp() ?? "-";
            var clash = ClashClient.Query();
            var pdh = new PdhMonitor();
            var perf = new PerfMonitor();
            perf.Sample();
            pdh.Sample();
            Thread.Sleep(600);
            pdh.Sample();
            perf.Sample();
            var hw = new HardwareMonitor();
            hw.Sample();
            System.Threading.Thread.Sleep(400);
            hw.Sample();
            string temps = $"cpuTemp={(hw.CpuTemp?.ToString("0") ?? "n/a")} gpuTemp={(hw.GpuTemp?.ToString("0") ?? "n/a")}";
            hw.Dispose();

            string sys = $"gpu={pdh.GpuPercent:0}% vram={pdh.VramMb:0}MB disk={pdh.DiskPercent:0}% " +
                         $"rd={SpeedMeter.FormatBps(pdh.DiskReadBps)}/s wr={SpeedMeter.FormatBps(pdh.DiskWriteBps)}/s " +
                         $"cpu={perf.CpuPercent:0}% mem={perf.MemoryPercent:0}%\n" +
                         temps;
            pdh.Dispose();
            string bt = devices.Count == 0
                ? "bt=无设备"
                : "bt=" + string.Join(", ", devices.Select(d => $"{d.Name}[{d.Source}]:{d.PercentText}"));
            string report =
                $"down={SpeedMeter.FormatBps(meter.DownBps)} up={SpeedMeter.FormatBps(meter.UpBps)}\n" +
                $"adapter={meter.ActiveName} ({meter.ActiveType}) vpn={meter.VpnUp} {meter.VpnName}\n" +
                $"wlan.connected={wlan.Connected} ssid={wlan.Ssid} signal={wlan.Signal}% rx={wlan.RxRateKbps}kbps tx={wlan.TxRateKbps}kbps\n" +
                $"vpn.active={vpn.Active} kind={vpn.Kind} detail={vpn.Detail} idleApp={idle}\n" +
                $"clash.ok={clash.Ok} mode={clash.Mode} tun={clash.Tun} node={clash.Node} delay={clash.Delay}\n" +
                sys + "\n" +
                bt + "\n";
            System.IO.File.WriteAllText(args[1], report);
            return;
        }

        using var mutex = new Mutex(true, "TrayLens.SingleInstance.v1", out bool createdNew);
        if (!createdNew) return;

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            AppHost.Log("unhandled: " + e.Exception);
            e.Handled = true;
        };
        AppHost.Log("=== app start ===");
        var host = new AppHost();
        app.Run();
        host.Dispose();
    }
}
