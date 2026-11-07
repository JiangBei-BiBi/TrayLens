using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace TrayLens;

/// <summary>一个无线设备：能读到电量就给百分比，读不到就是 null（界面显示「—」）。</summary>
internal readonly record struct WirelessDevice(string Name, int? Percent, string Source, bool Connected)
{
    public string PercentText => Percent is null ? "—" : Percent.Value + "%";
}

/// <summary>
/// 无线设备扫描。三种来源：
/// 1) BLE 设备：读 Battery Service(0x180F → 0x2A19)，这是唯一能拿到准确电量的；
/// 2) XInput 手柄：读 XInputGetBatteryInformation，只能得到空/低/中/满四档；
/// 3) 经典蓝牙（耳机等）：系统根本不暴露电量，只能列出「已配对」。
/// </summary>
internal static class WirelessScanner
{
    private static readonly Guid BatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = new("00002a19-0000-1000-8000-00805f9b34fb");
    private static readonly TimeSpan OpTimeout = TimeSpan.FromSeconds(4);

    public static async Task<List<WirelessDevice>> ScanAsync(CancellationToken ct = default)
    {
        var found = new List<WirelessDevice>();

        found.AddRange(await ScanBleAsync(ct).ConfigureAwait(false));
        found.AddRange(await ScanClassicAsync(ct).ConfigureAwait(false));
        found.AddRange(ScanXInput());

        return found;
    }

    private static async Task<List<WirelessDevice>> ScanBleAsync(CancellationToken ct)
    {
        var found = new List<WirelessDevice>();
        DeviceInformationCollection infos;
        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            infos = await DeviceInformation.FindAllAsync(selector).AsTask(ct).WaitAsync(OpTimeout);
        }
        catch
        {
            return found;
        }

        foreach (DeviceInformation di in infos)
        {
            if (ct.IsCancellationRequested) break;
            BluetoothLEDevice? device = null;
            try
            {
                device = await BluetoothLEDevice.FromIdAsync(di.Id).AsTask(ct).WaitAsync(OpTimeout);
                if (device is null) continue;

                string name = string.IsNullOrWhiteSpace(device.Name) ? di.Name : device.Name;
                int? percent = null;

                GattDeviceServicesResult services = await device
                    .GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached)
                    .AsTask(ct).WaitAsync(OpTimeout);

                if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
                {
                    GattDeviceService service = services.Services[0];
                    try
                    {
                        GattCharacteristicsResult chars = await service
                            .GetCharacteristicsForUuidAsync(BatteryLevelUuid, BluetoothCacheMode.Uncached)
                            .AsTask(ct).WaitAsync(OpTimeout);
                        if (chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
                        {
                            GattReadResult read = await chars.Characteristics[0]
                                .ReadValueAsync(BluetoothCacheMode.Uncached)
                                .AsTask(ct).WaitAsync(OpTimeout);
                            if (read.Status == GattCommunicationStatus.Success)
                            {
                                byte[] data = read.Value.ToArray();
                                if (data.Length > 0) percent = data[0] > 100 ? 100 : data[0];
                            }
                        }
                    }
                    finally
                    {
                        service.Dispose();
                    }
                }

                found.Add(new WirelessDevice(name, percent, "BLE", true));
            }
            catch
            {
                // 单个设备失败不影响其它
            }
            finally
            {
                device?.Dispose();
            }
        }

        return found;
    }

    private static List<WirelessDevice> ScanXInput()
    {
        var list = new List<WirelessDevice>();

        for (uint index = 0; index < 4; index++)
        {
            try
            {
                int rc = XInputGetState(index, out _);
                if (rc != 0) continue;

                int brc = XInputGetBatteryInformation(index, 0, out XINPUT_BATTERY_INFORMATION info);
                if (brc != 0) continue;

                int? percent = info.BatteryLevel switch
                {
                    0 => 5,
                    1 => 25,
                    2 => 60,
                    3 => 100,
                    _ => null
                };

                bool wired = info.BatteryType == 1;
                // 2.4G 接收器在系统里也常被当成「有线」，所以不加后缀，免得误导
                string name = $"手柄 {index + 1}";
                list.Add(new WirelessDevice(name, wired ? null : percent, "XInput", true));
            }
            catch
            {
                // xinput 未安装或没有手柄
            }
        }

        return list;
    }

    /// <summary>经典蓝牙只取「已连接」的——用 WinRT 的连接状态选择器，断开的自然不出现。</summary>
    private static async Task<List<WirelessDevice>> ScanClassicAsync(CancellationToken ct)
    {
        var list = new List<WirelessDevice>();

        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            DeviceInformationCollection infos = await DeviceInformation
                .FindAllAsync(selector).AsTask(ct).WaitAsync(OpTimeout);

            foreach (DeviceInformation di in infos)
            {
                if (ct.IsCancellationRequested) break;
                string name = di.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new WirelessDevice(name, null, "经典蓝牙", true));
            }
        }
        catch
        {
            // 没开蓝牙或 API 不可用
        }

        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(uint index, out XINPUT_STATE state);

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetBatteryInformation(uint index, byte deviceType, out XINPUT_BATTERY_INFORMATION info);
}
