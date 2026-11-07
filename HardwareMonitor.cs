using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace TrayLens;

/// <summary>
/// CPU / GPU 温度，走 LibreHardwareMonitor。
/// 注意：读 CPU 温度需要它的内核驱动，非管理员时通常只拿得到空值，此时返回 null。
/// 只看普通温度（Core/Package 或 GPU Core），跳过 Hot Spot / Junction / 显存温度。
/// </summary>
internal sealed class HardwareMonitor : IDisposable
{
    private Computer? _computer;

    public double? CpuTemp { get; private set; }
    public double? GpuTemp { get; private set; }

    public HardwareMonitor()
    {
        try
        {
            _computer = new Computer
            {
                // CPU 温度需要管理员加载驱动，普通权限下读不到，干脆不启用，省掉这部分开销
                IsCpuEnabled = false,
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false
            };
            _computer.Open();
        }
        catch
        {
            _computer = null;
        }
    }

    public void Sample()
    {
        if (_computer is null) return;

        double? cpu = null;
        double? gpu = null;

        try
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                hardware.Update();

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    cpu ??= PickCpu(hardware);
                }
                else if (hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
                {
                    gpu ??= PickGpu(hardware);
                }
            }
        }
        catch
        {
            // 驱动没加载等情况，保持 null
        }

        CpuTemp = cpu;
        GpuTemp = gpu;
    }

    private static double? PickCpu(IHardware hardware)
    {
        // 优先 Package / Tctl，其次任意 Core 平均
        ISensor? package = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
            .FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                 || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));

        if (package?.Value is float pv) return Math.Round((double)pv, 0);

        List<double> cores = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
            .Where(s => !IsNoisy(s.Name))
            .Select(s => (double)(s.Value ?? 0f))
            .ToList();

        return cores.Count > 0 ? Math.Round(cores.Average(), 0) : null;
    }

    private static double? PickGpu(IHardware hardware)
    {
        ISensor? core = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
            .FirstOrDefault(s => s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase)
                                 || s.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase));

        if (core?.Value is float gv) return Math.Round((double)gv, 0);

        List<double> candidates = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0)
            .Where(s => !IsNoisy(s.Name))
            .Select(s => (double)(s.Value ?? 0f))
            .ToList();

        return candidates.Count > 0 ? Math.Round(candidates.Average(), 0) : null;
    }

    private static bool IsNoisy(string name) =>
        name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Junction", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Memory", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VR ", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VRM", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch
        {
            // 忽略
        }

        _computer = null;
    }
}
