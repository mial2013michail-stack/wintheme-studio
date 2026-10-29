using System.Text;

namespace WinThemeStudio.Core;

public static class Logger
{
    private static readonly object Gate = new();

    public static string CurrentLogFile { get; private set; } =
        Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize()
    {
        AppPaths.EnsureCreated();
        CurrentLogFile = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Write("ERROR", text);
    }

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(CurrentLogFile, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never take down the application.
        }
    }
}
