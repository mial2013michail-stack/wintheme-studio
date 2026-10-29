using WinThemeStudio.Core;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class ConfigService
{
    private readonly object _gate = new();
    private bool _wasNew;

    public AppConfig Current { get; private set; }

    public event EventHandler? Changed;

    public ConfigService()
    {
        AppPaths.EnsureCreated();
        _wasNew = !File.Exists(AppPaths.ConfigFile);
        Current = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
        Normalize();

        if (_wasNew)
        {
            Current.DockItems = CreateDefaultDockItems();
            Save(false);
        }
    }

    public void Save(bool notify = true)
    {
        lock (_gate)
        {
            JsonStore.Save(AppPaths.ConfigFile, Current);
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Update(Action<AppConfig> mutate, bool notify = true)
    {
        lock (_gate)
        {
            mutate(Current);
            JsonStore.Save(AppPaths.ConfigFile, Current);
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Normalize()
    {
        Current ??= new AppConfig();
        Current.DockItems ??= new List<DockItemConfig>();
        Current.CustomIconPaths ??= new Dictionary<string, string>();
        Current.WallpaperFit = WallpaperService.NormalizeFit(Current.WallpaperFit);
        Current.IconStyle = IconPackService.NormalizeStyle(Current.IconStyle);
        Current.IconRadius = Math.Clamp(Current.IconRadius, -1, 100);
        Current.SchedulerProfile = string.IsNullOrWhiteSpace(Current.SchedulerProfile)
            ? "aurora"
            : Current.SchedulerProfile.Trim();
        Current.SchedulerTime = NormalizeTime(Current.SchedulerTime);
        Current.SchedulerLastRun ??= "";

        // Configs created before the capture flag existed already hold original
        // wallpaper data; do not overwrite it with the currently applied preset.
        if (!Current.OriginalWallpaperCaptured &&
            (!string.IsNullOrWhiteSpace(Current.OriginalWallpaperPath) ||
             !string.IsNullOrWhiteSpace(Current.OriginalWallpaperRegistryPath)))
        {
            Current.OriginalWallpaperCaptured = true;
        }


        if (string.IsNullOrWhiteSpace(Current.DockPosition))
        {
            Current.DockPosition = "bottom";
        }

        if (Current.DockSize < 32 || Current.DockSize > 84)
        {
            Current.DockSize = 48;
        }

        foreach (var item in Current.DockItems)
        {
            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
            item.Kind = string.IsNullOrWhiteSpace(item.Kind) ? "builtin" : item.Kind;
        }
    }

    private static string NormalizeTime(string? value)
    {
        return TimeSpan.TryParse(value, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
            ? time.ToString(@"hh\:mm")
            : "09:00";
    }

    public static List<DockItemConfig> CreateDefaultDockItems()
    {
        return new List<DockItemConfig>
        {
            new() { Name = "Проводник", Kind = "builtin", Target = "explorer" },
            new() { Name = "Браузер", Kind = "builtin", Target = "browser" },
            new() { Name = "Терминал", Kind = "builtin", Target = "terminal" },
            new() { Name = "Параметры", Kind = "builtin", Target = "settings" },
            new() { Name = "Блокнот", Kind = "builtin", Target = "notepad" },
            new() { Name = "Диспетчер задач", Kind = "builtin", Target = "taskmgr" },
            new() { Name = "Корзина", Kind = "builtin", Target = "trash" }
        };
    }
}
