using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

/// <summary>
/// Records every HKCU value and user-profile file it touches, so the whole
/// customization can be reverted without touching system files.
/// </summary>
public sealed class ChangeJournal
{
    private readonly object _gate = new();
    private readonly List<JournalTransaction> _transactions;

    public event EventHandler? Changed;

    /// <summary>
    /// Raised after a transaction was fully restored. Lets the application
    /// restore profile/wallpaper/overlay settings that live outside the registry.
    /// </summary>
    public event EventHandler<JournalTransaction>? TransactionUndone;

    /// <summary>
    /// Number of transactions that could not be undone during the last undo
    /// operation. Their journal entries stay active so the user can retry.
    /// </summary>
    public int LastUndoFailures { get; private set; }

    public ChangeJournal()
    {
        var document = JsonStore.Load(AppPaths.JournalFile, () => new JournalDocument());
        _transactions = document.Transactions ?? new List<JournalTransaction>();
        foreach (var transaction in _transactions)
        {
            transaction.Entries ??= new List<ChangeEntry>();
        }

        // A crash in the middle of a transaction leaves a partially written
        // journal entry. Treat it as a completed (and therefore undoable)
        // transaction instead of silently ignoring it on the next start.
        var recovered = _transactions
            .Where(t => !t.Completed && t.Entries.Count > 0)
            .ToList();
        if (recovered.Count > 0)
        {
            foreach (var transaction in recovered)
            {
                transaction.Completed = true;
                transaction.Recovered = true;
            }

            PersistLocked();
            Logger.Warn($"Восстановлено незавершённых транзакций журнала: {recovered.Count}.");
        }
    }

    public JournalTransaction Begin(string title) => new() { Title = title };

