namespace WinThemeStudio.Models;

public sealed class IconInfo
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string PreviewPath { get; set; } = "";
    public string IconPath { get; set; } = "";
}
