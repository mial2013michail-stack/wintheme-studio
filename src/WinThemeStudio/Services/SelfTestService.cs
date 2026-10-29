using Microsoft.Win32;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

/// <summary>
/// Headless diagnostics. Exercises the journal, registry rollback, file rollback
/// and asset generation without changing any real Windows setting.
/// </summary>
public static class SelfTestService
{
    private const string TestKeyPath = @"Software\WinThemeStudio\SelfTest";

    public static async Task<int> RunAsync(AppServices services, string directory)
    {
        Directory.CreateDirectory(directory);

        var lines = new List<string>();
        var failures = 0;

        void Check(string name, bool ok, string? detail = null)
        {
            if (!ok)
            {
                failures++;
            }

            lines.Add((ok ? "[ OK ] " : "[FAIL] ") + name + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " — " + detail));
        }

        var journalFile = AppPaths.JournalFile;
        var journalExisted = File.Exists(journalFile);
        byte[]? journalBytes = journalExisted ? File.ReadAllBytes(journalFile) : null;
        string? sampleDirectory = null;
        string? sampleBackupDirectory = null;
        string? retryDirectory = null;
        string? retryBackupDirectory = null;
        string? testShortcutPath = null;
        string? shortcutBaselinePath = null;
        var iconTransactionIds = new List<string>();