    public bool SetHkcuValue(JournalTransaction tx, string keyPath, string? valueName, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentNullException.ThrowIfNull(value);

        var name = valueName ?? string.Empty;
        var kind = value switch
        {
            int => RegistryValueKind.DWord,
            long => RegistryValueKind.QWord,
            string => RegistryValueKind.String,
            byte[] => RegistryValueKind.Binary,
            string[] => RegistryValueKind.MultiString,
            _ => throw new ArgumentException("Неподдерживаемый тип значения реестра.", nameof(value))
        };

        lock (_gate)
        {
            using var readKey = Registry.CurrentUser.OpenSubKey(keyPath, false);
            var currentExists = readKey is not null && ValueExists(readKey, name);
            var current = currentExists
                ? readKey!.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                : null;

            if (currentExists && ValuesEqual(current, value))
            {
                return false;
            }

            var entryAdded = false;
            if (tx.Entries.Any(e => e.Type == ChangeEntryType.Registry &&
                                   string.Equals(e.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(e.ValueName ?? "", name, StringComparison.Ordinal)))
            {
                // The original value was already captured earlier in this transaction.
            }
            else
            {
                var originalKind = currentExists
                    ? readKey!.GetValueKind(name)
                    : kind;

                tx.Entries.Add(new ChangeEntry
                {
                    Type = ChangeEntryType.Registry,
                    KeyPath = keyPath,
                    ValueName = name,
                    Existed = currentExists,
                    Kind = originalKind,
                    Value = currentExists ? SerializeValue(current, originalKind) : null,
                    Label = keyPath + (name.Length > 0 ? "\\" + name : "")
                });
                entryAdded = true;
            }

            try
            {
                using var writeKey = Registry.CurrentUser.CreateSubKey(keyPath, true)
                    ?? throw new InvalidOperationException($"Не удалось открыть раздел {keyPath}.");
                writeKey.SetValue(name, value, kind);
            }
            catch
            {
                // Do not keep a journal entry for a write that never happened.
                if (entryAdded)
                {
                    tx.Entries.RemoveAt(tx.Entries.Count - 1);
                }

                throw;
            }

            Persist(tx);
            return true;
        }
    }

    public bool DeleteHkcuValue(JournalTransaction tx, string keyPath, string? valueName)
    {
        var name = valueName ?? string.Empty;
        lock (_gate)
        {
            using var readKey = Registry.CurrentUser.OpenSubKey(keyPath, false);
            if (readKey is null || !ValueExists(readKey, name))
            {
                return false;
            }

            if (!tx.Entries.Any(e => e.Type == ChangeEntryType.Registry &&
                                     string.Equals(e.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(e.ValueName ?? "", name, StringComparison.Ordinal)))
            {
                var originalKind = readKey.GetValueKind(name);
                tx.Entries.Add(new ChangeEntry
                {
                    Type = ChangeEntryType.Registry,
                    KeyPath = keyPath,
                    ValueName = name,
                    Existed = true,
                    Kind = originalKind,
                    Value = SerializeValue(readKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), originalKind),
                    Label = keyPath + (name.Length > 0 ? "\\" + name : "")
                });
            }

            using var writeKey = Registry.CurrentUser.OpenSubKey(keyPath, true);
            writeKey?.DeleteValue(name, false);
            Persist(tx);
            return true;
        }
    }

    /// <summary>
    /// Deletes a whole HKCU key while recording every value and subkey so undo
    /// can recreate the tree. Used for the classic context-menu toggle, which
    /// only takes effect when its CLSID key is absent.
    /// </summary>
    public bool DeleteHkcuKey(JournalTransaction tx, string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        lock (_gate)
        {
            using var readKey = Registry.CurrentUser.OpenSubKey(keyPath, false);
            if (readKey is null)
            {
                return false;
            }

            CaptureKeyTree(tx, keyPath, readKey);
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            Persist(tx);
            return true;
        }
    }

    private static void CaptureKeyTree(JournalTransaction tx, string keyPath, RegistryKey key)
    {
        foreach (var name in key.GetValueNames())
        {
            if (tx.Entries.Any(e => e.Type == ChangeEntryType.Registry &&
                                    string.Equals(e.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(e.ValueName ?? "", name, StringComparison.Ordinal)))
            {
                continue;
            }

            var kind = key.GetValueKind(name);
            tx.Entries.Add(new ChangeEntry
            {
                Type = ChangeEntryType.Registry,
                KeyPath = keyPath,
                ValueName = name,
                Existed = true,
                Kind = kind,
                Value = SerializeValue(
                    key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), kind),
                Label = keyPath + (name.Length > 0 ? "\\" + name : "")
            });
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName, false);
            if (subKey is not null)
            {
                CaptureKeyTree(tx, keyPath + "\\" + subKeyName, subKey);
            }
        }

        if (!tx.Entries.Any(e => e.Type == ChangeEntryType.Key &&
                                 string.Equals(e.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase)))
        {
            tx.Entries.Add(new ChangeEntry
            {
                Type = ChangeEntryType.Key,
                KeyPath = keyPath,
                Existed = true,
                Label = keyPath
            });
        }
    }

    public bool BackupFile(JournalTransaction tx, string filePath, string? label = null)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        if (!AppPaths.IsAllowedUserFile(filePath))
        {
            throw new InvalidOperationException("Резервирование файлов вне разрешённых пользовательских папок запрещено.");
        }

