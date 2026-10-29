using Microsoft.Win32;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinThemeStudio";

    private readonly ChangeJournal _journal;

    public AutoStartService(ChangeJournal journal)
    {
        _journal = journal;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(JournalTransaction tx, bool enabled)
    {
        if (enabled)
        {
            _journal.SetHkcuValue(tx, RunKey, ValueName, BuildCommand());
        }
        else
        {
            _journal.DeleteHkcuValue(tx, RunKey, ValueName);
        }
    }

    public void RefreshCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValue(ValueName) is not string || !IsEnabled())
            {
                return;
            }

            using var writeKey = Registry.CurrentUser.OpenSubKey(RunKey, true);
            writeKey?.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
        }
        catch
        {
            // best effort
        }
    }

    private static string BuildCommand()
    {
        var exe = Environment.ProcessPath ?? "";
        return $"\"{exe}\" --tray";
    }
}
