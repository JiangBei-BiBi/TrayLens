using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TrayLens;

/// <summary>按 CPU 占用排序的进程 Top N。用两次 TotalProcessorTime 的差值算占用。</summary>
internal sealed class ProcessMonitor
{
    private readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastAt;
    private int _logicalCpus = Math.Max(1, Environment.ProcessorCount);

    public IReadOnlyList<ProcInfo> Top { get; private set; } = Array.Empty<ProcInfo>();

    public void Sample(int topN = 3)
    {
        TimeSpan now = _clock.Elapsed;
        double elapsedSeconds = (now - _lastAt).TotalSeconds;
        if (elapsedSeconds <= 0.05)
        {
            _lastAt = now;
            return;
        }

        var result = new List<ProcInfo>();
        var seen = new HashSet<int>();

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                TimeSpan cpu = p.TotalProcessorTime;
                double memMb = p.WorkingSet64 / 1024.0 / 1024.0;

                double percent = 0;
                if (_lastCpu.TryGetValue(p.Id, out TimeSpan previous))
                {
                    percent = (cpu - previous).TotalSeconds / elapsedSeconds / _logicalCpus * 100.0;
                    percent = Math.Clamp(percent, 0, 100);
                }

                _lastCpu[p.Id] = cpu;
                seen.Add(p.Id);

                string name = p.ProcessName;
                if (name.Length == 0) continue;

                result.Add(new ProcInfo(name, percent, memMb));
            }
            catch
            {
                // 系统进程没权限读，跳过
            }
            finally
            {
                p.Dispose();
            }
        }

        // 清掉已经退出的进程
        foreach (int id in _lastCpu.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _lastCpu.Remove(id);
        }

        _lastAt = now;
        Top = result
            .OrderByDescending(p => p.Cpu)
            .ThenByDescending(p => p.MemMb)
            .Take(topN)
            .ToList();
    }
}