        lock (_gate)
        {
            if (tx.Entries.Any(e => e.Type == ChangeEntryType.File &&
                                   string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var directory = Path.Combine(AppPaths.FileBackupsDir, tx.Id);
            Directory.CreateDirectory(directory);
            var safeName = MakeSafeFileName(Path.GetFileName(filePath));
            var backupPath = Path.Combine(directory, $"{tx.Entries.Count:D3}-{safeName}");
            File.Copy(filePath, backupPath, true);

            tx.Entries.Add(new ChangeEntry
            {
                Type = ChangeEntryType.File,
                FilePath = filePath,
                BackupPath = backupPath,
                Label = label ?? Path.GetFileName(filePath)
            });

            Persist(tx);
            return true;
        }
    }

    public void Commit(JournalTransaction tx)
    {
        lock (_gate)
        {
            if (tx.Entries.Count == 0)
            {
                return;
            }

            tx.Completed = true;
            if (!_transactions.Any(t => string.Equals(t.Id, tx.Id, StringComparison.Ordinal)))
            {
                _transactions.Add(tx);
            }

            PersistLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<JournalTransaction> Snapshot()
    {
        lock (_gate)
        {
            return _transactions
                .OrderByDescending(t => t.CreatedUtc)
                .Select(CloneTransaction)
                .ToList();
        }
    }

    public IReadOnlyList<JournalTransaction> ActiveTransactions()
    {
        lock (_gate)
        {
            return _transactions
                .Where(t => t.IsActive && t.Entries.Count > 0)
                .OrderByDescending(t => t.CreatedUtc)
                .Select(CloneTransaction)
                .ToList();
        }
    }

    public int ActiveTransactionCount
    {
        get
        {
            lock (_gate)
            {
                return _transactions.Count(t => t.IsActive && t.Entries.Count > 0);
            }
        }
    }

    public int ActiveEntryCount
    {
        get
        {
            lock (_gate)
            {
                return _transactions.Where(t => t.IsActive).Sum(t => t.Entries.Count);
            }
        }
    }

    public long GetBackupSizeBytes()
    {
        try
        {
            if (!Directory.Exists(AppPaths.BackupsDir))
            {
                return 0;
            }

            return Directory.EnumerateFiles(AppPaths.BackupsDir, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    public bool Undo(JournalTransaction transaction)
    {
        UndoOutcome outcome;
        JournalTransaction? undoneTransaction = null;

        lock (_gate)
        {
            var live = _transactions.FirstOrDefault(t => string.Equals(t.Id, transaction.Id, StringComparison.Ordinal));
            if (live is null || !live.IsActive)
            {
                LastUndoFailures = 0;
                return false;
            }

            outcome = UndoCore(live);
            if (outcome.Undone)
            {
                undoneTransaction = CloneTransaction(live);
            }

            PersistLocked();
        }

        LastUndoFailures = outcome.Undone ? 0 : 1;
        if (outcome.Undone)
        {
            NotifyAfterUndo(outcome.Wallpaper, outcome.Shortcuts);
            TransactionUndone?.Invoke(this, undoneTransaction!);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (outcome.ChangedSystem)
        {
            // Restore notifications for the entries that did succeed, but keep
            // the transaction active for a retry.
            NotifyAfterUndo(outcome.Wallpaper, outcome.Shortcuts);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return outcome.Undone;
    }

    public int UndoLast()
    {
        JournalTransaction? live;
        lock (_gate)
        {
            live = _transactions.Where(t => t.IsActive && t.Entries.Count > 0)
                .OrderByDescending(t => t.CreatedUtc)
                .FirstOrDefault();
        }

        return live is not null && Undo(live) ? 1 : 0;
    }

    public int UndoAll()
    {
        var count = 0;
        var failures = 0;
        var wallpaper = false;
        var shortcuts = false;
        var changedSystem = false;
        var undoneTransactions = new List<JournalTransaction>();

        lock (_gate)
        {
            var active = _transactions.Where(t => t.IsActive && t.Entries.Count > 0)
                .OrderByDescending(t => t.CreatedUtc)
                .ToList();

            foreach (var transaction in active)
            {
                var result = UndoCore(transaction);
                if (result.Undone)
                {
                    count++;
                    wallpaper |= result.Wallpaper;
                    shortcuts |= result.Shortcuts;
                    changedSystem = true;
                    undoneTransactions.Add(CloneTransaction(transaction));
                }
                else
                {
                    failures++;
                    wallpaper |= result.Wallpaper;
                    shortcuts |= result.Shortcuts;
                    changedSystem |= result.ChangedSystem;
                }
            }

            if (count > 0)
            {
                PersistLocked();
            }
        }

        LastUndoFailures = failures;
        if (changedSystem)
        {
            NotifyAfterUndo(wallpaper, shortcuts);
        }

        if (count > 0)
        {
            foreach (var transaction in undoneTransactions)
            {
                TransactionUndone?.Invoke(this, transaction);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (changedSystem)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return count;
    }

    private UndoOutcome UndoCore(JournalTransaction tx)
    {
        var wallpaper = false;
        var shortcuts = false;
        var failures = 0;
        var changedSystem = false;

        foreach (var entry in tx.Entries.AsEnumerable().Reverse())
        {
            if (entry.Type == ChangeEntryType.Registry && entry.KeyPath is not null)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(entry.KeyPath, true);
                    if (key is null)
                    {
                        failures++;
                        Logger.Error($"Не удалось открыть раздел реестра для отмены: {entry.KeyPath}");
                        continue;
                    }

                    var name = entry.ValueName ?? string.Empty;
                    if (entry.Existed)
                    {
                        var value = DeserializeValue(entry.Value, entry.Kind);
                        key.SetValue(name, value, entry.Kind);
                    }
                    else
                    {
                        key.DeleteValue(name, false);
                    }

                    changedSystem = true;
                    if (entry.KeyPath.Equals(@"Control Panel\Desktop", StringComparison.OrdinalIgnoreCase) &&
                        (name.Equals("Wallpaper", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("WallpaperStyle", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("TileWallpaper", StringComparison.OrdinalIgnoreCase)))
                    {
                        wallpaper = true;
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Logger.Error($"Не удалось отменить значение реестра {entry.KeyPath}\\{entry.ValueName}", ex);
                }
            }
            else if (entry.Type == ChangeEntryType.Key && entry.KeyPath is not null)
            {
                try
                {
                    if (entry.Existed)
                    {
                        Registry.CurrentUser.CreateSubKey(entry.KeyPath, true);
                    }
                    else
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(entry.KeyPath, false);
                    }

                    changedSystem = true;
                }
                catch (Exception ex)
                {
                    failures++;
                    Logger.Error($"Не удалось восстановить раздел реестра {entry.KeyPath}", ex);
                }
            }
            else if (entry.Type == ChangeEntryType.File && entry.FilePath is not null && entry.BackupPath is not null)
            {
                try
                {
                    if (!File.Exists(entry.BackupPath))
                    {
                        failures++;
                        Logger.Error($"Резервная копия для отмены не найдена: {entry.BackupPath}");
                        continue;
                    }

                    if (!AppPaths.IsAllowedUserFile(entry.FilePath))
                    {
                        failures++;
                        Logger.Error($"Восстановление файла вне разрешённых пользовательских папок запрещено: {entry.FilePath}");
                        continue;
                    }

                    var directory = Path.GetDirectoryName(entry.FilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.Copy(entry.BackupPath, entry.FilePath, true);
                    changedSystem = true;
                    if (entry.FilePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        shortcuts = true;
                    }

                    if (entry.FilePath.EndsWith("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase))
                    {
                        wallpaper = true;
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Logger.Error($"Не удалось восстановить файл {entry.FilePath}", ex);
                }
            }
            else
            {
                failures++;
                Logger.Error("В журнале найдена запись без достаточных данных для отмены.");
            }
        }

        if (failures > 0)
        {
            // Deliberately leave UndoneUtc unset so the transaction remains
            // active and the user can retry after fixing the problem.
            return new UndoOutcome(false, wallpaper, shortcuts, failures, changedSystem);
        }

        tx.UndoneUtc = DateTime.UtcNow;
        tx.Completed = true;
        return new UndoOutcome(true, wallpaper, shortcuts, 0, changedSystem);
    }

    private readonly record struct UndoOutcome(
        bool Undone,
        bool Wallpaper,
        bool Shortcuts,
        int Failures,
        bool ChangedSystem);

    private static void NotifyAfterUndo(bool wallpaper, bool shortcuts)
    {
        Native.NotifySettingChange("ImmersiveColorSet");
        Native.NotifySettingChange("WindowMetrics");
        Native.NotifySettingChange("TraySettings");
        if (wallpaper)
        {
            Native.RefreshWallpaperFromRegistry();
        }

        if (shortcuts)
        {
            Native.RefreshShellIcons();
        }
    }

    private void Persist(JournalTransaction tx)
    {
        if (tx.Entries.Count == 0)
        {
            return;
        }

        if (!_transactions.Any(t => string.Equals(t.Id, tx.Id, StringComparison.Ordinal)))
        {
            _transactions.Add(tx);
        }

        PersistLocked();
    }

    private void PersistLocked()
    {
        Trim();
        JsonStore.Save(AppPaths.JournalFile, new JournalDocument { Transactions = _transactions });
    }

    /// <summary>
    /// Removes already undone history and its file backups. Active entries are
    /// never touched.
    /// </summary>
    public int CleanupUndone()
    {
        var removed = 0;
        lock (_gate)
        {
            var candidates = _transactions.Where(t => !t.IsActive).ToList();
            foreach (var transaction in candidates)
            {
                _transactions.Remove(transaction);
                DeleteBackups(transaction);
                removed++;
            }

            if (removed > 0)
            {
                PersistLocked();
            }
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private void Trim()
    {
        const int maxTransactions = 200;
        if (_transactions.Count <= maxTransactions)
        {
            return;
        }

        var removable = _transactions
            .Where(t => !t.IsActive)
            .OrderBy(t => t.CreatedUtc)
            .Take(_transactions.Count - maxTransactions)
            .ToList();

        foreach (var transaction in removable)
        {
            _transactions.Remove(transaction);
            DeleteBackups(transaction);
        }
    }

    private static void DeleteBackups(JournalTransaction transaction)
    {
        try
        {
            var directory = Path.Combine(AppPaths.FileBackupsDir, transaction.Id);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Не удалось удалить резервные копии транзакции {transaction.Id}: {ex.Message}");
        }
    }

    private static JournalTransaction CloneTransaction(JournalTransaction source)
    {
        return new JournalTransaction
        {
            Id = source.Id,
            Title = source.Title,
            CreatedUtc = source.CreatedUtc,
            Completed = source.Completed,
            UndoneUtc = source.UndoneUtc,
            Recovered = source.Recovered,
            PreviousConfig = source.PreviousConfig?.Clone(),
            Entries = source.Entries.Select(e => new ChangeEntry
            {
                Type = e.Type,
                KeyPath = e.KeyPath,
                ValueName = e.ValueName,
                Existed = e.Existed,
                Kind = e.Kind,
                Value = e.Value,
                FilePath = e.FilePath,
                BackupPath = e.BackupPath,
                Label = e.Label
            }).ToList()
        };
    }

    private static bool ValueExists(RegistryKey key, string name)
    {
        try
        {
            key.GetValueKind(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValuesEqual(object? current, object target)
    {
        if (current is null)
        {
            return false;
        }

        return current switch
        {
            int i when target is int ti => i == ti,
            long l when target is long tl => l == tl,
            string s when target is string ts => string.Equals(s, ts, StringComparison.Ordinal),
            byte[] b when target is byte[] tb => b.AsSpan().SequenceEqual(tb),
            string[] a when target is string[] ta => a.SequenceEqual(ta, StringComparer.Ordinal),
            _ => current.Equals(target)
        };
    }

    private static string SerializeValue(object? value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.ToBase64String((byte[])value!),
            RegistryValueKind.MultiString => string.Join("\n", (string[])value!),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static object DeserializeValue(string? value, RegistryValueKind kind)
    {
        var text = value ?? string.Empty;
        return kind switch
        {
            RegistryValueKind.DWord => int.Parse(text, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(text, CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.FromBase64String(text),
            RegistryValueKind.MultiString => text.Split('\n'),
            _ => text
        };
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var safe = builder.ToString();
        if (safe.Length > 80)
        {
            using var sha = SHA1.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(name))).Substring(0, 10);
            safe = safe[..60] + "-" + hash + Path.GetExtension(name);
        }

        return safe;
    }
}
