using System;
using System.Runtime.InteropServices;

namespace TrayLens;

/// <summary>CPU 占用（GetSystemTimes 差值）与内存占用（GlobalMemoryStatusEx），都是零依赖的系统调用。</summary>
internal sealed class PerfMonitor
{
    private ulong _idle;
    private ulong _kernel;
    private ulong _user;
    private bool _primed;

    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public double TotalMb { get; private set; }
    public double UsedMb { get; private set; }
    public double CommitUsedMb { get; private set; }
    public double CommitTotalMb { get; private set; }
    public int Threads { get; } = Environment.ProcessorCount;
    public double BaseMhz { get; } = SystemDetails.BaseCpuMhz();

    public void Sample()
    {
        SampleCpu();
        SampleMemory();
    }

    private void SampleCpu()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user)) return;

        ulong i = ToUInt64(idle);
        ulong k = ToUInt64(kernel);
        ulong u = ToUInt64(user);

        if (_primed)
        {
            ulong idleDelta = i - _idle;
            ulong totalDelta = (k - _kernel) + (u - _user);
            if (totalDelta > 0)
            {
                double busy = (totalDelta - idleDelta) / (double)totalDelta;
                CpuPercent = Math.Clamp(busy * 100.0, 0, 100);
            }
        }

        _idle = i;
        _kernel = k;
        _user = u;
        _primed = true;
    }

    private void SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return;

        MemoryPercent = status.dwMemoryLoad;
        TotalMb = status.ullTotalPhys / 1024.0 / 1024.0;
        UsedMb = (status.ullTotalPhys - status.ullAvailPhys) / 1024.0 / 1024.0;
        CommitTotalMb = status.ullTotalPageFile / 1024.0 / 1024.0;
        CommitUsedMb = (status.ullTotalPageFile - status.ullAvailPageFile) / 1024.0 / 1024.0;
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
