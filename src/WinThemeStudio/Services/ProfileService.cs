using System.Text.Json;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;
using WinThemeStudio.Overlays;

namespace WinThemeStudio.Services;

public sealed class ProfileService
{
    private readonly ChangeJournal _journal;
    private readonly ThemeService _theme;
    private readonly ExplorerService _explorer;
    private readonly WallpaperService _wallpaper;
    private readonly ConfigService _config;
    private readonly OverlayManager _overlays;
    private readonly IconPackService _icons;
    private readonly ShortcutService _shortcuts;
    private readonly ShellIconService _shellIcons;

    public ProfileService(
        ChangeJournal journal,
        ThemeService theme,
        ExplorerService explorer,
        WallpaperService wallpaper,
        ConfigService config,
        OverlayManager overlays,
        IconPackService icons,
        ShortcutService shortcuts,
        ShellIconService shellIcons)
    {
        _journal = journal;
        _theme = theme;
        _explorer = explorer;
        _wallpaper = wallpaper;
        _config = config;
        _overlays = overlays;
        _icons = icons;
        _shortcuts = shortcuts;
        _shellIcons = shellIcons;
    }

    public static IReadOnlyList<ProfileInfo> GetProfiles() => new List<ProfileInfo>
    {
        new()
        {
            Key = "windows",
            Name = "Windows 11",
            Tagline = "Чистый системный вид",
            Description = "Возвращает светлую тему, синий системный акцент, исходные обои, стандартную панель задач, параметры Проводника и исходные значки.",
            AccentHex = "#0078D4",
            AccentName = "Синий Windows",
            PreviewKey = "windows",
            WallpaperKey = "",
            IsDark = false,
            Features = new[] { "Светлая тема", "Акцент #0078D4", "Исходные обои", "Стандартный Проводник", "Исходные значки" }
        },
        new()
        {
            Key = "aurora",
            Name = "Aurora",
            Tagline = "Вдохновлено macOS",
            Description = "Тёмная тема с холодным синим акцентом, полярными обоями, центрированной панелью задач, настройками Проводника и набором значков Aurora.",
            AccentHex = "#0A84FF",
            AccentName = "Ледяной синий",
            PreviewKey = "aurora",
            WallpaperKey = "aurora",
            IsDark = true,
            Features = new[] { "Тёмная тема", "Акцент #0A84FF", "Обои Aurora", "Панель по центру", "Проводник Aurora", "Значки Aurora" }
        },
        new()
        {
            Key = "aubergine",
            Name = "Aubergine",
            Tagline = "Вдохновлено Ubuntu",
            Description = "Глубокий фиолетовый с оранжевым акцентом, тёмными обоями, значками панели слева, компактным Проводником и набором значков Aubergine.",
            AccentHex = "#E95420",
            AccentName = "Тёплый оранжевый",
            PreviewKey = "aubergine",
            WallpaperKey = "aubergine",
            IsDark = true,
            Features = new[] { "Тёмная тема", "Акцент #E95420", "Обои Aubergine", "Значки панели слева", "Компактный Проводник", "Значки Aubergine" }
        },
        new()
        {
            Key = "minimal",
            Name = "Minimal",
            Tagline = "Тише и спокойнее",
            Description = "Графитовая тема без прозрачности и анимаций, спокойные обои, компактный Проводник и сдержанный набор значков.",
            AccentHex = "#9AA3B2",
            AccentName = "Серый графит",
            PreviewKey = "minimal",
            WallpaperKey = "graphite",
            IsDark = true,
            Features = new[] { "Тёмная тема", "Акцент #9AA3B2", "Обои Graphite", "Без прозрачности", "Без анимаций", "Компактный Проводник", "Значки Minimal" }
        },
        new()
        {
            Key = "midnight",
            Name = "Midnight",
            Tagline = "Ночь и золото",
            Description = "Глубокий синий с золотым акцентом, звёздными обоями, центрированной панелью задач и значками Midnight.",
            AccentHex = "#D4AF37",
            AccentName = "Золотой",
            PreviewKey = "midnight",
            WallpaperKey = "midnight",
            IsDark = true,
            Features = new[] { "Тёмная тема", "Акцент #D4AF37", "Обои Midnight", "Панель по центру", "Значки Midnight" }
        },
        new()
        {
            Key = "mint",
            Name = "Mint",
            Tagline = "Свежий и светлый",
            Description = "Светлая мятная тема с зелёным акцентом, мягкими обоями, центрированной панелью задач и значками Mint.",
            AccentHex = "#2FA37C",
            AccentName = "Мятный зелёный",
            PreviewKey = "mint",
            WallpaperKey = "mint",
            IsDark = false,
            Features = new[] { "Светлая тема", "Акцент #2FA37C", "Обои Mint", "Панель по центру", "Значки Mint" }
        },
        new()
        {
            Key = "sunset",
            Name = "Sunset",
            Tagline = "Тёплый закат",
            Description = "Тёмная тема с оранжевым акцентом, закатными обоями, панелью задач слева и значками Sunset.",
            AccentHex = "#FF7A45",
            AccentName = "Закатный оранжевый",
            PreviewKey = "sunset",
            WallpaperKey = "sunset",
            IsDark = true,
            Features = new[] { "Тёмная тема", "Акцент #FF7A45", "Обои Sunset", "Панель слева", "Значки Sunset" }
        }
    };

