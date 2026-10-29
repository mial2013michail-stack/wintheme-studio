using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinThemeStudio.Core;

public static class JsonStore
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback();
            }

            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception ex)
        {
            Logger.Error($"Не удалось прочитать {path}", ex);
            TryPreserveCorruptFile(path);
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(value, Options);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Не удалось сохранить {path}", ex);
        }
    }

    private static void TryPreserveCorruptFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var backup = path + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(path, backup, true);
                Logger.Warn($"Повреждённый файл сохранён как {backup}");
            }
        }
        catch
        {
            // ignore
        }
    }
}
