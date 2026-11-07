using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TrayLens;

/// <summary>
/// 任务管理器式的磁盘与 GPU 指标。
/// GPU 利用率在 Windows 里是「每个进程每个引擎一个实例」的计数器，
/// 所以要枚举 GPU Engine 类别下的所有实例再累加（PDH 的野指针数组这条路在本机返回无实例，走不通）。
/// 实例对象缓存 10 秒重建一次，避免每秒枚举几百个计数器。
/// </summary>
internal sealed class PdhMonitor : IDisposable
{
    private readonly object _gate = new();
    private List<PerformanceCounter> _gpuCounters = new();
    private List<PerformanceCounter> _vramCounters = new();
    private PerformanceCounter? _diskPct;
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;

    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _primed;

    public double GpuPercent { get; private set; }
    public double VramMb { get; private set; }
    public double DiskPercent { get; private set; }
    public double DiskReadBps { get; private set; }
    public double DiskWriteBps { get; private set; }
    public string Diag { get; private set; } = "";

    public PdhMonitor()
    {
        try
        {
            _diskPct = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
            _diskPct.NextValue();
            _diskRead.NextValue();
            _diskWrite.NextValue();
            Diag = "disk=ok";
        }
        catch (Exception ex)
        {
            Diag = "disk=fail:" + ex.GetType().Name;
        }

        RefreshGpuCounters();
    }

    private void RefreshGpuCounters()
    {
        _lastRefresh = DateTime.Now;
        try
        {
            var counters = new List<PerformanceCounter>();
            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (string instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3d", StringComparison.OrdinalIgnoreCase) &&
                    !instance.Contains("engtype_compute", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    counter.NextValue();
                    counters.Add(counter);
                }
                catch
                {
                    // 实例可能刚消失
                }
            }

            lock (_gate)
            {
                foreach (PerformanceCounter old in _gpuCounters) old.Dispose();
                _gpuCounters = counters;
            }

            try
            {
                var memCategory = new PerformanceCounterCategory("GPU Adapter Memory");
                var memCounters = new List<PerformanceCounter>();
                foreach (string inst in memCategory.GetInstanceNames())
                {
                    try
                    {
                        var c = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, readOnly: true);
                        c.NextValue();
                        memCounters.Add(c);
                    }
                    catch
                    {
                        // 忽略单个实例
                    }
                }

                lock (_gate)
                {
                    foreach (PerformanceCounter old in _vramCounters) old.Dispose();
                    _vramCounters = memCounters;
                }
            }
            catch
            {
                // 忽略
            }

            Diag = $"gpu={_gpuCounters.Count}";
        }
        catch (Exception ex)
        {
            Diag = "gpu=fail:" + ex.GetType().Name;
        }
    }

    public void Sample()
    {
        if (!_primed)
        {
            _primed = true;
            return;
        }

        if ((DateTime.Now - _lastRefresh).TotalSeconds > 10) RefreshGpuCounters();

        try
        {
            double total = 0;
            List<PerformanceCounter> snapshot;
            lock (_gate) snapshot = _gpuCounters.ToList();
            foreach (PerformanceCounter c in snapshot)
            {
                try
                {
                    double v = c.NextValue();
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) total += v;
                }
                catch
                {
                    // 计数器失效就跳过
                }
            }

            GpuPercent = Math.Clamp(total, 0, 100);
        }
        catch
        {
            GpuPercent = 0;
        }

        try
        {
            double mem = 0;
            List<PerformanceCounter> memSnapshot;
            lock (_gate) memSnapshot = _vramCounters.ToList();
            foreach (PerformanceCounter c in memSnapshot)
            {
                try { mem += Math.Max(0, c.NextValue()); } catch { }
            }
            VramMb = mem / 1024.0 / 1024.0;
        }
        catch
        {
            VramMb = 0;
        }

        try
        {
            DiskPercent = Math.Clamp(_diskPct?.NextValue() ?? 0, 0, 100);
            DiskReadBps = Math.Max(0, _diskRead?.NextValue() ?? 0);
            DiskWriteBps = Math.Max(0, _diskWrite?.NextValue() ?? 0);
        }
        catch
        {
            DiskPercent = 0;
            DiskReadBps = 0;
            DiskWriteBps = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (PerformanceCounter c in _gpuCounters) c.Dispose();
            _gpuCounters.Clear();
            foreach (PerformanceCounter c in _vramCounters) c.Dispose();
            _vramCounters.Clear();
        }

        _diskPct?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
    }
}
