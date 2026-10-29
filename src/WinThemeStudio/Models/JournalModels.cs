using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WinThemeStudio.Models;

public enum ChangeEntryType
{
    Registry,
    File,
    Key
}

public sealed class ChangeEntry
{
    public ChangeEntryType Type { get; set; }
    public string? KeyPath { get; set; }
    public string? ValueName { get; set; }
    public bool Existed { get; set; }
    public RegistryValueKind Kind { get; set; } = RegistryValueKind.String;
    public string? Value { get; set; }
    public string? FilePath { get; set; }
    public string? BackupPath { get; set; }
    public string? Label { get; set; }

    [JsonIgnore]
    public string TypeDisplay => Type switch
    {
        ChangeEntryType.Registry => "Реестр",
        ChangeEntryType.File => "Файл",
        ChangeEntryType.Key => "Раздел",
        _ => Type.ToString()
    };

    [JsonIgnore]
    public string TargetDisplay => Type switch
    {
        ChangeEntryType.Registry => (KeyPath ?? "") + (string.IsNullOrEmpty(ValueName) ? "" : "\\" + ValueName),
        ChangeEntryType.File => FilePath ?? "",
        ChangeEntryType.Key => KeyPath ?? "",
        _ => ""
    };
}

/// <summary>
/// Application-level settings that a transaction changed in addition to registry
/// values and files. Kept with the transaction so undo can restore the visible
/// profile/wallpaper/overlay state as well.
/// </summary>
public sealed class UndoConfigState
{
    public string LastAppliedProfile { get; set; } = "windows";
    public string LastWallpaperKey { get; set; } = "";
    public bool LastWallpaperIsCustom { get; set; }
    public string WallpaperFit { get; set; } = "fill";
    public string IconStyle { get; set; } = "auto";
    public string IconTint { get; set; } = "";
    public int IconRadius { get; set; } = -1;
    public bool IconMonochrome { get; set; }
    public bool UseDriveIcons { get; set; } = true;
    public bool UseFullTrashIcon { get; set; } = true;
    public bool ShowDesktopIcons { get; set; } = true;
    public bool ClassicContextMenu { get; set; }

    public bool DockEnabled { get; set; }
    public string DockPosition { get; set; } = "bottom";
    public double DockSize { get; set; } = 48;
    public bool DockAutoHide { get; set; }
    public bool DockBlur { get; set; } = true;
    public bool DockMagnify { get; set; } = true;
    public bool DockHideFullscreen { get; set; } = true;

    public static UndoConfigState Capture(AppConfig config) => new()
    {
        LastAppliedProfile = config.LastAppliedProfile,
        LastWallpaperKey = config.LastWallpaperKey,
        LastWallpaperIsCustom = config.LastWallpaperIsCustom,
        WallpaperFit = config.WallpaperFit,
        IconStyle = config.IconStyle,
        IconTint = config.IconTint,
        IconRadius = config.IconRadius,
        IconMonochrome = config.IconMonochrome,
        UseDriveIcons = config.UseDriveIcons,
        UseFullTrashIcon = config.UseFullTrashIcon,
        ShowDesktopIcons = config.ShowDesktopIcons,
        ClassicContextMenu = config.ClassicContextMenu,
        DockEnabled = config.DockEnabled,
        DockPosition = config.DockPosition,
        DockSize = config.DockSize,
        DockAutoHide = config.DockAutoHide,
        DockBlur = config.DockBlur,
        DockMagnify = config.DockMagnify,
        DockHideFullscreen = config.DockHideFullscreen
    };

    public void ApplyTo(AppConfig config)
    {
        config.LastAppliedProfile = LastAppliedProfile;
        config.LastWallpaperKey = LastWallpaperKey;
        config.LastWallpaperIsCustom = LastWallpaperIsCustom;
        config.WallpaperFit = WallpaperFit;
        config.IconStyle = IconStyle;
        config.IconTint = IconTint;
        config.IconRadius = IconRadius;
        config.IconMonochrome = IconMonochrome;
        config.UseDriveIcons = UseDriveIcons;
        config.UseFullTrashIcon = UseFullTrashIcon;
        config.ShowDesktopIcons = ShowDesktopIcons;
        config.ClassicContextMenu = ClassicContextMenu;
        config.DockEnabled = DockEnabled;
        config.DockPosition = DockPosition;
        config.DockSize = DockSize;
        config.DockAutoHide = DockAutoHide;
        config.DockBlur = DockBlur;
        config.DockMagnify = DockMagnify;
        config.DockHideFullscreen = DockHideFullscreen;
    }

    public UndoConfigState Clone() => (UndoConfigState)MemberwiseClone();
}

public sealed class JournalTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Completed { get; set; }
    public DateTime? UndoneUtc { get; set; }
    public bool Recovered { get; set; }
    public UndoConfigState? PreviousConfig { get; set; }
    public List<ChangeEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public bool IsActive => UndoneUtc is null;

    [JsonIgnore]
    public int EntryCount => Entries.Count;

    [JsonIgnore]
    public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy · HH:mm");

    [JsonIgnore]
    public string StatusDisplay => IsActive
        ? (Recovered ? "Восстановлено" : "Применено")
        : "Отменено";
}

public sealed class JournalDocument
{
    public List<JournalTransaction> Transactions { get; set; } = new();
}
