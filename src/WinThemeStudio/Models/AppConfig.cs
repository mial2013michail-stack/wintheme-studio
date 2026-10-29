namespace WinThemeStudio.Models;

public sealed class AppConfig
{
    public string LastAppliedProfile { get; set; } = "windows";
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool FirstRunDone { get; set; }
    public bool TrayHintShown { get; set; }

    public string? OriginalWallpaperPath { get; set; }
    public string? OriginalWallpaperRegistryPath { get; set; }
    public string OriginalWallpaperStyle { get; set; } = "10";
    public string OriginalWallpaperTile { get; set; } = "0";
    public bool OriginalWallpaperCaptured { get; set; }
    public string LastWallpaperKey { get; set; } = "";
    public bool LastWallpaperIsCustom { get; set; }
    public string WallpaperFit { get; set; } = "fill";

    // Icon studio. "auto" follows the active profile, "studio" uses the
    // tint/radius/monochrome controls below, any other value is a built-in
    // style key.
    public string IconStyle { get; set; } = "auto";
    public string IconTint { get; set; } = "";
    public int IconRadius { get; set; } = -1;
    public bool IconMonochrome { get; set; }
    public Dictionary<string, string> CustomIconPaths { get; set; } = new();
    public bool UseDriveIcons { get; set; } = true;
    public bool UseFullTrashIcon { get; set; } = true;

    // Selective profile application.
    public bool ProfileApplyTheme { get; set; } = true;
    public bool ProfileApplyWallpaper { get; set; } = true;
    public bool ProfileApplyTaskbar { get; set; } = true;
    public bool ProfileApplyExplorer { get; set; } = true;
    public bool ProfileApplyIcons { get; set; } = true;

    // Daily profile scheduler.
    public bool SchedulerEnabled { get; set; }
    public string SchedulerProfile { get; set; } = "aurora";
    public string SchedulerTime { get; set; } = "09:00";
    public string SchedulerLastRun { get; set; } = "";

    // Explorer extras.
    public bool ShowDesktopIcons { get; set; } = true;
    public bool ClassicContextMenu { get; set; }

    public bool DockEnabled { get; set; }
    public string DockPosition { get; set; } = "bottom";
    public double DockSize { get; set; } = 48;
    public bool DockAutoHide { get; set; }
    public bool DockBlur { get; set; } = true;
    public bool DockMagnify { get; set; } = true;
    public bool DockHideFullscreen { get; set; } = true;
    public List<DockItemConfig> DockItems { get; set; } = new();
}

public sealed class DockItemConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "builtin";
    public string Target { get; set; } = "";
    public string? Arguments { get; set; }
}