    public static ProfileInfo GetProfile(string key) =>
        GetProfiles().FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? GetProfiles()[0];

    public IReadOnlyList<ProfilePlanItem> Plan(string key, ProfileApplyOptions? options = null)
    {
        var profile = GetProfile(key);
        var look = GetLook(profile.Key);
        options ??= ProfileApplyOptions.All;
        var plan = new List<ProfilePlanItem>();

        if (options.Theme)
        {
            plan.Add(new ProfilePlanItem("Тема", look.Dark ? "Тёмная тема" : "Светлая тема",
                $"Акцент {look.Accent}, прозрачность {(look.Transparency ? "вкл" : "выкл")}, анимации {(look.Animations ? "вкл" : "выкл")}."));
        }

        if (options.Wallpaper)
        {
            var preset = string.IsNullOrWhiteSpace(look.WallpaperKey) ? null : WallpaperService.GetPreset(look.WallpaperKey);
            plan.Add(new ProfilePlanItem("Обои",
                preset?.Name ?? "Исходные обои Windows",
                preset is null
                    ? "Будет восстановлено исходное изображение и режим отображения."
                    : $"{preset.Description}. Режим: {WallpaperService.FitDisplayName(_config.Current.WallpaperFit)}."));
        }

        if (options.Taskbar)
        {
            plan.Add(new ProfilePlanItem("Панель задач",
                look.TaskbarCentered ? "Значки по центру" : "Значки слева",
                $"Поиск: {SearchModeName(look.SearchMode)}, виджеты {(look.Widgets ? "вкл" : "выкл")}, чат {(look.Chat ? "вкл" : "выкл")}, секунды {(look.Seconds ? "вкл" : "выкл")}."));
        }

        if (options.Explorer)
        {
            plan.Add(new ProfilePlanItem("Проводник",
                look.Compact ? "Компактный список" : "Обычный список",
                $"Расширения {(look.HideExtensions ? "скрыты" : "показаны")}, скрытые файлы {(look.HiddenFiles ? "показаны" : "скрыты")}."));
        }

        if (options.Icons)
        {
            var style = IconPackService.ResolveStyle(_config.Current.IconStyle, profile.Key);
            plan.Add(new ProfilePlanItem("Значки",
                look.ApplyIcons ? $"Стиль {StyleName(style)}" : "Исходные значки Windows",
                look.ApplyIcons
                    ? "Папки, корзина, диски и ярлыки рабочего стола, панели задач и меню «Пуск»."
                    : "Набор WinTheme Studio будет снят, ярлыки вернутся к исходным значкам."));
        }

        return plan;
    }

