using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI.Views;

namespace WinThemeStudio;

public partial class MainWindow : Window
{
    private static readonly (string Page, string Title, string Keywords)[] SearchIndex =
    {
        ("dashboard", "Обзор", "текущий профиль тема акцент обои состояние"),
        ("profiles", "Профили", "готовые образы предпросмотр выборочное применение расписание"),
        ("appearance", "Тема", "светлая тёмная тема оформление"),
        ("appearance", "Акцент", "цвет акцента взять с обоев hex"),
        ("appearance", "Обои", "обои изображение режим отображения заполнить вписать обрезать"),
        ("appearance", "Прозрачность и анимации", "эффекты прозрачность анимации"),
        ("explorer", "Панель задач", "центрирование поиск виджеты чат секунды"),
        ("explorer", "Проводник", "расширения скрытые файлы компактный режим этот компьютер"),
        ("explorer", "Значки рабочего стола", "рабочий стол ярлыки показать скрыть"),
        ("explorer", "Классическое контекстное меню", "меню windows 10 проводник"),
        ("explorer", "Перезапуск Проводника", "перезапуск оболочка панель задач"),
        ("icons", "Стиль иконок", "студия стиль оттенок скругление монохром"),
        ("icons", "Свои значки", "свой ico значок категория"),
        ("icons", "Папки, корзина и диски", "значки папок корзины дисков полная корзина"),
        ("icons", "Ярлыки", "иконки ярлыков рабочий стол панель задач пуск"),
        ("dock", "Док", "плавающая панель размер автоскрытие размытие увеличение"),
        ("restore", "Журнал изменений", "история поиск фильтр очистка отменённые"),
        ("restore", "Отменить всё", "полный откат восстановление резервные копии"),
        ("about", "О программе", "лицензия безопасность ограничения версия"),
        ("about", "Точка восстановления", "резервная копия система"),
        ("about", "Журнал приложения", "логи ошибки диагностика")
    };

    private readonly Dictionary<string, PageView> _pages = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<(string Page, string Title)> _searchMatches = new();
    private readonly AppServices _services;
    private readonly bool _headless;
    private TrayIconService? _tray;
    private bool _allowClose;
    private bool _initialized;
    private bool _busy;

    public MainWindow(AppServices services, bool headless = false)
    {
        InitializeComponent();
        _services = services;
        _headless = headless;
        VersionLabel.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

        _toastTimer.Tick += (_, _) => HideToast();
        Loaded += (_, _) =>
        {
            RefreshJournalState();
            if (NavDashboard.IsChecked != true)
            {
                NavDashboard.IsChecked = true;
            }
            else
            {
                Navigate("dashboard");
            }
        };

        SourceInitialized += (_, _) =>
        {
            NativeMethods.ApplyWindowChrome(this, true, true);
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                HwndSource.FromHwnd(handle)?.AddHook(ShowWindowHook);
            }
        };
        Closing += OnClosing;
        Application.Current.SessionEnding += (_, _) =>
        {
            _allowClose = true;
            DisposeTray();
        };

