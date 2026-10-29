using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinThemeStudio.Core;
using WinThemeStudio.Models;
using WinThemeStudio.Services;

namespace WinThemeStudio.UI.Views;

public partial class RestoreView : PageView
{
    private bool _updating;

    public RestoreView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        var active = Services.Journal.ActiveTransactionCount;
        ChangesCount.Text = active.ToString();
        EntriesCount.Text = Services.Journal.ActiveEntryCount.ToString();
        BackupSize.Text = FormatBytes(Services.Journal.GetBackupSizeBytes());
        UndoLastButton.IsEnabled = active > 0;
        UndoAllButton.IsEnabled = active > 0;

        _updating = true;
        try
        {
            if (JournalFilter.SelectedIndex < 0)
            {
                JournalFilter.SelectedIndex = 0;
            }
        }
        finally
        {
            _updating = false;
        }

        BuildHistory();
    }

    private void BuildHistory()
    {
        if (JournalSearch is null || JournalFilter is null ||
            JournalListPanel is null || HistoryCount is null)
        {
            return;
        }

        JournalListPanel.Children.Clear();
        var filter = (JournalFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var search = JournalSearch.Text.Trim();
        var history = Services.Journal.Snapshot()
            .Where(transaction => filter switch
            {
                "active" => transaction.IsActive,
                "undone" => !transaction.IsActive,
                _ => true
            })
            .Where(transaction => search.Length == 0 ||
                                  transaction.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                  transaction.Entries.Any(entry =>
                                      (entry.Label ?? "").Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                      entry.TargetDisplay.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        HistoryCount.Text = history.Count == 0
            ? "ничего не найдено"
            : $"{history.Count} {PluralActions(history.Count)}";

        if (history.Count == 0)
        {
            JournalListPanel.Children.Add(new TextBlock
            {
                Text = search.Length > 0 || filter != "all"
                    ? "По выбранному фильтру ничего не найдено. Измените запрос или фильтр."
                    : "Журнал пуст. Примените профиль или измените оформление — здесь появится история действий.",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 8)
            });
            return;
        }

        foreach (var transaction in history.Take(50))
        {
            JournalListPanel.Children.Add(CreateHistoryRow(transaction));
        }
    }

    private void JournalSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (JournalSearchHint is not null)
        {
            JournalSearchHint.Visibility = string.IsNullOrEmpty(JournalSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (!_updating)
        {
            BuildHistory();
        }
    }

    private void JournalFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
        {
            BuildHistory();
        }
    }

    private void CleanupUndone_Click(object sender, RoutedEventArgs e)
    {
        var undone = Services.Journal.Snapshot().Count(transaction => !transaction.IsActive);
        if (undone == 0)
        {
            Host.ShowToast("В журнале нет отменённых действий для очистки.");
            return;
        }

        if (!Host.Confirm("Очистить отменённые действия?",
                $"Будет удалено записей: {undone} и их резервные копии. Активные изменения и возможность отмены сохранятся.",
                "Очистить",
                danger: true))
        {
            return;
        }

        try
        {
            var removed = Services.Journal.CleanupUndone();
            Host.RefreshJournalState();
            RefreshAll();
            Host.ShowToast($"Удалено отменённых действий: {removed}.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось очистить журнал", ex);
            Host.ShowToast("Не удалось очистить отменённые действия.", true);
        }
    }

    private Border CreateHistoryRow(JournalTransaction transaction)
    {
        var title = new TextBlock
        {
            Text = transaction.Title,
            Style = (Style)FindResource("SettingTitle"),
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var details = new TextBlock
        {
            Text = $"{transaction.CreatedDisplay} · {transaction.EntryCount} " +
                   PluralParameters(transaction.EntryCount),
            Style = (Style)FindResource("Caption"),
            Margin = new Thickness(0, 3, 0, 0)
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(details);

        var active = transaction.IsActive;
        var badge = new Border
        {
            Style = (Style)FindResource("Pill"),
            Background = active ? (Brush)FindResource("SuccessSoftBrush") : new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderBrush = active ? new SolidColorBrush(Color.FromArgb(0x30, 0x45, 0xD6, 0xA0)) : (Brush)FindResource("StrokeBrush"),
            Padding = new Thickness(9, 3, 9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Child = new TextBlock
            {
                Text = transaction.StatusDisplay,
                FontSize = 11,
                Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(0x9B, 0xE9, 0xCB))
                    : (Brush)FindResource("DimBrush")
            }
        };

        var detailsButton = new Button
        {
            Content = "Подробности",
            Style = (Style)FindResource("LinkButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var button = new Button
        {
            Content = "Отменить",
            Style = (Style)FindResource("SmallButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            IsEnabled = active,
            Tag = transaction
        };
        button.Click += UndoOne_Click;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        Grid.SetColumn(detailsButton, 2);
        grid.Children.Add(detailsButton);
        Grid.SetColumn(button, 3);
        grid.Children.Add(button);

        var detailsPanel = CreateDetailsPanel(transaction);
        detailsButton.Click += (_, _) =>
        {
            detailsPanel.Visibility = detailsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            detailsButton.Content = detailsPanel.Visibility == Visibility.Visible ? "Скрыть" : "Подробности";
        };

        var layout = new StackPanel();
        layout.Children.Add(grid);
        layout.Children.Add(detailsPanel);

        return new Border
        {
            Style = (Style)FindResource("SettingRow"),
            Background = new SolidColorBrush(Color.FromArgb(active ? (byte)0x0A : (byte)0x05, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(13, 10, 10, 10),
            Opacity = active ? 1 : 0.62,
            Child = layout
        };
    }

    private StackPanel CreateDetailsPanel(JournalTransaction transaction)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 9, 0, 2),
            Visibility = Visibility.Collapsed
        };

        if (transaction.Recovered)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Транзакция была восстановлена после аварийного завершения. Изменения записаны в журнал и доступны для отмены.",
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xC2, 0x6B)),
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        var entries = transaction.Entries
            .GroupBy(entry => entry.TargetDisplay, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .ToList();

        foreach (var entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = entry.TypeDisplay,
                Style = (Style)FindResource("Caption"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            var target = new TextBlock
            {
                Text = entry.TargetDisplay,
                Style = (Style)FindResource("Caption"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(target, 1);
            row.Children.Add(target);

            var value = new TextBlock
            {
                Text = DescribeEntry(entry),
                Style = (Style)FindResource("Caption"),
                FontSize = 11,
                Foreground = (Brush)FindResource("DimBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);

            panel.Children.Add(row);
        }

        if (transaction.Entries.Count > entries.Count)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"…и ещё {transaction.Entries.Count - entries.Count} записей в журнале.",
                Style = (Style)FindResource("Caption"),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        return panel;
    }

    private static string DescribeEntry(ChangeEntry entry)
    {
        return entry.Type switch
        {
            ChangeEntryType.Key => "раздел реестра",
            ChangeEntryType.File => entry.BackupPath is null ? "без резервной копии" : "есть резервная копия",
            _ => entry.Existed
                ? Truncate(entry.Value ?? "(пусто)", 46)
                : "не существовало"
        };
    }

    private static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "…";

    private void UndoOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: JournalTransaction transaction })
        {
            return;
        }

        if (!Host.TryBeginOperation())
        {
            return;
        }

        try
        {
            if (!Host.Confirm("Отменить действие?",
                    $"«{transaction.Title}» будет отменено, восстановятся предыдущие значения {transaction.EntryCount} параметров и резервные копии ярлыков.",
                    "Отменить",
                    danger: true))
            {
                return;
            }

            try
            {
                var undone = Services.Journal.Undo(transaction);
                var failed = Services.Journal.LastUndoFailures;
                Host.RefreshJournalState();
                RefreshAll();
                if (failed > 0)
                {
                    Host.ShowToast("Не удалось полностью отменить действие. Журнал сохранён для повторной попытки.", true);
                }
                else
                {
                    Host.ShowToast(undone ? "Действие отменено." : "Это действие уже отменено.", !undone);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось отменить действие", ex);
                Host.ShowToast("Не удалось отменить действие. Подробности в журнале.", true);
            }
        }
        finally
        {
            Host.EndOperation();
        }
    }

    private void UndoLast_Click(object sender, RoutedEventArgs e)
    {
        if (!Host.TryBeginOperation())
        {
            return;
        }

        try
        {
            if (Services.Journal.ActiveTransactionCount == 0)
            {
                Host.ShowToast("В журнале нет изменений для отмены.");
                return;
            }

            if (!Host.Confirm("Отменить последнее действие?",
                    "Будет восстановлено предыдущее состояние последнего изменения WinTheme Studio.",
                    "Отменить",
                    danger: true))
            {
                return;
            }

            try
            {
                var undone = Services.Journal.UndoLast();
                var failed = Services.Journal.LastUndoFailures;
                Host.RefreshJournalState();
                RefreshAll();
                if (failed > 0)
                {
                    Host.ShowToast("Не удалось отменить последнее действие. Журнал сохранён для повторной попытки.", true);
                }
                else
                {
                    Host.ShowToast(undone > 0 ? "Последнее действие отменено." : "Отменять нечего.", undone == 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось отменить последнее действие", ex);
                Host.ShowToast("Не удалось выполнить откат. Подробности в журнале.", true);
            }
        }
        finally
        {
            Host.EndOperation();
        }
    }

    private void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        if (!Host.TryBeginOperation())
        {
            return;
        }

        try
        {
            var active = Services.Journal.ActiveTransactionCount;
            if (active == 0)
            {
                Host.ShowToast("В журнале нет изменений для отмены.");
                return;
            }

            if (!Host.Confirm("Отменить все изменения?",
                    $"Будет восстановлено исходное состояние для {active} действий: тема, акцент, обои, Проводник и значки ярлыков. Это самый полный откат.",
                    "Отменить всё",
                    danger: true))
            {
                return;
            }

            try
            {
                var undone = Services.Journal.UndoAll();
                var failed = Services.Journal.LastUndoFailures;
                Host.RefreshJournalState();
                RefreshAll();
                if (failed > 0)
                {
                    Host.ShowToast($"Отменено действий: {undone}. Не удалось: {failed}. Журнал сохранён для повторной попытки.", true);
                }
                else
                {
                    Host.ShowToast(undone > 0 ? $"Отменено действий: {undone}." : "Отменять нечего.", undone == 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось отменить все изменения", ex);
                Host.ShowToast("Не удалось выполнить полный откат. Подробности в журнале.", true);
            }
        }
        finally
        {
            Host.EndOperation();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshAll();
        Host.ShowToast("Журнал обновлён.");
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            Process.Start(new ProcessStartInfo(AppPaths.BackupsDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть папку резервных копий: " + ex.Message);
            Host.ShowToast("Не удалось открыть папку резервных копий.", true);
        }
    }

    private static string PluralActions(int count)
    {
        var tail = count % 100;
        var last = count % 10;
        return tail is >= 11 and <= 14
            ? "действий"
            : last switch
            {
                1 => "действие",
                2 or 3 or 4 => "действия",
                _ => "действий"
            };
    }

    private static string PluralParameters(int count)
    {
        var tail = count % 100;
        var last = count % 10;
        return tail is >= 11 and <= 14
            ? "параметров"
            : last switch
            {
                1 => "параметр",
                2 or 3 or 4 => "параметра",
                _ => "параметров"
            };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " Б";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.#") + " КБ";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.#") + " МБ";
        }

        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " ГБ";
    }
}