    public async Task<JournalTransaction> ApplyAsync(
        string key,
        ProfileApplyOptions? options = null,
        IProgress<string>? progress = null)
    {
        var profile = GetProfile(key);
        var look = GetLook(profile.Key);
        options ??= ProfileApplyOptions.All;
        if (!options.Any)
        {
            throw new InvalidOperationException("Не выбран ни один раздел профиля.");
        }

        progress?.Report("Подготовка профиля…");

        WallpaperPreset? preset = null;
        if (options.Wallpaper && look.WallpaperKey.Length > 0)
        {
            preset = WallpaperService.GetPreset(look.WallpaperKey);
            await _wallpaper.EnsureWallpaperAsync(preset);
        }

        if (options.Icons)
        {
            await _icons.EnsurePackAsync();
        }

        var tx = _journal.Begin("Профиль: " + profile.Name);
        tx.PreviousConfig = UndoConfigState.Capture(_config.Current);
        try
        {
            if (options.Theme)
            {
                progress?.Report("Применение темы и акцента…");
                _theme.SetTheme(tx, look.Dark);
                _theme.SetAccent(tx, look.Accent);
                _theme.SetTransparency(tx, look.Transparency);
                _theme.SetAnimations(tx, look.Animations);
            }

            if (options.Taskbar)
            {
                progress?.Report("Настройка панели задач…");
                _explorer.SetTaskbarCentered(tx, look.TaskbarCentered);
                _explorer.SetSearchMode(tx, look.SearchMode);
                _explorer.SetWidgetsShown(tx, look.Widgets);
                _explorer.SetChatShown(tx, look.Chat);
                _explorer.SetSecondsShown(tx, look.Seconds);
            }

            if (options.Explorer)
            {
                progress?.Report("Настройка Проводника…");
                _explorer.SetFileExtensionsHidden(tx, look.HideExtensions);
                _explorer.SetHiddenFilesShown(tx, look.HiddenFiles);
                _explorer.SetCompactMode(tx, look.Compact);
            }

            if (options.Wallpaper)
            {
                progress?.Report("Установка обоев…");
                if (preset is not null)
                {
                    await _wallpaper.ApplyAsync(tx, preset);
                }
                else
                {
                    _wallpaper.RestoreOriginal(tx);
                }
            }

            if (options.Icons)
            {
                progress?.Report("Обновление значков…");
                var iconStyle = IconPackService.ResolveStyle(_config.Current.IconStyle, profile.Key);
                var shortcuts = await _shortcuts.LoadAsync(iconStyle);
                if (look.ApplyIcons)
                {
                    var cfg = _config.Current;
                    _shellIcons.ApplyIcons(
                        tx,
                        _icons.GetIconPath("folder", iconStyle),
                        _icons.GetIconPath("trash", iconStyle),
                        cfg.UseFullTrashIcon ? _icons.GetIconPath("trash-full", iconStyle) : null,
                        cfg.UseDriveIcons ? _icons.GetIconPath("drive", iconStyle) : null,
                        cfg.UseDriveIcons);
                    var iconResult = _shortcuts.ApplyToAll(tx, shortcuts, iconStyle);
                    Logger.Info($"Профиль {profile.Name}: обновлено ярлыков {iconResult.Applied}, пропущено {iconResult.Skipped}, ошибок {iconResult.Failed}.");
                }
                else
                {
                    _shellIcons.RemoveOwned(tx, AppPaths.IconPackDir);
                    var restoreResult = _shortcuts.RestoreAll(tx, shortcuts);
                    Logger.Info($"Профиль {profile.Name}: исходных значков восстановлено {restoreResult.Applied}, пропущено {restoreResult.Skipped}, ошибок {restoreResult.Failed}.");
                }
            }

            NativeMethods.RefreshShellIcons();
            _journal.Commit(tx);
        }
        catch
        {
            if (tx.Entries.Count > 0)
            {
                _journal.Commit(tx);
            }

            throw;
        }

        var config = _config.Current;
        if (options.Theme || options.Wallpaper || options.Icons)
        {
            config.LastAppliedProfile = profile.Key;
        }

        // The floating dock is optional. Profiles must never put icons over
        // the desktop on their own.
        config.DockEnabled = false;
        _config.Save();

        progress?.Report("Обновление дока…");
        _overlays.Refresh();
        Logger.Info($"Применён профиль {profile.Name} ({tx.Entries.Count} изменений).");
        return tx;
    }