        try
        {
            lines.Add("WinTheme Studio — самопроверка " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.Add("Данные: " + AppPaths.LocalDir);
            lines.Add(string.Empty);

            // Data directory and temp file.
            var probePath = Path.Combine(AppPaths.TempDir, "selftest-probe-" + Guid.NewGuid().ToString("N") + ".txt");
            Directory.CreateDirectory(AppPaths.TempDir);
            File.WriteAllText(probePath, "probe");
            Check("Каталог данных доступен для записи", File.ReadAllText(probePath) == "probe", probePath);
            File.Delete(probePath);

            // Registry round-trip through the journal.
            Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, false);
            var journal = new ChangeJournal();
            var tx = journal.Begin("Самопроверка: реестр");
            var textWritten = journal.SetHkcuValue(tx, TestKeyPath, "Text", "WinTheme Studio");
            var numberWritten = journal.SetHkcuValue(tx, TestKeyPath, "Number", 42);
            Check("Запись значений HKCU", textWritten && numberWritten);

            using (var key = Registry.CurrentUser.OpenSubKey(TestKeyPath, false))
            {
                Check(
                    "Чтение значений HKCU",
                    key?.GetValue("Text") as string == "WinTheme Studio" && key?.GetValue("Number") is int number && number == 42);
            }

            journal.Commit(tx);
            Check(
                "Транзакция записана в журнал",
                journal.ActiveTransactions().Any(t => t.Id == tx.Id && t.EntryCount == 2));

            tx.PreviousConfig = new UndoConfigState
            {
                LastAppliedProfile = "macos",
                DockSize = 56
            };
            journal.Commit(tx);

            UndoConfigState? undoneConfig = null;
            journal.TransactionUndone += (_, transaction) => undoneConfig = transaction.PreviousConfig;

            var undone = journal.Undo(tx);
            Check("Отмена транзакции", undone);
            Check(
                "Снимок настроек передан при отмене",
                undoneConfig is { LastAppliedProfile: "macos" } &&
                Math.Abs(undoneConfig.DockSize - 56) < 0.01);

            var textGone = true;
            var numberGone = true;
            using (var key = Registry.CurrentUser.OpenSubKey(TestKeyPath, false))
            {
                if (key is not null)
                {
                    textGone = key.GetValue("Text") is null;
                    numberGone = key.GetValue("Number") is null;
                }
            }

            Check("Значения реестра восстановлены", textGone && numberGone);
            Check(
                "Транзакция помечена как отменённая",
                journal.Snapshot().Any(t => t.Id == tx.Id && t.UndoneUtc is not null));

            // Whole-key deletion is used by the classic context-menu toggle.
            var treePath = TestKeyPath + @"\Tree";
            using (var tree = Registry.CurrentUser.CreateSubKey(treePath + @"\Child", true))
            {
                tree?.SetValue("Inner", "kept");
            }

            var keyTx = journal.Begin("Самопроверка: раздел реестра");
            var keyDeleted = journal.DeleteHkcuKey(keyTx, treePath);
            var keyGone = Registry.CurrentUser.OpenSubKey(treePath, false) is null;
            journal.Commit(keyTx);
            var keyUndone = journal.Undo(keyTx);
            var keyRestored = Registry.CurrentUser.OpenSubKey(treePath + @"\Child", false)
                ?.GetValue("Inner") as string == "kept";
            Check("Удаление раздела реестра отменяется", keyDeleted && keyGone && keyUndone && keyRestored);

            // File backup and restore.
            sampleDirectory = Path.Combine(AppPaths.TempDir, "selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sampleDirectory);
            var sampleFile = Path.Combine(sampleDirectory, "sample.txt");
            File.WriteAllText(sampleFile, "original-content");

            var fileJournal = new ChangeJournal();
            var fileTx = fileJournal.Begin("Самопроверка: файл");
            var backedUp = fileJournal.BackupFile(fileTx, sampleFile, "sample.txt");
            File.WriteAllText(sampleFile, "changed-content");
            fileJournal.Commit(fileTx);
            sampleBackupDirectory = Path.Combine(AppPaths.FileBackupsDir, fileTx.Id);

            var fileUndone = fileJournal.Undo(fileTx);
            Check("Резервная копия файла создана", backedUp && File.Exists(Path.Combine(sampleBackupDirectory, "000-sample.txt")));
            Check("Файл восстановлен из копии", fileUndone && File.ReadAllText(sampleFile) == "original-content");

            // The journal must survive a reload with the undo state intact.
            var reloaded = new ChangeJournal();
            Check(
                "Журнал читается после перезапуска",
                reloaded.Snapshot().Any(t => t.Id == tx.Id && t.UndoneUtc is not null) &&
                reloaded.Snapshot().Any(t => t.Id == fileTx.Id && t.UndoneUtc is not null));
            Check(
                "Снимок настроек сохраняется в журнале",
                reloaded.Snapshot().Any(t =>
                    t.Id == tx.Id &&
                    t.PreviousConfig is { LastAppliedProfile: "macos", DockSize: 56 }));

            // A failed undo must leave the transaction active so a retry can
            // finish the job once the missing backup is available again.
            retryDirectory = Path.Combine(AppPaths.TempDir, "selftest-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(retryDirectory);
            var retryFile = Path.Combine(retryDirectory, "retry.txt");
            File.WriteAllText(retryFile, "before-failure");

            var retryJournal = new ChangeJournal();
            var retryTx = retryJournal.Begin("Самопроверка: повторная отмена");
            retryJournal.BackupFile(retryTx, retryFile, "retry.txt");
            File.WriteAllText(retryFile, "after-failure");
            retryJournal.Commit(retryTx);
            retryBackupDirectory = Path.Combine(AppPaths.FileBackupsDir, retryTx.Id);
            var retryBackup = Path.Combine(retryBackupDirectory, "000-retry.txt");
            if (File.Exists(retryBackup))
            {
                File.Delete(retryBackup);
            }

            var failedUndo = retryJournal.Undo(retryTx);
            var failuresAfterFirstAttempt = retryJournal.LastUndoFailures;
            var stillActive = retryJournal.Snapshot().Any(t => t.Id == retryTx.Id && t.UndoneUtc is null);

            File.WriteAllText(retryBackup, "before-failure");
            var retried = retryJournal.Undo(retryTx);

            Check(
                "Сбой отмены сохраняет транзакцию активной",
                !failedUndo && failuresAfterFirstAttempt == 1 && stillActive);
            Check(
                "Повторная отмена завершает транзакцию",
                retried && retryJournal.LastUndoFailures == 0 &&
                File.ReadAllText(retryFile) == "before-failure");

            // Config snapshots must round-trip every field that undo restores.
            var snapshotConfig = new AppConfig
            {
                LastAppliedProfile = "macos",
                LastWallpaperKey = "aurora",
                LastWallpaperIsCustom = true,
                WallpaperFit = "crop",
                IconStyle = "studio",
                IconTint = "#7C5CFF",
                IconRadius = 32,
                IconMonochrome = true,
                UseDriveIcons = false,
                UseFullTrashIcon = false,
                ShowDesktopIcons = false,
                ClassicContextMenu = true,
                DockEnabled = true,
                DockPosition = "left",
                DockSize = 56,
                DockAutoHide = true,
                DockBlur = false,
                DockMagnify = false,
                DockHideFullscreen = false
            };
            var configSnapshot = UndoConfigState.Capture(snapshotConfig);
            snapshotConfig.LastAppliedProfile = "windows";
            snapshotConfig.DockEnabled = false;
            snapshotConfig.DockPosition = "bottom";
            snapshotConfig.DockSize = 48;
            configSnapshot.ApplyTo(snapshotConfig);

            Check(
                "Снимок настроек отмены восстанавливается",
                snapshotConfig.LastAppliedProfile == "macos" &&
                snapshotConfig.LastWallpaperKey == "aurora" &&
                snapshotConfig.LastWallpaperIsCustom &&
                snapshotConfig.WallpaperFit == "crop" &&
                snapshotConfig.IconStyle == "studio" &&
                snapshotConfig.IconTint == "#7C5CFF" &&
                snapshotConfig.IconRadius == 32 &&
                snapshotConfig.IconMonochrome &&
                !snapshotConfig.UseDriveIcons &&
                !snapshotConfig.UseFullTrashIcon &&
                !snapshotConfig.ShowDesktopIcons &&
                snapshotConfig.ClassicContextMenu &&
                snapshotConfig.DockEnabled &&
                snapshotConfig.DockPosition == "left" &&
                Math.Abs(snapshotConfig.DockSize - 56) < 0.01 &&
                snapshotConfig.DockAutoHide &&
                !snapshotConfig.DockBlur &&
                !snapshotConfig.DockMagnify &&
                !snapshotConfig.DockHideFullscreen);

            // Original wallpaper snapshot is captured before first change.
            var originalWallpaper = services.Config.Current.OriginalWallpaperPath;
            var originalCaptured = services.Config.Current.OriginalWallpaperCaptured;
            var originalOk = originalCaptured &&
                             (string.IsNullOrWhiteSpace(originalWallpaper) || File.Exists(originalWallpaper));
            Check(
                "Исходные обои сохранены",
                originalOk,
                string.IsNullOrWhiteSpace(originalWallpaper)
                    ? originalCaptured ? "исходный фон — сплошной цвет" : "снимок не создан"
                    : originalWallpaper);

            // Asset generation.
            await services.Icons.EnsurePackAsync();
            var definitions = IconPackService.GetDefinitions();
            var previewCount = Directory.Exists(Path.Combine(AppPaths.IconPackDir, "previews"))
                ? Directory.GetFiles(Path.Combine(AppPaths.IconPackDir, "previews"), "*.png").Length
                : 0;
            var iconStyles = new[] { "default", "aurora", "aubergine", "minimal", "midnight", "mint", "sunset", "studio" };
            Check("Набор иконок создан", definitions.Count == 18 && previewCount >= 18 * iconStyles.Length,
                $"иконок: {definitions.Count}, превью: {previewCount}");

            var missingStyles = iconStyles
                .Where(style => definitions.Any(definition =>
                    !File.Exists(services.Icons.GetIconPath(definition.Key, style))))
                .ToList();
            Check("Все стили иконок созданы", missingStyles.Count == 0,
                missingStyles.Count == 0 ? $"стилей: {iconStyles.Length}" : "нет: " + string.Join(", ", missingStyles));

            var styleFolderIcons = new[] { "aurora", "midnight", "sunset" }
                .Select(style => services.Icons.GetIconPath("folder", style))
                .ToList();
            var styleTrashIcons = new[] { "aurora", "midnight", "sunset" }
                .Select(style => services.Icons.GetIconPath("trash", style))
                .ToList();
            var distinctFolders = styleFolderIcons.All(File.Exists) &&
                                  !File.ReadAllBytes(styleFolderIcons[0]).SequenceEqual(File.ReadAllBytes(styleFolderIcons[1])) &&
                                  !File.ReadAllBytes(styleFolderIcons[1]).SequenceEqual(File.ReadAllBytes(styleFolderIcons[2]));
            var distinctTrash = styleTrashIcons.All(File.Exists) &&
                                !File.ReadAllBytes(styleTrashIcons[0]).SequenceEqual(File.ReadAllBytes(styleTrashIcons[1])) &&
                                !File.ReadAllBytes(styleTrashIcons[1]).SequenceEqual(File.ReadAllBytes(styleTrashIcons[2]));
            Check("Папки и корзина отличаются по профилям", distinctFolders && distinctTrash,
                $"папки: {string.Join(", ", styleFolderIcons.Select(Path.GetFileName))}");

            var driveIcon = services.Icons.GetIconPath("drive", "aurora");
            var fullTrashIcon = services.Icons.GetIconPath("trash-full", "aurora");
            Check("Значки дисков и полной корзины созданы",
                File.Exists(driveIcon) && File.Exists(fullTrashIcon) &&
                !File.ReadAllBytes(driveIcon).SequenceEqual(File.ReadAllBytes(fullTrashIcon)));

            // The desktop and pin folders may be redirected to another drive.
            // The journal has to accept them for backups and undo.
            var desktopProbe = Path.Combine(AppPaths.DesktopDir, "wts-selftest-probe.lnk");
            var taskbarProbe = Path.Combine(AppPaths.TaskbarPinnedDir, "wts-selftest-probe.lnk");
            var startMenuProbe = Path.Combine(AppPaths.StartMenuProgramsDir, "wts-selftest-probe.lnk");
            var windowsProbe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "wts-selftest-probe.lnk");
            Check(
                "Рабочий стол, панель задач и меню «Пуск» разрешены для журнала",
                AppPaths.IsAllowedUserFile(desktopProbe) &&
                AppPaths.IsAllowedUserFile(taskbarProbe) &&
                AppPaths.IsAllowedUserFile(startMenuProbe) &&
                !AppPaths.IsAllowedUserFile(windowsProbe),
                "рабочий стол: " + AppPaths.DesktopDir);

            // Shortcut icons: apply, undo and restore on the real desktop,
            // which also covers the redirected-folder backup fix.
            try
            {
                testShortcutPath = Path.Combine(
                    AppPaths.DesktopDir,
                    "WinTheme Studio SelfTest " + Guid.NewGuid().ToString("N")[..8] + ".lnk");

                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    Check("Создание тестового ярлыка", false, "WScript.Shell недоступен");
                }
                else
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(testShortcutPath);
                    shortcut.TargetPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                    shortcut.Save();
                }

                var shortcutCreated = File.Exists(testShortcutPath);
                Check("Создание тестового ярлыка", shortcutCreated, testShortcutPath);

                if (shortcutCreated)
                {
                    shortcutBaselinePath = Path.Combine(
                        AppPaths.IconBackupsDir, "originals", "desktop", Path.GetFileName(testShortcutPath));

                    var iconJournal = new ChangeJournal();
                    var iconItem = new ShortcutItem
                    {
                        Path = testShortcutPath,
                        Name = Path.GetFileNameWithoutExtension(testShortcutPath),
                        Target = Path.Combine(Environment.SystemDirectory, "notepad.exe"),
                        LocationKey = "desktop",
                        CategoryKey = "folder"
                    };

                    var applyTx = iconJournal.Begin("Самопроверка: значок ярлыка");
                    iconTransactionIds.Add(applyTx.Id);
                    services.Shortcuts.ApplyIcon(applyTx, iconItem, "folder", "aurora");
                    iconJournal.Commit(applyTx);

                    string iconAfterApply;
                    using (var link = ShellLinkHandle.Open(testShortcutPath))
                    {
                        iconAfterApply = link.GetIconLocation(out _);
                    }

                    var expectedIcon = services.Icons.GetIconPath("folder", "aurora");
                    Check(
                        "Значок ярлыка на рабочем столе применён",
                        string.Equals(iconAfterApply, expectedIcon, StringComparison.OrdinalIgnoreCase),
                        iconAfterApply);

                    var iconUndone = iconJournal.Undo(applyTx);
                    string iconAfterUndo;
                    using (var link = ShellLinkHandle.Open(testShortcutPath))
                    {
                        iconAfterUndo = link.GetIconLocation(out _);
                    }

                    Check(
                        "Отмена возвращает исходный значок ярлыка",
                        iconUndone && string.IsNullOrWhiteSpace(iconAfterUndo),
                        iconAfterUndo);

                    var restoreTx = iconJournal.Begin("Самопроверка: возврат значка ярлыка");
                    iconTransactionIds.Add(restoreTx.Id);
                    services.Shortcuts.ApplyIcon(restoreTx, iconItem, "folder", "aurora");
                    services.Shortcuts.RestoreOriginal(restoreTx, iconItem);
                    iconJournal.Commit(restoreTx);

                    string iconAfterRestore;
                    using (var link = ShellLinkHandle.Open(testShortcutPath))
                    {
                        iconAfterRestore = link.GetIconLocation(out _);
                    }

                    Check(
                        "Кнопка «Вернуть исходные» восстанавливает значок",
                        string.IsNullOrWhiteSpace(iconAfterRestore),
                        iconAfterRestore);
                }
            }
            catch (Exception ex)
            {
                failures++;
                lines.Add("[FAIL] Значки ярлыков: " + ex.Message);
                Logger.Error("Самопроверка значков ярлыков", ex);
            }

