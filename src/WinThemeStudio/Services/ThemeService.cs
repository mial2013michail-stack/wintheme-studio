using Microsoft.Win32;
using System.Windows.Media;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string ExplorerAccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";

    private readonly ChangeJournal _journal;

    public ThemeService(ChangeJournal journal)
    {
        _journal = journal;
    }

    public bool IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    public string GetAccentHex()
    {
        var color = GetAccentColor();
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    public bool AreAnimationsEnabled()
    {
        try
        {
            using var metrics = Registry.CurrentUser.OpenSubKey(WindowMetricsKey, false);
            var minAnimate = metrics?.GetValue("MinAnimate") as string;
            if (minAnimate is not null)
            {
                return minAnimate != "0";
            }

            using var advanced = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", false);
            return advanced?.GetValue("TaskbarAnimations") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    public Color GetAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey, false);
            if (key?.GetValue("AccentColor") is int value)
            {
                var raw = unchecked((uint)value);
                var a = (byte)((raw >> 24) & 0xFF);
                var b = (byte)((raw >> 16) & 0xFF);
                var g = (byte)((raw >> 8) & 0xFF);
                var r = (byte)(raw & 0xFF);
                if (a == 0)
                {
                    a = 255;
                }

                return Color.FromArgb(a, r, g, b);
            }
        }
        catch
        {
            // ignore and return the Windows default
        }

        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    public void SetTheme(JournalTransaction tx, bool dark)
    {
        _journal.SetHkcuValue(tx, PersonalizeKey, "AppsUseLightTheme", dark ? 0 : 1);
        _journal.SetHkcuValue(tx, PersonalizeKey, "SystemUsesLightTheme", dark ? 0 : 1);
        NotifyTheme();
    }

    public void SetAccent(JournalTransaction tx, string hex)
    {
        var color = ParseColor(hex);
        var packed = PackAbgr(color);
        _journal.SetHkcuValue(tx, DwmKey, "AccentColor", unchecked((int)packed));
        _journal.SetHkcuValue(tx, DwmKey, "AccentColorInactive", unchecked((int)packed));
        _journal.SetHkcuValue(tx, DwmKey, "ColorPrevalence", 1);
        _journal.SetHkcuValue(tx, ExplorerAccentKey, "AccentColorMenu", unchecked((int)packed));
        _journal.SetHkcuValue(tx, ExplorerAccentKey, "StartColorMenu", unchecked((int)packed));
        _journal.SetHkcuValue(tx, PersonalizeKey, "ColorPrevalence", 1);
        NotifyTheme();
    }

    public void SetTransparency(JournalTransaction tx, bool enabled)
    {
        _journal.SetHkcuValue(tx, PersonalizeKey, "EnableTransparency", enabled ? 1 : 0);
        NotifyTheme();
    }

    public void SetAnimations(JournalTransaction tx, bool enabled)
    {
        _journal.SetHkcuValue(tx, WindowMetricsKey, "MinAnimate", enabled ? "1" : "0");
        _journal.SetHkcuValue(tx, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations", enabled ? 1 : 0);
        Native.NotifySettingChange("WindowMetrics");
    }

    public static Color ParseColor(string hex)
    {
        var text = (hex ?? "").Trim().TrimStart('#');
        if (text.Length == 3)
        {
            text = new string(new[] { text[0], text[0], text[1], text[1], text[2], text[2] });
        }

        if (text.Length != 6 ||
            !byte.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            throw new FormatException("Цвет должен быть в формате #RRGGBB.");
        }

        return Color.FromRgb(r, g, b);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static uint PackAbgr(Color color)
    {
        return 0xFF000000u |
               ((uint)color.B << 16) |
               ((uint)color.G << 8) |
               color.R;
    }

    public static void NotifyTheme()
    {
        Native.NotifySettingChange("ImmersiveColorSet");
        Native.NotifySettingChange("WindowMetrics");
        Native.NotifySettingChange("TraySettings");
    }
}
