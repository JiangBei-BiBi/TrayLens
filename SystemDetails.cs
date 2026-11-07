using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace TrayLens;

/// <summary>各项指标的补充信息：CPU 型号/频率、GPU 型号/显存总量、网络 IP/网关/DNS。</summary>
internal static class SystemDetails
{
    private static string? _gpuName;
    private static double _vramTotalMb = -1;
    private static bool _gpuLoaded;

    public static string GpuName
    {
        get { LoadGpu(); return _gpuName ?? "-"; }
    }

    public static double VramTotalMb
    {
        get { LoadGpu(); return _vramTotalMb > 0 ? _vramTotalMb : 0; }
    }

    /// <summary>CPU 基础频率（MHz），来自注册表，不动 WMI。</summary>
    public static double BaseCpuMhz()
    {
        try
        {
            object? v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", 0);
            return v is int i ? i : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void LoadGpu()
    {
        if (_gpuLoaded) return;
        _gpuLoaded = true;

        try
        {
            const string baseKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(baseKey);
            if (root is null) return;

            foreach (string name in root.GetSubKeyNames())
            {
                if (!name.All(char.IsDigit)) continue;

                using RegistryKey? sub = root.OpenSubKey(name);
                if (sub is null) continue;

                if (sub.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
                _gpuName = desc;

                // qwMemorySize 是个 8 字节小端整数，比 AdapterRAM 准（AdapterRAM 会截断到 4GB 以下）
                if (sub.GetValue("HardwareInformation.qwMemorySize") is byte[] raw && raw.Length == 8)
                {
                    ulong bytes = BitConverter.ToUInt64(raw, 0);
                    _vramTotalMb = bytes / 1024.0 / 1024.0;
                }

                if (_vramTotalMb <= 0 && sub.GetValue("HardwareInformation.MemorySize") is int mb)
                {
                    _vramTotalMb = mb;
                }

                break;
            }
        }
        catch
        {
            // 读不到就留空
        }
    }

    /// <summary>当前联网网卡的 IP / 网关 / DNS，一句话概括。</summary>
    public static string NetworkLine()
    {
        try
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .FirstOrDefault();

            if (nic is null) return "";

            IPInterfaceProperties props = nic.GetIPProperties();

            string ip = props.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .FirstOrDefault() ?? "-";

            string gateway = props.GatewayAddresses
                .Select(g => g.Address.ToString())
                .FirstOrDefault(g => g != "0.0.0.0") ?? "-";

            string dns = string.Join(" / ", props.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Take(2));

            if (dns.Length == 0) dns = "-";

            return $"IP {ip} · 网关 {gateway} · DNS {dns}";
        }
        catch
        {
            return "";
        }
    }
}