            // Folder and recycle-bin shell icons use a throw-away HKCU key so
            // the real Explorer appearance is never changed by diagnostics.
            try
            {
                const string shellIconKey = TestKeyPath + @"\ShellIcons";
                Registry.CurrentUser.DeleteSubKeyTree(shellIconKey, false);
                var shellJournal = new ChangeJournal();
                var shellIcons = new ShellIconService(shellJournal, shellIconKey);
                var folderIcon = services.Icons.GetIconPath("folder");
                var trashIcon = services.Icons.GetIconPath("trash");

                var shellTx = shellJournal.Begin("Самопроверка: значки папок");
                shellIcons.ApplyIcons(
                    shellTx,
                    folderIcon,
                    trashIcon,
                    services.Icons.GetIconPath("trash-full", "aurora"),
                    services.Icons.GetIconPath("drive", "aurora"),
                    includeDrives: true);
                shellJournal.Commit(shellTx);

                string? folderValue;
                string? trashValue;
                string? fullTrashValue;
                string? driveValue;
                using (var key = Registry.CurrentUser.OpenSubKey(shellIconKey, false))
                {
                    folderValue = key?.GetValue("3") as string;
                    trashValue = key?.GetValue("31") as string;
                    fullTrashValue = key?.GetValue("32") as string;
                    driveValue = key?.GetValue("7") as string;
                }

                Check(
                    "Значки папок, корзины и дисков записываются в HKCU",
                    folderValue == folderIcon + ",0" &&
                    trashValue == trashIcon + ",0" &&
                    fullTrashValue == services.Icons.GetIconPath("trash-full", "aurora") + ",0" &&
                    driveValue == services.Icons.GetIconPath("drive", "aurora") + ",0");
                Check("Применённый набор значков распознаётся", shellIcons.ArePackIconsApplied(AppPaths.IconPackDir));

                var shellUndone = shellJournal.Undo(shellTx);
                var shellValuesGone = true;
                using (var key = Registry.CurrentUser.OpenSubKey(shellIconKey, false))
                {
                    if (key is not null)
                    {
                        shellValuesGone = key.GetValue("3") is null &&
                                          key.GetValue("31") is null &&
                                          key.GetValue("32") is null &&
                                          key.GetValue("7") is null;
                    }
                }

                Check("Значки папок, корзины и дисков отменяются", shellUndone && shellValuesGone);

                // RemoveOwned must keep icons that do not come from our pack.
                var ownedTx = shellJournal.Begin("Самопроверка: чужие значки папок");
                shellJournal.SetHkcuValue(ownedTx, shellIconKey, "3", @"C:\MyIcons\folder.ico,0");
                shellJournal.SetHkcuValue(ownedTx, shellIconKey, "31", trashIcon + ",0");
                shellIcons.RemoveOwned(ownedTx, AppPaths.IconPackDir);

                string? customValue;
                string? ownedValue;
                using (var key = Registry.CurrentUser.OpenSubKey(shellIconKey, false))
                {
                    customValue = key?.GetValue("3") as string;
                    ownedValue = key?.GetValue("31") as string;
                }

                Check(
                    "Возврат значков не трогает чужие значения",
                    customValue == @"C:\MyIcons\folder.ico,0" && ownedValue is null);
                shellJournal.Commit(ownedTx);
            }
            catch (Exception ex)
            {
                failures++;
                lines.Add("[FAIL] Значки папок и корзины: " + ex.Message);
                Logger.Error("Самопроверка значков папок", ex);
            }

