using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;

namespace TrayLens;

internal sealed class SpeedMeter
{
    private long _lastRx;
    private long _lastTx;
    private long _lastTicks;
    private bool _primed;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public double DownBps { get; private set; }
    public double UpBps { get; private set; }
    public string ActiveName { get; private set; } = "-";
    public string ActiveType { get; private set; } = "";
    public bool VpnUp { get; private set; }
    public string VpnName { get; private set; } = "";

    public void Sample()
    {
        long rx = 0, tx = 0;
        string active = "-";
        string activeType = "";
        long activeRx = -1;
        bool vpn = false;
        string vpnName = "";

        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.OperationalStatus != OperationalStatus.Up) continue;

            bool isVpn = IsVpn(ni);
            try
            {
                IPv4InterfaceStatistics st = ni.GetIPv4Statistics();
                if (isVpn)
                {
                    vpn = true;
                    vpnName = ni.Name;
                    continue; // 不重复计入总量
                }

                rx += st.BytesReceived;
                tx += st.BytesSent;

                if (st.BytesReceived > activeRx)
                {
                    activeRx = st.BytesReceived;
                    active = ni.Name;
                    activeType = DescribeType(ni.NetworkInterfaceType);
                }
            }
            catch
            {
                // 某些虚拟网卡取统计会抛异常，忽略
            }
        }

        long now = _clock.ElapsedTicks;
        if (_primed)
        {
            double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
            if (dt > 0.05)
            {
                DownBps = Math.Max(0, (rx - _lastRx) / dt);
                UpBps = Math.Max(0, (tx - _lastTx) / dt);
            }
        }

        _lastRx = rx;
        _lastTx = tx;
        _lastTicks = now;
        _primed = true;

        ActiveName = active;
        ActiveType = activeType;
        VpnUp = vpn;
        VpnName = vpnName;
    }

    private static bool IsVpn(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;
        string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
        string[] keys = { "vpn", "wireguard", "openvpn", "tap-windows", "tap adapter", "wintun", "tailscale", "zerotier", "clash", "v2ray", "sing-box", "shadow" };
        foreach (string k in keys)
        {
            if (desc.Contains(k)) return true;
        }
        return false;
    }

    private static string DescribeType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "以太网",
        NetworkInterfaceType.Ppp => "拨号",
        _ => t.ToString()
    };

    public static string FormatBps(double bps)
    {
        if (bps < 1024) return "0";
        if (bps < 1024 * 1024) return (bps / 1024).ToString("0", CultureInfo.InvariantCulture) + "K";
        if (bps < 1024.0 * 1024 * 1024) return (bps / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        return (bps / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + "G";
    }
}
