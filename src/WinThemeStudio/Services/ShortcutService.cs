using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public readonly record struct ShortcutApplyResult(int Applied, int Skipped, int Failed);

public sealed class ShortcutService
{
    private readonly ChangeJournal _journal;
    private readonly IconPackService _icons;

    public ShortcutService(ChangeJournal journal, IconPackService icons)
    {
        _journal = journal;
        _icons = icons;
    }

    public Task<IReadOnlyList<ShortcutItem>> LoadAsync(string? style = null)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ShortcutItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(LoadCore(style));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private IReadOnlyList<ShortcutItem> LoadCore(string? style)
    {
        var items = new List<ShortcutItem>();
            foreach (var (directory, locationKey) in new[]
                     {
                         (AppPaths.DesktopDir, "desktop"),
                         (AppPaths.TaskbarPinnedDir, "taskbar"),
                         (AppPaths.StartMenuProgramsDir, "startmenu")
                     })
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    var search = locationKey == "startmenu"
                        ? SearchOption.AllDirectories
                        : SearchOption.TopDirectoryOnly;
                    files = Directory.EnumerateFiles(directory, "*.lnk", search).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var path in files)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        var target = "";
                        var iconLocation = "";
                        try
                        {
                            using var link = ShellLinkHandle.Open(path);
                            target = link.GetTargetPath();
                            iconLocation = link.GetIconLocation(out _);
                        }
                        catch
                        {
                            // A broken shortcut is still listed so its icon can be fixed.
                        }

                        var category = IconPackService.GuessKey(target, name);
                        items.Add(new ShortcutItem
                        {
                            Path = path,
                            Name = name,
                            Target = target,
                            LocationKey = locationKey,
                            CategoryKey = category,
                            IsModified = !string.IsNullOrWhiteSpace(iconLocation) &&
                                         iconLocation.StartsWith(AppPaths.IconPackDir, StringComparison.OrdinalIgnoreCase),
                            Icon = LoadBitmap(_icons.GetPreviewPath(category, style)) ?? NativeMethods.GetFileIcon(path, 64)
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Не удалось прочитать ярлык {path}: {ex.Message}");
                    }
                }
            }

        return (IReadOnlyList<ShortcutItem>)items
            .OrderBy(i => i.LocationKey switch
            {
                "desktop" => 0,
                "taskbar" => 1,
                _ => 2
            })
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void ApplyIcon(JournalTransaction tx, ShortcutItem item, string iconKey, string? style = null)
    {
        ApplyIconCore(tx, item, iconKey, style);
        NativeMethods.RefreshShellIcons();
    }

    private bool ApplyIconCore(JournalTransaction tx, ShortcutItem item, string iconKey, string? style)
    {
        var iconPath = _icons.GetIconPath(iconKey, style);

        using var link = ShellLinkHandle.Open(item.Path);
        var current = link.GetIconLocation(out var currentIndex);
        if (string.Equals(current, iconPath, StringComparison.OrdinalIgnoreCase) && currentIndex == 0)
        {
            EnsureBaseline(item);
            item.CategoryKey = iconKey;
            item.IsModified = true;
            return false;
        }

        EnsureBaseline(item);
        _journal.BackupFile(tx, item.Path, $"Ярлык: {item.Name}");
        link.SetIconLocation(iconPath, 0);
        link.Save();

        item.CategoryKey = iconKey;
        item.IsModified = true;
        item.Icon = LoadBitmap(_icons.GetPreviewPath(iconKey, style)) ?? NativeMethods.GetFileIcon(item.Path, 64);
        return true;
    }

    public void RestoreOriginal(JournalTransaction tx, ShortcutItem item)
    {
        RestoreOriginalCore(tx, item);
        NativeMethods.RefreshShellIcons();
    }

    private void RestoreOriginalCore(JournalTransaction tx, ShortcutItem item)
    {
        var baseline = BaselinePath(item);
        if (!File.Exists(baseline))
        {
            throw new FileNotFoundException("Резервная копия ярлыка не найдена.", baseline);
        }

        _journal.BackupFile(tx, item.Path, $"Ярлык: {item.Name}");
        File.Copy(baseline, item.Path, true);

        item.CategoryKey = IconPackService.GuessKey(item.Target, item.Name);
        item.IsModified = false;
        item.Icon = NativeMethods.GetFileIcon(item.Path, 64) ?? LoadBitmap(_icons.GetPreviewPath(item.CategoryKey));
    }

    public ShortcutApplyResult ApplyToAll(JournalTransaction tx, IEnumerable<ShortcutItem> items, string? style = null)
    {
        var applied = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in items)
        {
            try
            {
                if (ApplyIconCore(tx, item, item.CategoryKey, style))
                {
                    applied++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error($"Не удалось изменить значок {item.Path}", ex);
            }
        }

        if (applied > 0)
        {
            NativeMethods.RefreshShellIcons();
        }

        return new ShortcutApplyResult(applied, skipped, failed);
    }

    public ShortcutApplyResult RestoreAll(JournalTransaction tx, IEnumerable<ShortcutItem> items)
    {
        var restored = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in items)
        {
            if (!item.IsModified)
            {
                skipped++;
                continue;
            }

            try
            {
                RestoreOriginalCore(tx, item);
                restored++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error($"Не удалось вернуть значок {item.Path}", ex);
            }
        }

        if (restored > 0)
        {
            NativeMethods.RefreshShellIcons();
        }

        return new ShortcutApplyResult(restored, skipped, failed);
    }

    private static void EnsureBaseline(ShortcutItem item)
    {
        var baseline = BaselinePath(item);
        if (File.Exists(baseline))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        if (File.Exists(item.Path))
        {
            File.Copy(item.Path, baseline, false);
        }
    }

    private static string BaselinePath(ShortcutItem item)
    {
        var directory = Path.Combine(AppPaths.IconBackupsDir, "originals", item.LocationKey);
        var fileName = Path.GetFileName(item.Path);
        if (string.Equals(item.LocationKey, "startmenu", StringComparison.OrdinalIgnoreCase))
        {
            // The Start menu has nested folders and repeated shortcut names.
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(item.Path.ToLowerInvariant())))[..8];
            fileName = Path.GetFileNameWithoutExtension(fileName) + "-" + hash + ".lnk";
        }

        return Path.Combine(directory, fileName);
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
