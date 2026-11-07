using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TrayLens;

internal sealed class ModuleSetting
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

internal sealed class AppSettings
{
    public List<ModuleSetting> Modules { get; set; } = new();
    public bool Topmost { get; set; } = false;
    public bool ShowBatteryIcon { get; set; } = true;

    /// <summary>开机自动启动（写 HKCU\...\Run）。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>全局热键修饰键：Ctrl+Alt / Ctrl+Shift / Alt+Shift / 无</summary>
    public string HotkeyModifiers { get; set; } = "Ctrl+Alt";

    /// <summary>全局热键主键（A-Z / 0-9）。</summary>
    public string HotkeyKey { get; set; } = "N";

    /// <summary>面板背景样式：acrylic（亚克力）/ solid（纯色）/ image（自定义图片）</summary>
    public string BackdropStyle { get; set; } = "acrylic";

    /// <summary>面板不透明度 0-100。</summary>
    public int PanelOpacity { get; set; } = 73;

    /// <summary>自定义背景图片路径（BackdropStyle=image 时生效）。</summary>
    public string BackgroundImage { get; set; } = "";

    // ---- 预警 ----
    public bool CpuAlertEnabled { get; set; } = true;
    public int CpuAlertThreshold { get; set; } = 90;
    public int CpuAlertSeconds { get; set; } = 30;
    public string CpuAlertText { get; set; } = "CPU 占用过高";

    public bool MemAlertEnabled { get; set; } = true;
    public int MemAlertThreshold { get; set; } = 90;
    public int MemAlertSeconds { get; set; } = 30;
    public string MemAlertText { get; set; } = "内存占用过高";

    public bool GpuAlertEnabled { get; set; } = true;
    public int GpuAlertThreshold { get; set; } = 90;
    public int GpuAlertSeconds { get; set; } = 30;
    public double GpuTempAlertThreshold { get; set; } = 85;
    public string GpuAlertText { get; set; } = "GPU 占用过高";
    public string GpuTempAlertText { get; set; } = "GPU 温度偏高";

    public bool BattAlertEnabled { get; set; } = true;
    public int BattAlertThreshold { get; set; } = 20;
    public string BattAlertText { get; set; } = "无线设备电量偏低";

    /// <summary>固定贴任务栏右侧（不可拖动）。</summary>
    public bool PinnedToTaskbar { get; set; } = true;

    /// <summary>是否显示代理线路（Clash 节点 + 延迟）。默认关闭，关掉时也不再去查管道。</summary>
    public bool ShowProxyLine { get; set; }

    /// <summary>悬停时是否自动展开详情（关掉就只靠点击）。</summary>
    public bool HoverExpand { get; set; } = true;

    /// <summary>悬停多久才展开，毫秒。</summary>
    public int HoverShowDelayMs { get; set; } = 200;

    /// <summary>鼠标移开后多久才收起，毫秒。</summary>
    public int HoverHideDelayMs { get; set; } = 600;

    public static IReadOnlyList<(string Id, string Title, string Glyph)> Catalog { get; } = new[]
    {
        ("network", "网络速率", "\uE704"),
        ("wlan", "无线网络", "\uE701"),
        ("perf", "系统性能", "\uE9F5"),
        ("devices", "无线设备", "\uE85E"),
    };

    public static string PathFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayLens", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathFile));
                if (loaded is not null)
                {
                    loaded.Normalize();
                    loaded.MigrateLegacyTexts();
                    return loaded;
                }
            }
        }
        catch
        {
            // 配置坏了就回默认
        }

        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public void Normalize()
    {
        var known = Catalog.Select(c => c.Id).ToHashSet();

        // 去掉不认识的模块、补上缺失的模块（按目录顺序），保证升级后新模块会出现
        Modules = Modules.Where(m => known.Contains(m.Id)).ToList();
        foreach (var (id, _, _) in Catalog)
        {
            if (Modules.All(m => m.Id != id)) Modules.Add(new ModuleSetting { Id = id, Enabled = true });
        }
    }

    public bool IsEnabled(string id) => Modules.FirstOrDefault(m => m.Id == id)?.Enabled ?? true;

    /// <summary>旧版文案里带 {value} 这类占位符，新版不再支持，统一换成简单句子。</summary>
    private void MigrateLegacyTexts()
    {
        if (CpuAlertText.Contains('{')) CpuAlertText = "CPU 占用过高";
        if (MemAlertText.Contains('{')) MemAlertText = "内存占用过高";
        if (BattAlertText.Contains('{')) BattAlertText = "无线设备电量偏低";
        if (GpuAlertText.Contains('{')) GpuAlertText = "GPU 占用过高";
        if (GpuTempAlertText.Contains('{')) GpuTempAlertText = "GPU 温度偏高";
    }

    /// <summary>把第 index 个模块上移/下移一格，越界返回 false。</summary>
    public bool MoveModule(int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= Modules.Count || target < 0 || target >= Modules.Count) return false;

        (Modules[index], Modules[target]) = (Modules[target], Modules[index]);
        return true;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