            await services.Wallpaper.EnsureAllAsync();
            var presets = WallpaperService.GetPresets();
            var missingThumbs = presets
                .Where(p => !File.Exists(Path.Combine(AppPaths.WallpaperThumbsDir, p.Key + ".png")))
                .Select(p => p.Key)
                .ToList();
            Check("Обои и превью созданы", presets.Count == 8 && missingThumbs.Count == 0,
                missingThumbs.Count == 0 ? $"композиций: {presets.Count}" : "нет: " + string.Join(", ", missingThumbs));

            Check(
                "Режимы отображения обоев",
                WallpaperService.FitRegistryValues("fill") == ("10", "0") &&
                WallpaperService.FitRegistryValues("fit") == ("6", "0") &&
                WallpaperService.FitRegistryValues("stretch") == ("2", "0") &&
                WallpaperService.FitRegistryValues("center") == ("0", "0") &&
                WallpaperService.FitRegistryValues("tile") == ("0", "1") &&
                WallpaperService.FitRegistryValues("crop") == ("10", "0") &&
                WallpaperService.NormalizeFit("unknown") == "fill");

            // Profiles and read-only system probes.
            var profiles = ProfileService.GetProfiles();
            Check("Профили доступны", profiles.Count == 7 && profiles.All(p => !string.IsNullOrWhiteSpace(p.Name)));

