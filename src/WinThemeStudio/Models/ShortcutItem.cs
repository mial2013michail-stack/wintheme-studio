using System.Windows.Media.Imaging;

namespace WinThemeStudio.Models;

public sealed class ShortcutItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string LocationKey { get; set; } = "desktop";
    public string CategoryKey { get; set; } = "app";
    public bool IsModified { get; set; }
    public BitmapSource? Icon { get; set; }

    public string TargetDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Target))
            {
                return "Ярлык";
            }

            var file = System.IO.Path.GetFileName(Target);
            return string.IsNullOrWhiteSpace(file) ? Target : file;
        }
    }
}