        _services.Journal.Changed += (_, _) => Dispatcher.BeginInvoke(RefreshJournalState);
        _services.Config.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshSchedulerState));
        if (!_headless)
        {
            CreateTray();
        }

        RefreshJournalState();
        UpdateSearchHint();
    }

    public AppServices Services => _services;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _services.Icons.EnsurePackAsync();
        await _services.Wallpaper.EnsureAllAsync();
        if (!_headless)
        {
            _services.Overlays.Refresh();
        }

        _services.Scheduler.ProfileApplied += (_, name) =>
            Dispatcher.BeginInvoke(new Action(() => ShowToast($"Ежедневный профиль «{name}» применён по расписанию.")));
        _services.Scheduler.Failed += (_, name) =>
            Dispatcher.BeginInvoke(new Action(() => ShowToast($"Не удалось применить профиль «{name}» по расписанию.", true)));
        RefreshSchedulerState();

        if (!_headless && _services.Config.Current.CloseToTray && !_services.Config.Current.TrayHintShown && _tray is not null)
        {
            _tray.ShowBalloon("WinTheme Studio", "Приложение работает в трее. Двойной клик по значку откроет окно.");
            _services.Config.Update(c => c.TrayHintShown = true);
        }
    }

    public void PrepareForTrayStartup()
    {
        if (_tray is null)
        {
            // Without a tray icon the application would be unreachable.
            ShowFromTray();
            return;
        }

        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        new WindowInteropHelper(this).EnsureHandle();
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void UndoLastFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (!TryBeginOperation())
            {
                return;
            }

            try
            {
                if (_services.Journal.ActiveTransactionCount == 0)
                {
                    ShowToast("В журнале нет изменений для отмены.");
                    return;
                }

                if (!Confirm("Отменить последнее изменение?",
                        "Будет восстановлено предыдущее значение параметров и резервные копии ярлыков. Это действие безопасно.",
                        "Отменить изменение"))
                {
                    return;
                }

                var count = _services.Journal.UndoLast();
                var failed = _services.Journal.LastUndoFailures;
                RefreshJournalState();
                RefreshCurrentPage();
                if (failed > 0)
                {
                    ShowToast("Не удалось полностью отменить изменение. Журнал оставлен активным для повторной попытки.", true);
                }
                else
                {
                    ShowToast(count > 0 ? "Последнее изменение отменено." : "Не удалось отменить изменение.", count == 0);
                }
            }
            finally
            {
                EndOperation();
            }
        });
    }

    public void UndoAllFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (!TryBeginOperation())
            {
                return;
            }

            try
            {
                if (_services.Journal.ActiveTransactionCount == 0)
                {
                    ShowToast("В журнале нет изменений для отмены.");
                    return;
                }

                if (!Confirm("Отменить все изменения?",
                        "Будет восстановлено исходное состояние темы, акцента, обоев, Проводника и значков ярлыков. Это самый полный откат.",
                        "Отменить всё",
                        danger: true))
                {
                    return;
                }

                var undone = _services.Journal.UndoAll();
                var failed = _services.Journal.LastUndoFailures;
                RefreshJournalState();
                RefreshCurrentPage();
                ShowToast(failed > 0
                    ? $"Отменено действий: {undone}. Не удалось: {failed}. Журнал сохранён для повторной попытки."
                    : undone > 0 ? $"Отменено действий: {undone}." : "Отменять нечего.", failed > 0 || undone == 0);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось выполнить полный откат из трея", ex);
                ShowToast("Не удалось выполнить полный откат. Подробности в журнале.", true);
            }
            finally
            {
                EndOperation();
            }
        });
    }

    private async Task ApplyProfileFromTrayAsync(string key)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var profile = ProfileService.GetProfile(key);
            if (!Confirm("Применить профиль «" + profile.Name + "»?",
                    "Профиль изменит тему, акцент, обои, панель задач, Проводник и значки. Все исходные значения сохранятся в журнале.",
                    "Применить"))
            {
                return;
            }

            var transaction = await _services.Profiles.ApplyAsync(profile.Key);
            RefreshJournalState();
            RefreshCurrentPage();
            _tray?.SetActiveProfile(profile.Key);
            ShowToast($"Профиль «{profile.Name}» применён из трея. Изменений: {transaction.EntryCount}.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось применить профиль из трея: " + key, ex);
            ShowToast("Не удалось применить профиль из трея. Подробности в журнале.", true);
        }
        finally
        {
            EndOperation();
        }
    }

    public void RequestExit()
    {
        _allowClose = true;
        DisposeTray();
        _services.Scheduler.Dispose();
        _services.Overlays.Stop();
        Close();
        Application.Current.Shutdown();
    }

    public void DisposeTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private void CreateTray()
    {
        try
        {
            _tray = new TrayIconService(ProfileService.GetProfiles());
            _tray.ShowRequested += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
            _tray.UndoRequested += (_, _) => Dispatcher.BeginInvoke(new Action(UndoLastFromTray));
            _tray.UndoAllRequested += (_, _) => Dispatcher.BeginInvoke(new Action(UndoAllFromTray));
            _tray.ProfileRequested += (_, key) =>
                Dispatcher.BeginInvoke(new Action(() => _ = ApplyProfileFromTrayAsync(key)));
            _tray.ExitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(RequestExit));
            _tray.SetUndoAvailable(_services.Journal.ActiveTransactionCount > 0);
            _tray.SetActiveProfile(_services.Config.Current.LastAppliedProfile);
        }
        catch (Exception ex)
        {
            Logger.Warn("Значок трея недоступен: " + ex.Message);
        }
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null)
        {
            return;
        }

        if (sender is RadioButton { Tag: string key })
        {
            Navigate(key);
        }
    }

    public void Navigate(string key)
    {
        var (title, subtitle) = key switch
        {
            "dashboard" => ("Обзор", "Состояние оформления и быстрые действия"),
            "profiles" => ("Профили", "Семь готовых образов: от Windows 11 до Midnight и Sunset"),
            "appearance" => ("Внешний вид", "Тема, акцент, обои и прозрачность"),
            "explorer" => ("Проводник и панель задач", "Поведение Windows без патчей системных файлов"),
            "icons" => ("Иконки", "18 иконок, 8 стилей и значки папок, корзины и дисков"),
            "dock" => ("Док", "Плавающий док поверх рабочего стола"),
            "restore" => ("Восстановление", "Резервные копии и откат любых изменений"),
            "about" => ("О программе", "Безопасность, лицензии и ограничения"),
            _ => ("WinTheme Studio", "")
        };

        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = CreatePage(key);
            _pages[key] = page;
        }

        if (!ReferenceEquals(PageHost.Content, page))
        {
            PageHost.Content = page;
            PageScroll.ScrollToTop();
            page.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        page.OnShown();

        foreach (var nav in new[] { NavDashboard, NavProfiles, NavAppearance, NavExplorer, NavIcons, NavDock, NavRestore, NavAbout })
        {
            if (string.Equals(nav.Tag as string, key, StringComparison.Ordinal) && nav.IsChecked != true)
            {
                nav.IsChecked = true;
            }
        }

        _tray?.SetActiveProfile(_services.Config.Current.LastAppliedProfile);
    }

    private PageView CreatePage(string key) => key switch
    {
        "profiles" => new ProfilesView(this, _services),
        "appearance" => new AppearanceView(this, _services),
        "explorer" => new ExplorerView(this, _services),
        "icons" => new IconsView(this, _services),
        "dock" => new DockView(this, _services),
        "restore" => new RestoreView(this, _services),
        "about" => new AboutView(this, _services),
        _ => new DashboardView(this, _services)
    };

    public void RefreshCurrentPage()
    {
        if (PageHost.Content is PageView page)
        {
            page.OnShown();
        }
    }

    public void RefreshJournalState()
    {
        try
        {
            var count = _services.Journal.ActiveTransactionCount;
            ChangesLabel.Text = count switch
            {
                0 => "Изменений нет",
                1 => "1 изменение в журнале",
                _ => $"{count} изменений в журнале"
            };
            UndoButton.IsEnabled = count > 0;
            _tray?.SetUndoAvailable(count > 0);
        }
        catch
        {
            // journal label is cosmetic
        }
    }

    private void RefreshSchedulerState()
    {
        try
        {
            if (_services.Config.Current.SchedulerEnabled)
            {
                _services.Scheduler.Start();
            }
            else
            {
                _services.Scheduler.Stop();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось обновить состояние расписания: " + ex.Message);
        }
    }

    private void UpdateSearchHint()
    {
        if (SearchHint is not null)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SettingsSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchHint();
        var query = SettingsSearch.Text.Trim();
        if (query.Length < 2)
        {
            SearchPopup.IsOpen = false;
            return;
        }

        _searchMatches.Clear();
        SearchResults.Children.Clear();

        foreach (var entry in SearchIndex)
        {
            if (!entry.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                !entry.Keywords.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            _searchMatches.Add((entry.Page, entry.Title));
            if (_searchMatches.Count >= 9)
            {
                break;
            }
        }

        if (_searchMatches.Count == 0)
        {
            SearchResults.Children.Add(new TextBlock
            {
                Text = "Ничего не найдено. Попробуйте «обои», «значки», «откат» или «меню».",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 9, 10, 9)
            });
        }
        else
        {
            foreach (var (page, title) in _searchMatches)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("GhostButton"),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 1, 0, 1),
                    Foreground = (Brush)FindResource("TextBrush"),
                    Tag = page
                };

                var content = new StackPanel();
                content.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold
                });
                content.Children.Add(new TextBlock
                {
                    Text = PageDisplayName(page),
                    Style = (Style)FindResource("Caption"),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                button.Content = content;
                button.Click += (_, _) =>
                {
                    Navigate(page);
                    SearchPopup.IsOpen = false;
                };
                SearchResults.Children.Add(button);
            }
        }

        SearchPopup.IsOpen = true;
    }

    private void SettingsSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            SearchPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter && _searchMatches.Count > 0)
        {
            Navigate(_searchMatches[0].Page);
            SearchPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private static string PageDisplayName(string key) => key switch
    {
        "dashboard" => "Обзор",
        "profiles" => "Профили",
        "appearance" => "Внешний вид",
        "explorer" => "Проводник",
        "icons" => "Иконки",
        "dock" => "Док",
        "restore" => "Восстановление",
        "about" => "О программе",
        _ => key
    };

    public bool Confirm(string title, string message, string confirmText = "Применить", bool danger = false)
    {
        return _services.Dialogs.Confirm(this, title, message, confirmText, "Отмена", danger);
    }

    public bool TryBeginOperation()
    {
        if (_busy)
        {
            ShowToast("Дождитесь завершения текущей операции.", true);
            return false;
        }

        _busy = true;
        return true;
    }

    public void EndOperation()
    {
        _busy = false;
    }

    public async Task<bool> ExecuteChangeAsync(
        string title,
        Func<JournalTransaction, Task> apply,
        string successMessage,
        string? confirmMessage = null,
        bool danger = false)
    {
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            if (confirmMessage is not null && !Confirm(title, confirmMessage, "Продолжить", danger))
            {
                return false;
            }

            var tx = _services.Journal.Begin(title);
            tx.PreviousConfig = UndoConfigState.Capture(_services.Config.Current);
            try
            {
                await apply(tx);
                _services.Journal.Commit(tx);
                RefreshJournalState();
                ShowToast(successMessage);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось применить изменение: " + title, ex);
                if (tx.Entries.Count > 0)
                {
                    _services.Journal.Commit(tx);
                }

                RefreshJournalState();
                ShowToast("Не удалось применить изменение. Подробности в журнале.", true);
                return false;
            }
        }
        finally
        {
            EndOperation();
        }
    }

    public void ShowToast(string message, bool error = false)
    {
        if (!IsVisible && _tray is not null)
        {
            _tray.ShowBalloon(error ? "WinTheme Studio — ошибка" : "WinTheme Studio", message);
            return;
        }

        ToastText.Text = message;
        Toast.BorderBrush = error
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("StrokeStrongBrush");
        ToastDot.Fill = error
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("SuccessBrush");
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        var button = sender as Button;
        try
        {
            if (!Confirm("Перезапустить Проводник?",
                    "Все открытые окна Проводника закроются. Панель задач и рабочий стол появятся снова через несколько секунд.",
                    "Перезапустить"))
            {
                return;
            }

            if (button is not null)
            {
                button.IsEnabled = false;
            }

            await Task.Run(() => _services.Explorer.RestartExplorer());
            _services.Overlays.Refresh();
            ShowToast("Проводник перезапущен.");
        }
        catch (FullscreenAppActiveException ex)
        {
            ShowToast(ex.Message, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось перезапустить Проводник", ex);
            ShowToast("Не удалось перезапустить Проводник.", true);
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }

            EndOperation();
        }
    }

    private void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            var count = _services.Journal.ActiveTransactionCount;
            if (count == 0)
            {
                ShowToast("В журнале нет изменений для отмены.");
                return;
            }

            if (!Confirm("Отменить все изменения?",
                    $"Будет восстановлено исходное состояние для {count} записей журнала: тема, акцент, обои, проводник и значки ярлыков. " +
                    "Резервные копии удаляются только после успешного отката.",
                    "Отменить всё",
                    danger: true))
            {
                return;
            }

            try
            {
                var undone = _services.Journal.UndoAll();
                var failed = _services.Journal.LastUndoFailures;
                RefreshJournalState();
                RefreshCurrentPage();
                if (failed > 0)
                {
                    ShowToast($"Отменено действий: {undone}. Не удалось: {failed}. Журнал сохранён для повторной попытки.", true);
                }
                else
                {
                    ShowToast(undone > 0 ? $"Отменено изменений: {undone}." : "Отменять нечего.", undone == 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось отменить все изменения", ex);
                ShowToast("Не удалось выполнить откат. Подробности в журнале.", true);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !_headless && _tray is not null && _services.Config.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            ShowToast("WinTheme Studio свёрнут в трей.");
            return;
        }

        _allowClose = true;
        DisposeTray();
        _services.Scheduler.Dispose();
        _services.Overlays.Stop();
    }

    private IntPtr ShowWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == NativeMethods.ShowWindowMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(ShowFromTray));
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _toastTimer.Stop();
        DisposeTray();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