    public string ExportProfile(string key)
    {
        var profile = GetProfile(key);
        var look = GetLook(profile.Key);
        var payload = new
        {
            schema = "wintheme-profile/v1",
            exportedUtc = DateTime.UtcNow,
            profile.Key,
            profile.Name,
            profile.Tagline,
            profile.Description,
            profile.AccentHex,
            profile.AccentName,
            profile.WallpaperKey,
            look.Dark,
            look.Transparency,
            look.Animations,
            look.TaskbarCentered,
            look.SearchMode,
            look.Widgets,
            look.Chat,
            look.Seconds,
            look.HideExtensions,
            look.HiddenFiles,
            look.Compact,
            look.ApplyIcons,
            plan = Plan(key, ProfileApplyOptions.All)
                .Select(item => new { item.Area, item.Title, item.Detail })
                .ToList()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public string SaveExport(string key, string? path = null)
    {
        var profile = GetProfile(key);
        var target = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppPaths.LocalDir, "profiles", profile.Key + ".json")
            : path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.WriteAllText(target, ExportProfile(profile.Key));
        return target;
    }

    private static string SearchModeName(int mode) => mode switch
    {
        0 => "скрыт",
        1 => "значок",
        2 => "поле",
        3 => "поле с подписью",
        _ => "поле"
    };

    public static string StyleName(string style) => IconPackService.NormalizeStyle(style) switch
    {
        "aurora" => "Aurora",
        "aubergine" => "Aubergine",
        "minimal" => "Minimal",
        "midnight" => "Midnight",
        "mint" => "Mint",
        "sunset" => "Sunset",
        "studio" => "Студия",
        "auto" => "Авто",
        _ => "Default"
    };

    private static ProfileLook GetLook(string key) => key switch
    {
        "aurora" => new ProfileLook(
            Dark: true,
            Accent: "#0A84FF",
            Transparency: true,
            Animations: true,
            TaskbarCentered: true,
            SearchMode: 1,
            Widgets: false,
            Chat: false,
            Seconds: true,
            HideExtensions: true,
            HiddenFiles: false,
            Compact: false,
            WallpaperKey: "aurora",
            ApplyIcons: true),
        "aubergine" => new ProfileLook(
            Dark: true,
            Accent: "#E95420",
            Transparency: true,
            Animations: true,
            TaskbarCentered: false,
            SearchMode: 2,
            Widgets: false,
            Chat: false,
            Seconds: true,
            HideExtensions: false,
            HiddenFiles: false,
            Compact: true,
            WallpaperKey: "aubergine",
            ApplyIcons: true),
        "minimal" => new ProfileLook(
            Dark: true,
            Accent: "#9AA3B2",
            Transparency: false,
            Animations: false,
            TaskbarCentered: true,
            SearchMode: 0,
            Widgets: false,
            Chat: false,
            Seconds: false,
            HideExtensions: true,
            HiddenFiles: false,
            Compact: true,
            WallpaperKey: "graphite",
            ApplyIcons: true),
        "midnight" => new ProfileLook(
            Dark: true,
            Accent: "#D4AF37",
            Transparency: true,
            Animations: true,
            TaskbarCentered: true,
            SearchMode: 1,
            Widgets: false,
            Chat: false,
            Seconds: true,
            HideExtensions: true,
            HiddenFiles: false,
            Compact: false,
            WallpaperKey: "midnight",
            ApplyIcons: true),
        "mint" => new ProfileLook(
            Dark: false,
            Accent: "#2FA37C",
            Transparency: true,
            Animations: true,
            TaskbarCentered: true,
            SearchMode: 2,
            Widgets: false,
            Chat: false,
            Seconds: false,
            HideExtensions: false,
            HiddenFiles: false,
            Compact: false,
            WallpaperKey: "mint",
            ApplyIcons: true),
        "sunset" => new ProfileLook(
            Dark: true,
            Accent: "#FF7A45",
            Transparency: true,
            Animations: true,
            TaskbarCentered: false,
            SearchMode: 1,
            Widgets: false,
            Chat: false,
            Seconds: true,
            HideExtensions: true,
            HiddenFiles: false,
            Compact: false,
            WallpaperKey: "sunset",
            ApplyIcons: true),
        _ => new ProfileLook(
            Dark: false,
            Accent: "#0078D4",
            Transparency: true,
            Animations: true,
            TaskbarCentered: true,
            SearchMode: 2,
            Widgets: true,
            Chat: false,
            Seconds: false,
            HideExtensions: true,
            HiddenFiles: false,
            Compact: false,
            WallpaperKey: "",
            ApplyIcons: false)
    };

    private sealed record ProfileLook(
        bool Dark,
        string Accent,
        bool Transparency,
        bool Animations,
        bool TaskbarCentered,
        int SearchMode,
        bool Widgets,
        bool Chat,
        bool Seconds,
        bool HideExtensions,
        bool HiddenFiles,
        bool Compact,
        string WallpaperKey,
        bool ApplyIcons);
}
