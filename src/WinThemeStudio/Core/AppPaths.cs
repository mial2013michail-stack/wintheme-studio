namespace WinThemeStudio.Core;

public static class AppPaths
{
    public static string LocalDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinThemeStudio");

    public static string ConfigFile => Path.Combine(LocalDir, "config.json");
    public static string JournalFile => Path.Combine(LocalDir, "journal.json");
    public static string LogsDir => Path.Combine(LocalDir, "logs");
    public static string BackupsDir => Path.Combine(LocalDir, "backups");
    public static string FileBackupsDir => Path.Combine(BackupsDir, "files");
    public static string IconBackupsDir => Path.Combine(BackupsDir, "icons");
    public static string WallpapersDir => Path.Combine(LocalDir, "wallpapers");
    public static string WallpaperThumbsDir => Path.Combine(WallpapersDir, "thumbs");
    public static string IconPackDir => Path.Combine(LocalDir, "icons", "pack");
    public static string TempDir => Path.Combine(LocalDir, "temp");
    public static string OriginalWallpaperBase => Path.Combine(LocalDir, "original-wallpaper");

    public static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string TaskbarPinnedDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    public static string StartMenuProgramsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");

    public static string UserProfileDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(FileBackupsDir);
        Directory.CreateDirectory(IconBackupsDir);
        Directory.CreateDirectory(WallpapersDir);
        Directory.CreateDirectory(WallpaperThumbsDir);
        Directory.CreateDirectory(IconPackDir);
        Directory.CreateDirectory(TempDir);
    }

    public static bool IsUnderUserProfile(string path)
    {
        return IsUnder(path, UserProfileDir);
    }

    /// <summary>
    /// Files the journal may touch. The desktop and the taskbar pins are user
    /// files even when the user redirected them to another drive, so they are
    /// allowed in addition to the profile directory.
    /// </summary>
    public static bool IsAllowedUserFile(string path)
    {
        return IsUnderUserProfile(path) ||
               IsUnder(path, DesktopDir) ||
               IsUnder(path, TaskbarPinnedDir) ||
               IsUnder(path, StartMenuProgramsDir);
    }

    private static bool IsUnder(string path, string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUnderLocalAppData(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(LocalDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
