namespace WinThemeStudio.Models;

public sealed class ProfileInfo
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Tagline { get; init; }
    public required string Description { get; init; }
    public required string AccentHex { get; init; }
    public required string AccentName { get; init; }
    public required string PreviewKey { get; init; }
    public required string[] Features { get; init; }
    public string WallpaperKey { get; init; } = "";
    public bool IsDark { get; init; }
}

public sealed class ProfileApplyOptions
{
    public bool Theme { get; set; } = true;
    public bool Wallpaper { get; set; } = true;
    public bool Taskbar { get; set; } = true;
    public bool Explorer { get; set; } = true;
    public bool Icons { get; set; } = true;

    public bool Any => Theme || Wallpaper || Taskbar || Explorer || Icons;

    public static ProfileApplyOptions All => new();

    public ProfileApplyOptions Clone() => (ProfileApplyOptions)MemberwiseClone();

    public string Describe()
    {
        var parts = new List<string>();
        if (Theme) parts.Add("тема и акцент");
        if (Wallpaper) parts.Add("обои");
        if (Taskbar) parts.Add("панель задач");
        if (Explorer) parts.Add("Проводник");
        if (Icons) parts.Add("значки");
        return parts.Count == 0 ? "ничего не выбрано" : string.Join(", ", parts);
    }
}

public sealed record ProfilePlanItem(string Area, string Title, string Detail);