            var selective = services.Profiles.Plan("aurora", new ProfileApplyOptions
            {
                Theme = true,
                Wallpaper = false,
                Taskbar = false,
                Explorer = false,
                Icons = false
            });
            var fullPlan = services.Profiles.Plan("aurora");
            Check("Выборочное применение профиля", selective.Count == 1 && selective[0].Area == "Тема",
                $"разделов: {selective.Count}");
            Check("План профиля содержит все разделы", fullPlan.Count == 5,
                string.Join(", ", fullPlan.Select(item => item.Area)));

            var export = services.Profiles.ExportProfile("mint");
            Check("Экспорт профиля в JSON",
                export.Contains("wintheme-profile/v1", StringComparison.Ordinal) &&
                export.Contains("\"mint\"", StringComparison.Ordinal) &&
                export.Contains("\"plan\"", StringComparison.Ordinal));

            Check(
                "Разбор времени расписания",
                SchedulerService.TryParseTime("09:30", out var scheduled) &&
                scheduled.Hours == 9 && scheduled.Minutes == 30 &&
                SchedulerService.TryParseTime("23:59", out _) &&
                !SchedulerService.TryParseTime("25:00", out _) &&
                !SchedulerService.TryParseTime("abc", out _));

            var accent = ThemeService.ToHex(services.Theme.GetAccentColor());
            Check("Чтение темы Windows", accent.Length == 7 && accent[0] == '#', "акцент " + accent.ToUpperInvariant());

