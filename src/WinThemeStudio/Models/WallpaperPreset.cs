namespace WinThemeStudio.Models;

public sealed class WallpaperPreset
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsLight { get; init; }
    public string ThumbnailPath { get; set; } = "";
    public string FullPath { get; set; } = "";
}
