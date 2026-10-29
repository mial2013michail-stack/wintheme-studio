using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class ExplorerService
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string ClassicMenuKey =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    private readonly ChangeJournal _journal;

    public ExplorerService(ChangeJournal journal)
    {
        _journal = journal;
    }

    public bool IsTaskbarCentered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("TaskbarAl") is not int value || value != 0;
    }

    public bool AreFileExtensionsHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("HideFileExt") is not int value || value != 0;
    }

    public bool AreHiddenFilesShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("Hidden") is int value && value == 1;
    }

    public bool IsCompactMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("UseCompactMode") is int value && value == 1;
    }

    public bool IsFullPathShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("FullPath") is int value && value == 1;
    }

    public bool IsThisPcStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("LaunchTo") is int value && value == 1;
    }

    public bool AreSecondsShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("ShowSecondsInSystemClock") is int value && value == 1;
    }

    public bool AreDesktopIconsShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("HideIcons") is not int value || value == 0;
    }

    public bool IsClassicContextMenu()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassicMenuKey, false);
        return key is not null;
    }

    public bool AreWidgetsShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("TaskbarDa") is not int value || value != 0;
    }

    public bool IsChatShown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey, false);
        return key?.GetValue("TaskbarMn") is int value && value == 1;
    }

    public int GetSearchMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SearchKey, false);
        return key?.GetValue("SearchboxTaskbarMode") is int value ? Math.Clamp(value, 0, 3) : 2;
    }

    public void SetTaskbarCentered(JournalTransaction tx, bool centered)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "TaskbarAl", centered ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetDesktopIconsShown(JournalTransaction tx, bool shown)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "HideIcons", shown ? 0 : 1);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetClassicContextMenu(JournalTransaction tx, bool enabled)
    {
        if (enabled)
        {
            _journal.SetHkcuValue(tx, ClassicMenuKey, "", "");
        }
        else
        {
            _journal.DeleteHkcuKey(tx, ClassicMenuKey);
        }

        Native.NotifySettingChange("TraySettings");
    }

    public void SetFileExtensionsHidden(JournalTransaction tx, bool hidden)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "HideFileExt", hidden ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetHiddenFilesShown(JournalTransaction tx, bool shown)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "Hidden", shown ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetCompactMode(JournalTransaction tx, bool enabled)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "UseCompactMode", enabled ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetFullPathShown(JournalTransaction tx, bool shown)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "FullPath", shown ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetThisPcStart(JournalTransaction tx, bool enabled)
    {
        // 1 opens This PC, 2 opens the Home/Quick access view.
        _journal.SetHkcuValue(tx, AdvancedKey, "LaunchTo", enabled ? 1 : 2);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetSecondsShown(JournalTransaction tx, bool shown)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "ShowSecondsInSystemClock", shown ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public bool SetWidgetsShown(JournalTransaction tx, bool shown)
    {
        try
        {
            _journal.SetHkcuValue(tx, AdvancedKey, "TaskbarDa", shown ? 1 : 0);
            Native.NotifySettingChange("TraySettings");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            // Some Windows 11 builds protect TaskbarDa. The rest of a profile
            // must still apply, so this single value is allowed to fail.
            Logger.Warn("Windows не разрешает изменить кнопку виджетов: " + ex.Message);
            return false;
        }
    }

    public void SetChatShown(JournalTransaction tx, bool shown)
    {
        _journal.SetHkcuValue(tx, AdvancedKey, "TaskbarMn", shown ? 1 : 0);
        Native.NotifySettingChange("TraySettings");
    }

    public void SetSearchMode(JournalTransaction tx, int mode)
    {
        _journal.SetHkcuValue(tx, SearchKey, "SearchboxTaskbarMode", Math.Clamp(mode, 0, 3));
        Native.NotifySettingChange("TraySettings");
    }

    public bool IsFullscreenForeground() => NativeMethods.IsForegroundFullscreen(IntPtr.Zero);

    public void RestartExplorer(bool force = false)
    {
        if (!force && NativeMethods.IsForegroundFullscreen(IntPtr.Zero))
        {
            throw new FullscreenAppActiveException(
                "Обнаружено полноэкранное приложение. Перезапуск Проводника отложен, чтобы не прервать игру или видео.");
        }

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // ignore a process we cannot stop
            }
        }

        Thread.Sleep(1200);

        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
            catch
            {
                // Windows usually restarts the shell on its own
            }
        }

        // Give the shell a moment to recreate the taskbar before the caller
        // refreshes overlays or shell icons.
        for (var i = 0; i < 20; i++)
        {
            if (NativeMethods.IsShellTaskbarPresent())
            {
                return;
            }

            Thread.Sleep(150);
        }
    }
}

public sealed class FullscreenAppActiveException : InvalidOperationException
{
    public FullscreenAppActiveException(string message) : base(message)
    {
    }
}
