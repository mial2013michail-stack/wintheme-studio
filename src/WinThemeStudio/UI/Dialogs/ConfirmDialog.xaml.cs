using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinThemeStudio.UI.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(
        string title,
        string message,
        string confirmText = "Продолжить",
        string? cancelText = "Отмена",
        bool danger = false)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelText;
        }

        if (danger)
        {
            IconBox.Background = (Brush)FindResource("DangerSoftBrush");
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            ConfirmButton.Style = (Style)FindResource("DangerButton");
            ConfirmButton.Content = confirmText;
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
