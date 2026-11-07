using System;
using System.IO;
using System.Windows;

namespace TrayLens;

/// <summary>离线渲染飞窗外观（浅色 / 深色各一份），用于自查排版。</summary>
internal static class FlyoutShot
{
    public static void Render(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new FlyoutWindow();
        window.ApplySettings(AppSettings.Load());

        var wlan = new WlanProbe.WlanState(true, "TP-LINK_09CC", 92, 360000, 144400);
        var clash = ClashClient.Query();
        var vpn = clash.Ok
            ? new VpnState(true, clash.Tun ? VpnKind.Tun : VpnKind.SystemProxy, "", clash.Node, clash.Delay)
            : new VpnState(true, VpnKind.SystemProxy, "127.0.0.1:7897", "", -1);
        var devices = new[]
        {
            new WirelessDevice("VGN V98pro V2 BT1", 87, "BLE", true),
            new WirelessDevice("WH-1000XM4", 34, "BLE", true),
            new WirelessDevice("手柄 1", null, "XInput", true),
        };

        for (int i = 0; i < 60; i++)
        {
            double t = i / 60.0;
            double down = 300_000 + 260_000 * Math.Sin(t * 6.2) * Math.Sin(t * 2.1) + 120_000 * Math.Cos(t * 13);
            double up = 60_000 + 55_000 * Math.Cos(t * 4.4);
            window.UpdateState(Math.Max(20_000, down), Math.Max(8_000, up), wlan, vpn, devices,
                "IP 192.168.1.5 · 网关 192.168.1.1 · DNS 192.168.1.1");
        }

        var perf = new PerfMonitor();
        perf.Sample();
        System.Threading.Thread.Sleep(120);
        perf.Sample();
        for (int i = 0; i < 60; i++)
        {
            double t = i / 60.0;
            window.UpdatePerf(new PerfSnapshot(
                18 + 30 * Math.Abs(Math.Sin(t * 5.1)),
                46 + 4 * Math.Sin(t * 3),
                15.2 * 1024, 32 * 1024,
                18.4 * 1024, 36 * 1024, perf.Threads, perf.BaseMhz,
                2 + 26 * Math.Abs(Math.Sin(t * 9.3)),
                1.4 * 1024 * 1024, 0.6 * 1024 * 1024,
                25 + 40 * Math.Abs(Math.Sin(t * 7.7)),
                2.1 * 1024, SystemDetails.VramTotalMb, SystemDetails.GpuName,
                52 + 8 * Math.Sin(t * 4),
                58 + 10 * Math.Sin(t * 3.4),
                new[]
                {
                    new ProcInfo("chrome", 12.3, 1200),
                    new ProcInfo("TrayLens", 3.1, 90),
                    new ProcInfo("explorer", 1.2, 140),
                },
                PerfSnapshot.ReadDrives()));
        }

        foreach (bool light in new[] { true, false })
        {
            window.ApplyThemeForPreview(light);
            string path = Path.Combine(outDir, light ? "flyout-light.png" : "flyout-dark.png");
            window.RenderToPng(path);
            Console.WriteLine(path);

            if (light)
            {
                window.PreviewSelectTile(0);
                string expanded = Path.Combine(outDir, "flyout-expanded.png");
                window.RenderToPng(expanded);
                window.PreviewSelectTile(0);
            }
        }

        // 注意：不要 Close()，否则会触发 Closed → SavePosition，把预览窗口的假坐标写进真实的位置文件
        app.Shutdown();
    }
}
