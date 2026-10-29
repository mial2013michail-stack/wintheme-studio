using WinThemeStudio.Core;
using WinThemeStudio.Models;
using WinThemeStudio.Overlays;

namespace WinThemeStudio.Services;

public sealed class AppServices
{
    public ConfigService Config { get; }
    public ChangeJournal Journal { get; }
    public ThemeService Theme { get; }
    public ExplorerService Explorer { get; }
    public WallpaperService Wallpaper { get; }
    public IconPackService Icons { get; }
    public ShortcutService Shortcuts { get; }
    public ShellIconService ShellIcons { get; }
    public RestorePointService RestorePoints { get; }
    public AutoStartService AutoStart { get; }
    public OverlayManager Overlays { get; }
    public ProfileService Profiles { get; }
    public SchedulerService Scheduler { get; }
    public DialogService Dialogs { get; }

    public AppServices()
    {
        Logger.Initialize();
        AppPaths.EnsureCreated();

        Config = new ConfigService();
        Journal = new ChangeJournal();
        Theme = new ThemeService(Journal);
        Explorer = new ExplorerService(Journal);
        Wallpaper = new WallpaperService(Journal, Config);
        Icons = new IconPackService(Config);
        Shortcuts = new ShortcutService(Journal, Icons);
        ShellIcons = new ShellIconService(Journal);
        RestorePoints = new RestorePointService();
        AutoStart = new AutoStartService(Journal);
        Overlays = new OverlayManager(Config, Icons);
        Profiles = new ProfileService(Journal, Theme, Explorer, Wallpaper, Config, Overlays, Icons, Shortcuts, ShellIcons);
        Scheduler = new SchedulerService(Config, Profiles, Explorer);
        Dialogs = new DialogService();

        Journal.TransactionUndone += (_, transaction) => ApplyUndoneConfig(transaction);

        Wallpaper.CaptureOriginalIfNeeded();
    }

    private void ApplyUndoneConfig(JournalTransaction transaction)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ApplyUndoneConfig(transaction));
            return;
        }

        if (transaction.PreviousConfig is not null)
        {
            Config.Update(config => transaction.PreviousConfig.ApplyTo(config), notify: false);
        }

        Overlays.Refresh();
    }
}
