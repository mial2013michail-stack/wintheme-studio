using System.Windows;
using System.Windows.Controls;
using WinThemeStudio.Core;
using WinThemeStudio.Services;

namespace WinThemeStudio.UI.Views;

public partial class ExplorerView : PageView
{
    private bool _updating;

    public ExplorerView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        _updating = true;
        try
        {
            TaskbarCenterToggle.IsChecked = Services.Explorer.IsTaskbarCentered();
            SecondsToggle.IsChecked = Services.Explorer.AreSecondsShown();
            WidgetsToggle.IsChecked = Services.Explorer.AreWidgetsShown();
            ChatToggle.IsChecked = Services.Explorer.IsChatShown();
            HideExtensionsToggle.IsChecked = Services.Explorer.AreFileExtensionsHidden();
            HiddenFilesToggle.IsChecked = Services.Explorer.AreHiddenFilesShown();
            CompactModeToggle.IsChecked = Services.Explorer.IsCompactMode();
            ThisPcToggle.IsChecked = Services.Explorer.IsThisPcStart();
            FullPathToggle.IsChecked = Services.Explorer.IsFullPathShown();
            DesktopIconsToggle.IsChecked = Services.Explorer.AreDesktopIconsShown();
            ClassicMenuToggle.IsChecked = Services.Explorer.IsClassicContextMenu();

            var search = Services.Explorer.GetSearchMode();
            SearchHidden.IsChecked = search == 0;
            SearchIcon.IsChecked = search == 1;
            SearchBox.IsChecked = search == 2;
            SearchBoxLabel.IsChecked = search == 3;
        }
        finally
        {
            _updating = false;
        }
    }

    private async void TaskbarCenter_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var centered = TaskbarCenterToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            centered ? "Панель задач по центру" : "Панель задач слева",
            transaction =>
            {
                Services.Explorer.SetTaskbarCentered(transaction, centered);
                return Task.CompletedTask;
            },
            centered ? "Значки панели задач выровнены по центру." : "Значки панели задач выровнены слева.");
        ShowRestartHint();
        OnShown();
    }

    private async void Seconds_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = SecondsToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            shown ? "Секунды в часах" : "Секунды скрыты",
            transaction =>
            {
                Services.Explorer.SetSecondsShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "Секунды в системных часах включены." : "Секунды в системных часах выключены.");
        ShowRestartHint();
        OnShown();
    }

    private async void Widgets_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = WidgetsToggle.IsChecked == true;
        var applied = true;
        await Host.ExecuteChangeAsync(
            shown ? "Виджеты включены" : "Виджеты выключены",
            transaction =>
            {
                applied = Services.Explorer.SetWidgetsShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "Кнопка виджетов включена." : "Кнопка виджетов выключена.");
        if (!applied)
        {
            Host.ShowToast("Windows не разрешила изменить кнопку виджетов на этом компьютере. Остальные настройки не затронуты.", true);
        }

        ShowRestartHint();
        OnShown();
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = ChatToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            shown ? "Чат включён" : "Чат выключен",
            transaction =>
            {
                Services.Explorer.SetChatShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "Кнопка чата включена." : "Кнопка чата выключена.");
        ShowRestartHint();
        OnShown();
    }

    private async void Search_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not RadioButton { IsChecked: true, Tag: string tag } ||
            !int.TryParse(tag, out var mode))
        {
            return;
        }

        await Host.ExecuteChangeAsync(
            "Режим поиска на панели задач",
            transaction =>
            {
                Services.Explorer.SetSearchMode(transaction, mode);
                return Task.CompletedTask;
            },
            "Режим кнопки поиска изменён.");
        ShowRestartHint();
        OnShown();
    }

    private async void HideExtensions_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var hidden = HideExtensionsToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            hidden ? "Расширения скрыты" : "Расширения показаны",
            transaction =>
            {
                Services.Explorer.SetFileExtensionsHidden(transaction, hidden);
                return Task.CompletedTask;
            },
            hidden ? "Расширения файлов скрыты." : "Расширения файлов теперь видны.");
        OnShown();
    }

    private async void HiddenFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = HiddenFilesToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            shown ? "Скрытые файлы показаны" : "Скрытые файлы скрыты",
            transaction =>
            {
                Services.Explorer.SetHiddenFilesShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "Скрытые файлы теперь видны в Проводнике." : "Скрытые файлы снова скрыты.");
        OnShown();
    }

    private async void CompactMode_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = CompactModeToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            enabled ? "Компактный режим включён" : "Компактный режим выключен",
            transaction =>
            {
                Services.Explorer.SetCompactMode(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled ? "Компактный режим Проводника включён." : "Компактный режим Проводника выключен.");
        OnShown();
    }

    private async void ThisPc_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = ThisPcToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            enabled ? "Проводник открывает «Этот компьютер»" : "Проводник открывает «Главную»",
            transaction =>
            {
                Services.Explorer.SetThisPcStart(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled ? "Проводник будет открываться с «Этот компьютер»." : "Проводник будет открываться с «Главной».");
        OnShown();
    }

    private async void FullPath_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = FullPathToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            shown ? "Полный путь в заголовке" : "Короткое имя в заголовке",
            transaction =>
            {
                Services.Explorer.SetFullPathShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "В заголовке окна будет полный путь." : "В заголовке окна будет только имя папки.");
        OnShown();
    }

    private async void DesktopIcons_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var shown = DesktopIconsToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            shown ? "Значки рабочего стола показаны" : "Значки рабочего стола скрыты",
            transaction =>
            {
                Services.Explorer.SetDesktopIconsShown(transaction, shown);
                return Task.CompletedTask;
            },
            shown ? "Значки рабочего стола возвращены." : "Значки рабочего стола скрыты.");
        OnShown();
    }

    private async void ClassicMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = ClassicMenuToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            enabled ? "Классическое контекстное меню" : "Современное контекстное меню",
            transaction =>
            {
                Services.Explorer.SetClassicContextMenu(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled
                ? "Классическое меню включено. Оно появится после перезапуска Проводника."
                : "Современное меню Windows 11 возвращено.");
        OnShown();
    }

    private void ShowRestartHint()
    {
        RestartHint.Visibility = Visibility.Visible;
    }

    private void RestartHint_Click(object sender, RoutedEventArgs e)
    {
        _ = RestartShellAsync(sender as Button, ask: false);
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        await RestartShellAsync(sender as Button, ask: true);
    }

    private async Task RestartShellAsync(Button? button, bool ask)
    {
        if (ask && !Host.Confirm("Перезапустить Проводник?",
                "Все открытые окна Проводника закроются. Панель задач и рабочий стол появятся снова через несколько секунд.",
                "Перезапустить"))
        {
            return;
        }

        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            await Task.Run(() => Services.Explorer.RestartExplorer());
            RestartHint.Visibility = Visibility.Collapsed;
            Host.ShowToast("Проводник перезапущен.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось перезапустить Проводник", ex);
            Host.ShowToast("Не удалось перезапустить Проводник.", true);
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }
}
