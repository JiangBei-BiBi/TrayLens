using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace TrayLens;

internal enum VpnKind
{
    None,
    Tun,
    SystemProxy
}

internal readonly record struct VpnState(bool Active, VpnKind Kind, string Detail, string Node, int Delay)
{
    public static VpnState Off => new(false, VpnKind.None, "", "", -1);
}

/// <summary>
/// 三重判定代理/VPN 状态，因为 Clash 这类工具有两种完全不同的工作方式：
/// 1) TUN 模式：多出一块虚拟网卡，流量真的从那里走；
/// 2) 系统代理模式：没有虚拟网卡，只是往注册表写了 ProxyServer，浏览器/应用自己连本地端口。
/// 只查网卡的话，第 2 种永远漏。
/// </summary>
internal static class VpnDetector
{
    private static readonly string[] TunHints =
    {
        "wintun", "tap-windows", "tap adapter", "wireguard", "openvpn", "tun", "clash", "mihomo", "sing-box", "v2ray"
    };

    private static readonly string[] ProxyAppNames =
    {
        "clash-verge", "verge-mihomo", "clash", "mihomo", "v2ray", "xray", "sing-box", "nekoray"
    };

    public static VpnState Detect()
    {
        VpnState tun = DetectTun();
        if (tun.Active) return tun;

        VpnState proxy = DetectSystemProxy();
        if (proxy.Active) return proxy;

        return VpnState.Off;
    }

    private static VpnState DetectTun()
    {
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            string desc = ni.Description.ToLowerInvariant();
            string name = ni.Name.ToLowerInvariant();
            bool hit = TunHints.Any(h => desc.Contains(h) || name.Contains(h));
            if (!hit) continue;

            // WLAN/以太网的描述里不会出现这些词，命中就是虚拟网卡
            return new VpnState(true, VpnKind.Tun, ni.Name, "", -1);
        }

        return VpnState.Off;
    }

    private static VpnState DetectSystemProxy()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key is null) return VpnState.Off;

            object? enable = key.GetValue("ProxyEnable");
            if (enable is not int on || on != 1) return VpnState.Off;

            string? server = key.GetValue("ProxyServer") as string;
            if (string.IsNullOrWhiteSpace(server)) return VpnState.Off;

            string host = server;
            string port = "";
            int colon = server.LastIndexOf(':');
            if (colon > 0 && !server.Contains("://"))
            {
                host = server[..colon];
                port = server[(colon + 1)..];
            }
            else if (server.Contains("://"))
            {
                var parts = server.Split(new[] { "://" }, StringSplitOptions.None);
                string rest = parts.Length > 1 ? parts[1].Split(';')[0] : server;
                int c = rest.LastIndexOf(':');
                if (c > 0)
                {
                    host = rest[..c];
                    port = rest[(c + 1)..];
                }
                else
                {
                    host = rest;
                }
            }

            bool local = host is "127.0.0.1" or "localhost" || host.StartsWith("127.") || host.StartsWith("192.168.");
            string label = port.Length > 0 ? $"{host}:{port}" : host;
            return new VpnState(true, VpnKind.SystemProxy, label, "", -1);
        }
        catch
        {
            return VpnState.Off;
        }
    }

    /// <summary>代理程序在跑但两种模式都没开时，用来给个「有工具但没接管」的提示。</summary>
    public static string? DetectIdleApp()
    {
        try
        {
            foreach (Process p in Process.GetProcesses())
            {
                string n = p.ProcessName.ToLowerInvariant();
                if (!ProxyAppNames.Any(a => n.Contains(a))) continue;
                try { p.Dispose(); } catch { }
                return n;
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }
}
