using System.Windows;
using System.Windows.Threading;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\WinThemeStudio.SingleInstance";
    private Mutex? _instanceMutex;

    public AppServices Services { get; private set; } = null!;
    public bool StartedInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Logger.Error("Необработанная ошибка приложения", ex);
            }
        };

        if (!AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        try
        {
            Services = new AppServices();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось запустить приложение", ex);
            MessageBox.Show(
                "WinTheme Studio не удалось запустить.\n\n" + ex.Message +
                "\n\nПодробности записаны в журнал приложения.",
                "WinTheme Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Services.AutoStart.RefreshCommand();

        StartedInTray = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

        if (e.Args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            NativeMethods.AttachParentConsole();
            PrintHelp();
            Shutdown(0);
            return;
        }

        var listProfilesIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--list-profiles", StringComparison.OrdinalIgnoreCase));
        if (listProfilesIndex >= 0)
        {
            NativeMethods.AttachParentConsole();
            ListProfiles();
            Shutdown(0);
            return;
        }

        var dryRunIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
        if (dryRunIndex >= 0)
        {
            NativeMethods.AttachParentConsole();
            var key = ArgumentValue(e.Args, dryRunIndex) ?? "aurora";
            RunDryRun(key);
            return;
        }

        var exportIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--export-profile", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0)
        {
            NativeMethods.AttachParentConsole();
            var key = ArgumentValue(e.Args, exportIndex) ?? "aurora";
            var path = exportIndex + 2 < e.Args.Length && !e.Args[exportIndex + 2].StartsWith("--", StringComparison.Ordinal)
                ? e.Args[exportIndex + 2]
                : null;
            ExportProfile(key, path);
            return;
        }

        var undoLastIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--undo-last", StringComparison.OrdinalIgnoreCase));
        if (undoLastIndex >= 0)
        {
            NativeMethods.AttachParentConsole();
            RunUndo(last: true);
            return;
        }

        var undoAllIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--undo-all", StringComparison.OrdinalIgnoreCase));
        if (undoAllIndex >= 0)
        {
            NativeMethods.AttachParentConsole();
            RunUndo(last: false);
            return;
        }

        var selfTestIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));
        if (selfTestIndex >= 0)
        {
            var directory = ArgumentAfter(e.Args, selfTestIndex) ?? Path.Combine(AppPaths.LocalDir, "selftest");
            _ = RunSelfTestAsync(directory);
            return;
        }

        var screenshotIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0)
        {
            var directory = ArgumentAfter(e.Args, screenshotIndex) ?? Path.Combine(AppPaths.LocalDir, "screenshots");
            var headless = new MainWindow(Services, headless: true);
            MainWindow = headless;
            _ = CaptureScreenshotsAsync(headless, directory);
            return;
        }

        var profileIndex = Array.FindIndex(e.Args,
            a => string.Equals(a, "--apply-profile", StringComparison.OrdinalIgnoreCase));
        if (profileIndex >= 0)
        {
            var key = profileIndex + 1 < e.Args.Length && !e.Args[profileIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? e.Args[profileIndex + 1].Trim()
                : "windows";
            _ = ApplyProfileHeadlessAsync(key);
            return;
        }

        var window = new MainWindow(Services);
        MainWindow = window;

        if (StartedInTray)
        {
            window.PrepareForTrayStartup();
        }
        else
        {
            window.Show();
        }

        _ = InitializeAsync(window);
    }

    private bool AcquireSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось проверить единственный экземпляр: " + ex.Message);
            return true;
        }

        if (!NativeMethods.ActivateExistingInstance())
        {
            MessageBox.Show(
                "WinTheme Studio уже запущен.\n\nРазверните существующее окно из панели задач или двойным щелчком по значку в трее.",
                "WinTheme Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        return false;
    }

    private static string? ArgumentAfter(string[] args, int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(args[index + 1]);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? ArgumentValue(string[] args, int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        return args[index + 1].Trim();
    }

    private static void PrintHelp()
    {
        WriteLine("WinTheme Studio — командная строка");
        WriteLine("");
        WriteLine("  --apply-profile <key>     применить профиль");
        WriteLine("  --dry-run [key]           показать план без изменений (по умолчанию aurora)");
        WriteLine("  --export-profile <key> [файл]  выгрузить профиль в JSON");
        WriteLine("  --list-profiles           список профилей");
        WriteLine("  --undo-last               отменить последнее изменение");
        WriteLine("  --undo-all                отменить все изменения");
        WriteLine("  --selftest [папка]        самопроверка без изменения системы");
        WriteLine("  --screenshot [папка]      снимки интерфейса");
        WriteLine("  --tray                    запустить свёрнутым в трей");
        WriteLine("  --help                    эта справка");
    }

    private void ListProfiles()
    {
        WriteLine("WinTheme Studio · профили");
        foreach (var profile in ProfileService.GetProfiles())
        {
            var style = IconPackService.ResolveStyle(Services.Config.Current.IconStyle, profile.Key);
            WriteLine($"  {profile.Key,-10} {profile.Name,-12} {profile.Tagline}");
            WriteLine($"             акцент {profile.AccentHex}, тема {(profile.IsDark ? "тёмная" : "светлая")}, " +
                      $"обои {(string.IsNullOrWhiteSpace(profile.WallpaperKey) ? "исходные" : profile.WallpaperKey)}, " +
                      $"значки {ProfileService.StyleName(style)}");
        }
    }

    private void RunDryRun(string key)
    {
        try
        {
            var profile = ProfileService.GetProfile(key);
            var options = ProfileApplyOptions.All;
            var plan = Services.Profiles.Plan(profile.Key, options);
            var lines = new List<string>
            {
                $"WinTheme Studio · предпросмотр профиля «{profile.Name}»",
                $"Изменяемые разделы: {options.Describe()}",
                ""
            };

            foreach (var item in plan)
            {
                lines.Add($"[{item.Area}] {item.Title}");
                lines.Add("    " + item.Detail);
            }

            lines.Add("");
            lines.Add("Система не изменена. Это только план.");
            var text = string.Join(Environment.NewLine, lines);
            WriteLine(text);

            Directory.CreateDirectory(AppPaths.LocalDir);
            var path = Path.Combine(AppPaths.LocalDir, "dry-run.txt");
            File.WriteAllText(path, text);
            WriteLine("");
            WriteLine("Сохранено: " + path);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось выполнить предпросмотр профиля", ex);
            WriteLine("Ошибка: " + ex.Message);
            Shutdown(2);
        }
    }

    private void ExportProfile(string key, string? path)
    {
        try
        {
            var target = Services.Profiles.SaveExport(key, path);
            WriteLine("Профиль выгружен: " + target);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось выгрузить профиль", ex);
            WriteLine("Ошибка: " + ex.Message);
            Shutdown(2);
        }
    }

    private void RunUndo(bool last)
    {
        try
        {
            var active = Services.Journal.ActiveTransactionCount;
            if (active == 0)
            {
                WriteLine("В журнале нет изменений для отмены.");
                Shutdown(0);
                return;
            }

            var undone = last ? Services.Journal.UndoLast() : Services.Journal.UndoAll();
            var failed = Services.Journal.LastUndoFailures;
            WriteLine(last
                ? $"Отменено действий: {undone}."
                : $"Полный откат: отменено действий {undone}.");
            if (failed > 0)
            {
                WriteLine($"Не удалось отменить: {failed}. Журнал сохранён для повторной попытки.");
                Shutdown(2);
                return;
            }

            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось выполнить откат из командной строки", ex);
            WriteLine("Ошибка: " + ex.Message);
            Shutdown(2);
        }
    }

    private static void WriteLine(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // A GUI process may not have a console; the log still gets the line.
        }

        Logger.Info(text);
    }

    private async Task RunSelfTestAsync(string directory)
    {
        try
        {
            var exitCode = await SelfTestService.RunAsync(Services, directory);
            Shutdown(exitCode);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось выполнить самопроверку", ex);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "selftest-error.txt"), ex.ToString());
            }
            catch
            {
                // the log still has the details
            }

            Shutdown(2);
        }
    }

    private async Task ApplyProfileHeadlessAsync(string key)
    {
        try
        {
            var profile = ProfileService.GetProfile(key);
            await Services.Profiles.ApplyAsync(profile.Key);
            Logger.Info($"Профиль {profile.Name} применён из командной строки.");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось применить профиль из командной строки", ex);
            Shutdown(2);
        }
    }

    private async Task CaptureScreenshotsAsync(MainWindow window, string directory)
    {
        try
        {
            var files = await ScreenshotService.CaptureAsync(window, Services, directory);
            Logger.Info($"Создано снимков интерфейса: {files.Count}.");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось создать снимки интерфейса", ex);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "screenshot-error.txt"), ex.ToString());
            }
            catch
            {
                // the log still has the details
            }

            Shutdown(2);
        }
    }

    private async Task InitializeAsync(MainWindow window)
    {
        try
        {
            await window.InitializeAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка инициализации оформления", ex);
            window.ShowToast("Не удалось подготовить часть оформления. Подробности в журнале.", true);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Ошибка интерфейса", e.Exception);
        e.Handled = true;
        if (MainWindow is MainWindow window)
        {
            window.ShowToast("Произошла ошибка. Подробности записаны в журнал.", true);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.DisposeTray();
        }

        Services?.Overlays.Stop();

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // the mutex may already have been released by the OS
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        base.OnExit(e);
    }
}
