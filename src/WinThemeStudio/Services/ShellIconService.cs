using Microsoft.Win32;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

/// <summary>
/// Changes the shell icons for every folder, the recycle bin and the drives
/// through the documented per-user Shell Icons key. Nothing outside HKCU is
/// touched and every value is journaled before it is written.
/// </summary>
public sealed class ShellIconService
{
    private const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    private static readonly string[] FolderValues = { "3", "4" };
    private static readonly string[] TrashEmptyValues = { "31" };
    private static readonly string[] TrashFullValues = { "32" };
    private static readonly string[] DriveValues = { "7", "8", "9" };
    private static readonly string[] AllValues = { "3", "4", "7", "8", "9", "31", "32" };

    private readonly ChangeJournal _journal;
    private readonly string _keyPath;

    public ShellIconService(ChangeJournal journal, string? keyPath = null)
    {
        _journal = journal;
        _keyPath = string.IsNullOrWhiteSpace(keyPath) ? DefaultKeyPath : keyPath;
    }

    public bool ArePackIconsApplied(string packDirectory)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, false);
            if (key is null)
            {
                return false;
            }

            return AllValues.Any(value =>
            {
                var text = key.GetValue(value) as string;
                return !string.IsNullOrWhiteSpace(text) &&
                       text.StartsWith(packDirectory, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch
        {
            return false;
        }
    }

    public void ApplyFolderAndTrash(JournalTransaction tx, string folderIcon, string trashIcon)
    {
        ApplyIcons(tx, folderIcon, trashIcon, trashIcon, null, includeDrives: false);
    }

    public void ApplyIcons(
        JournalTransaction tx,
        string folderIcon,
        string trashEmptyIcon,
        string? trashFullIcon = null,
        string? driveIcon = null,
        bool includeDrives = true)
    {
        foreach (var value in FolderValues)
        {
            _journal.SetHkcuValue(tx, _keyPath, value, folderIcon + ",0");
        }

        foreach (var value in TrashEmptyValues)
        {
            _journal.SetHkcuValue(tx, _keyPath, value, trashEmptyIcon + ",0");
        }

        if (!string.IsNullOrWhiteSpace(trashFullIcon))
        {
            foreach (var value in TrashFullValues)
            {
                _journal.SetHkcuValue(tx, _keyPath, value, trashFullIcon + ",0");
            }
        }

        if (includeDrives && !string.IsNullOrWhiteSpace(driveIcon))
        {
            foreach (var value in DriveValues)
            {
                _journal.SetHkcuValue(tx, _keyPath, value, driveIcon + ",0");
            }
        }
    }

    /// <summary>
    /// Removes only the values that still point into our own icon pack. Icons
    /// the user chose independently are never overwritten.
    /// </summary>
    public void RemoveOwned(JournalTransaction tx, string packDirectory)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, false);
        if (key is null)
        {
            return;
        }

        foreach (var value in AllValues)
        {
            var text = key.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(text) &&
                text.StartsWith(packDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _journal.DeleteHkcuValue(tx, _keyPath, value);
            }
        }
    }
}
