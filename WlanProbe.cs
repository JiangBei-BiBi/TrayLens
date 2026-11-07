using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayLens;

/// <summary>
/// 通过 WLAN API 读取当前 Wi-Fi 连接（SSID / 信号强度）。
/// 不用 netsh：中文系统的输出要按 OEM 代码页解析，脆得很。
/// </summary>
internal static class WlanProbe
{
    public readonly record struct WlanState(bool Connected, string Ssid, int Signal, int RxRateKbps, int TxRateKbps)
    {
        public static WlanState Disconnected => new(false, "", 0, 0, 0);
    }

    public static WlanState Query()
    {
        IntPtr handle = IntPtr.Zero;
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0) return WlanState.Disconnected;

        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out IntPtr listPtr) != 0) return WlanState.Disconnected;
            try
            {
                int count = Marshal.ReadInt32(listPtr);
                int entrySize = Marshal.SizeOf<WlanInterfaceInfo>();
                IntPtr first = IntPtr.Add(listPtr, 8);

                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<WlanInterfaceInfo>(IntPtr.Add(first, i * entrySize));
                    if (info.State != 1) continue; // wlan_interface_state_connected

                    if (WlanQueryInterface(handle, ref info.InterfaceGuid, 7, IntPtr.Zero,
                            out _, out IntPtr data, out _) != 0) continue;
                    try
                    {
                        var attrs = Marshal.PtrToStructure<WlanConnectionAttributes>(data);
                        var assoc = attrs.Association;
                        int len = (int)Math.Min(assoc.Ssid.Length, 32u);
                        string ssid = Encoding.UTF8.GetString(assoc.Ssid.Bytes, 0, len).TrimEnd('\0');
                        return new WlanState(true, ssid, (int)assoc.SignalQuality, (int)assoc.RxRate, (int)assoc.TxRate);
                    }
                    finally
                    {
                        WlanFreeMemory(data);
                    }
                }
            }
            finally
            {
                WlanFreeMemory(listPtr);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }

        return WlanState.Disconnected;
    }

    // ---- interop ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public char[] Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public uint BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;
        public uint PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint RxRate;
        public uint TxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        public int SecurityEnabled;
        public int OneXEnabled;
        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int State;
        public int ConnectionMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public char[] ProfileName;
        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [DllImport("wlanapi.dll")]
    private static extern int WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opcode, IntPtr reserved,
        out int dataSize, out IntPtr data, out int opcodeValue);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