            Check("Чтение автозапуска", services.AutoStart.IsEnabled() || !services.AutoStart.IsEnabled());

            Check("Журнал приложения доступен", services.Journal.ActiveTransactionCount >= 0);
        }
        catch (Exception ex)
        {
            failures++;
            lines.Add("[FAIL] Исключение: " + ex);
            Logger.Error("Самопроверка завершилась исключением", ex);
        }
        finally
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestKeyPath, false);
            }
            catch
            {
                // best effort cleanup
            }

            try
            {
                if (sampleDirectory is not null && Directory.Exists(sampleDirectory))
                {
                    Directory.Delete(sampleDirectory, true);
                }

                if (sampleBackupDirectory is not null && Directory.Exists(sampleBackupDirectory))
                {
                    Directory.Delete(sampleBackupDirectory, true);
                }

                if (retryDirectory is not null && Directory.Exists(retryDirectory))
                {
                    Directory.Delete(retryDirectory, true);
                }

                if (retryBackupDirectory is not null && Directory.Exists(retryBackupDirectory))
                {
                    Directory.Delete(retryBackupDirectory, true);
                }

                if (testShortcutPath is not null && File.Exists(testShortcutPath))
                {
                    File.Delete(testShortcutPath);
                }

                if (shortcutBaselinePath is not null && File.Exists(shortcutBaselinePath))
                {
                    File.Delete(shortcutBaselinePath);
                }

                foreach (var id in iconTransactionIds)
                {
                    var backupDirectory = Path.Combine(AppPaths.FileBackupsDir, id);
                    if (Directory.Exists(backupDirectory))
                    {
                        Directory.Delete(backupDirectory, true);
                    }
                }
            }
            catch
            {
                // best effort cleanup
            }

            // The self-test must not leave anything in the real journal.
            try
            {
                if (journalBytes is not null)
                {
                    File.WriteAllBytes(journalFile, journalBytes);
                }
                else if (File.Exists(journalFile))
                {
                    File.Delete(journalFile);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }

        lines.Add(string.Empty);
        lines.Add(failures == 0
            ? "ИТОГ: все проверки пройдены."
            : $"ИТОГ: проверок с ошибкой — {failures}.");

        var report = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        var reportPath = Path.Combine(directory, "selftest.txt");
        File.WriteAllText(reportPath, report);
        Logger.Info(failures == 0
            ? "Самопроверка пройдена."
            : $"Самопроверка завершена с ошибками: {failures}.");
        return failures == 0 ? 0 : 3;
    }
}
