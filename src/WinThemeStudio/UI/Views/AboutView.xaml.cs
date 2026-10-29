using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WinThemeStudio.Core;
using WinThemeStudio.Services;

namespace WinThemeStudio.UI.Views;

public partial class AboutView : PageView
{
    private bool _updating;

    public AboutView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = "Версия " + (version?.ToString(3) ?? "1.0.0");
        DataPathText.Text = AppPaths.LocalDir;
    }

    public override void OnShown()
    {
        _updating = true;
        try
        {
            AutoStartToggle.IsChecked = Services.AutoStart.IsEnabled();
            CloseToTrayToggle.IsChecked = Services.Config.Current.CloseToTray;
        }
        finally
        {
            _updating = false;
        }
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = AutoStartToggle.IsChecked == true;
        var ok = await Host.ExecuteChangeAsync(
            enabled ? "Автозапуск включён" : "Автозапуск выключен",
            transaction =>
            {
                Services.AutoStart.SetEnabled(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled ? "WinTheme Studio будет запускаться в трее при входе в Windows." : "Автозапуск отключён.");
        if (ok)
        {
            Services.Config.Update(config => config.StartWithWindows = enabled);
        }

        OnShown();
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = CloseToTrayToggle.IsChecked == true;
        Services.Config.Update(config => config.CloseToTray = enabled);
        Host.ShowToast(enabled
            ? "Окно будет сворачиваться в трей. Выход — через меню значка."
            : "Кнопка закрытия теперь завершает приложение.");
    }

    private void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        if (!Host.Confirm("Создать точку восстановления Windows?",
                "Откроется запрос UAC. Windows создаст системную точку восстановления. Это необязательный шаг: WinTheme Studio и без него хранит собственные резервные копии.",
                "Продолжить"))
        {
            return;
        }

        if (Services.RestorePoints.RequestRestorePoint())
        {
            Host.ShowToast("Запрос на создание точки восстановления отправлен Windows.");
        }
        else
        {
            Host.ShowToast("Не удалось запустить создание точки восстановления.", true);
        }
    }

    private void SystemRestore_Click(object sender, RoutedEventArgs e)
    {
        Services.RestorePoints.OpenSystemRestore();
        Host.ShowToast("Открыто восстановление системы Windows.");
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalDir);
            Process.Start(new ProcessStartInfo(AppPaths.LocalDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть папку данных: " + ex.Message);
            Host.ShowToast("Не удалось открыть папку данных.", true);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            Process.Start(new ProcessStartInfo(AppPaths.LogsDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть журнал: " + ex.Message);
            Host.ShowToast("Не удалось открыть журнал.", true);
        }
    }
}
