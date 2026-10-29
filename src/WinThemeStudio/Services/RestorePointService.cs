using System.Diagnostics;
using WinThemeStudio.Core;

namespace WinThemeStudio.Services;

public sealed class RestorePointService
{
    public bool RequestRestorePoint()
    {
        try
        {
            var script =
                "Checkpoint-Computer -Description 'WinTheme Studio' -RestorePointType 'MODIFY_SETTINGS'";
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(info);
            Logger.Info("Запрошено создание точки восстановления Windows.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn("Точка восстановления не создана: " + ex.Message);
            return false;
        }
    }

    public void OpenSystemRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть восстановление системы: " + ex.Message);
        }
    }

    public void OpenProtectionSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть защиту системы: " + ex.Message);
        }
    }
}
