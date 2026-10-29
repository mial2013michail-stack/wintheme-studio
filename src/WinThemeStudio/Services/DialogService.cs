using System.Windows;
using Microsoft.Win32;
using WinThemeStudio.UI.Dialogs;

namespace WinThemeStudio.Services;

public sealed class DialogService
{
    public bool Confirm(
        Window? owner,
        string title,
        string message,
        string confirmText = "Продолжить",
        string? cancelText = "Отмена",
        bool danger = false)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText, danger);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true;
    }

    public void Info(Window? owner, string title, string message)
    {
        Confirm(owner, title, message, "Понятно", null);
    }

    public string? PickImage(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение для обоев",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickIcon(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите значок .ico",
            Filter = "Значки Windows (*.ico)|*.ico|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickExecutable(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите приложение или ярлык",
            Filter = "Приложения (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
