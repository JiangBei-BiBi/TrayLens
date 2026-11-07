using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrayLens;

internal readonly record struct ProcInfo(string Name, double Cpu, double MemMb);

internal readonly record struct DriveStat(string Name, double FreeGb, double TotalGb);

/// <summary>一帧性能快照，避免把十几个参数一路传下去。</summary>
internal readonly record struct PerfSnapshot(
    double CpuPercent,
    double MemPercent,
    double MemUsedMb,
    double MemTotalMb,
    double CommitUsedMb,
    double CommitTotalMb,
    int Threads,
    double BaseMhz,
    double DiskPercent,
    double DiskReadBps,
    double DiskWriteBps,
    double GpuPercent,
    double VramMb,
    double VramTotalMb,
    string GpuName,
    double? CpuTempC,
    double? GpuTempC,
    IReadOnlyList<ProcInfo> TopProcs,
    IReadOnlyList<DriveStat> Drives)
{
    public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    public (double freeGb, double totalGb) SystemDisk()
    {
        try
        {
            var drive = new DriveInfo(SystemDrive);
            return (drive.AvailableFreeSpace / 1024.0 / 1024 / 1024,
                    drive.TotalSize / 1024.0 / 1024 / 1024);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>所有就绪的本地分区，按盘符排序。</summary>
    public static IReadOnlyList<DriveStat> ReadDrives()
    {
        var list = new List<DriveStat>();

        try
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    if (d.TotalSize <= 0) continue;

                    list.Add(new DriveStat(
                        d.Name.TrimEnd('\\'),
                        d.AvailableFreeSpace / 1024.0 / 1024 / 1024,
                        d.TotalSize / 1024.0 / 1024 / 1024));
                }
                catch
                {
                    // 单个分区读不到就跳过
                }
            }
        }
        catch
        {
            // 忽略
        }

        return list.OrderBy(d => d.Name).ToList();
    }
}
