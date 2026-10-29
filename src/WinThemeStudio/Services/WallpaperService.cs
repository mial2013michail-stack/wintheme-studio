using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class WallpaperService
{
    private const int FullWidth = 2560;
    private const int FullHeight = 1440;
    private const int ThumbWidth = 480;
    private const int ThumbHeight = 270;
    private const string DesktopKey = @"Control Panel\Desktop";

    private readonly ChangeJournal _journal;
    private readonly ConfigService _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WallpaperService(ChangeJournal journal, ConfigService config)
    {
        _journal = journal;
        _config = config;
    }

    public static IReadOnlyList<WallpaperPreset> GetPresets() => new List<WallpaperPreset>
    {
        new()
        {
            Key = "aurora",
            Name = "Aurora",
            Description = "Тёмно-синие волны с холодным свечением",
            IsLight = false
        },
        new()
        {
            Key = "aubergine",
            Name = "Aubergine",
            Description = "Глубокий фиолетовый с тёплым оранжевым акцентом",
            IsLight = false
        },
        new()
        {
            Key = "bloom",
            Name = "Bloom",
            Description = "Светлая абстрактная композиция из мягких лепестков",
            IsLight = true
        },
        new()
        {
            Key = "graphite",
            Name = "Graphite",
            Description = "Минималистичный графит с тонкими контурами",
            IsLight = false
        },
        new()
        {
            Key = "frost",
            Name = "Frost",
            Description = "Ледяные кристаллы на холодном фоне",
            IsLight = false
        },
        new()
        {
            Key = "midnight",
            Name = "Midnight",
            Description = "Глубокий синий с золотыми искрами",
            IsLight = false
        },
        new()
        {
            Key = "mint",
            Name = "Mint",
            Description = "Светлая мята с мягкими листьями",
            IsLight = true
        },
        new()
        {
            Key = "sunset",
            Name = "Sunset",
            Description = "Тёплый закат с оранжевыми полосами",
            IsLight = false
        }
    };

    public static string NormalizeFit(string? fit) => fit?.Trim().ToLowerInvariant() switch
    {
        "fit" => "fit",
        "stretch" => "stretch",
        "center" => "center",
        "tile" => "tile",
        "crop" => "crop",
        _ => "fill"
    };

    public static string FitDisplayName(string? fit) => NormalizeFit(fit) switch
    {
        "fit" => "Вписать",
        "stretch" => "Растянуть",
        "center" => "По центру",
        "tile" => "Замостить",
        "crop" => "Обрезать под экран",
        _ => "Заполнить"
    };

    public static (string Style, string Tile) FitRegistryValues(string? fit) => NormalizeFit(fit) switch
    {
        "fit" => ("6", "0"),
        "stretch" => ("2", "0"),
        "center" => ("0", "0"),
        "tile" => ("0", "1"),
        // Crop is pre-rendered to the screen aspect ratio, so Windows should
        // fill the result without adding letterboxing.
        "crop" => ("10", "0"),
        _ => ("10", "0")
    };

    public static WallpaperPreset GetPreset(string? key)
    {
        var presets = GetPresets();
        return presets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
               ?? presets[0];
    }

    public void CaptureOriginalIfNeeded()
    {
        var cfg = _config.Current;
        if (cfg.OriginalWallpaperCaptured)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey, false);
            var registryPath = key?.GetValue("Wallpaper") as string;
            var isOwnWallpaper = !string.IsNullOrWhiteSpace(registryPath) &&
                                 registryPath.StartsWith(AppPaths.WallpapersDir, StringComparison.OrdinalIgnoreCase);

            // If the current wallpaper is already one of ours and no earlier
            // snapshot exists, we cannot know the real original. Leave the flag
            // unset instead of recording a preset as the user's original.
            if (isOwnWallpaper)
            {
                Logger.Warn("Исходные обои не сохранены: текущие обои созданы приложением.");
                return;
            }

            var style = key?.GetValue("WallpaperStyle")?.ToString();
            var tile = key?.GetValue("TileWallpaper")?.ToString();

            cfg.OriginalWallpaperRegistryPath = string.IsNullOrWhiteSpace(registryPath) ? null : registryPath;
            cfg.OriginalWallpaperStyle = string.IsNullOrWhiteSpace(style) ? "10" : style;
            cfg.OriginalWallpaperTile = string.IsNullOrWhiteSpace(tile) ? "0" : tile;

            if (!string.IsNullOrWhiteSpace(registryPath) && File.Exists(registryPath))
            {
                var extension = Path.GetExtension(registryPath);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                {
                    extension = ".jpg";
                }

                var target = AppPaths.OriginalWallpaperBase + extension;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(registryPath, target, true);
                    cfg.OriginalWallpaperPath = target;
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось скопировать исходные обои, будет использован исходный путь: " + ex.Message);
                }
            }

            cfg.OriginalWallpaperCaptured = true;
            _config.Save();
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось сохранить исходные обои: " + ex.Message);
        }
    }

    public async Task<WallpaperPreset> EnsureWallpaperAsync(WallpaperPreset preset)
    {
        await _gate.WaitAsync();
        try
        {
            var full = Path.Combine(AppPaths.WallpapersDir, preset.Key + ".png");
            var thumb = Path.Combine(AppPaths.WallpaperThumbsDir, preset.Key + ".png");
            preset.FullPath = full;
            preset.ThumbnailPath = thumb;

            if (!File.Exists(full) || !File.Exists(thumb))
            {
                var key = preset.Key;
                await RunStaAsync(() => RenderPreset(key, full, thumb));
            }

            return preset;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WallpaperPreset>> EnsureAllAsync(IProgress<string>? progress = null)
    {
        var presets = GetPresets().ToList();
        foreach (var preset in presets)
        {
            progress?.Report($"Подготовка обоев «{preset.Name}»…");
            await EnsureWallpaperAsync(preset);
        }

        return presets;
    }

    public async Task ApplyAsync(JournalTransaction tx, WallpaperPreset preset, bool isCustom = false)
    {
        CaptureOriginalIfNeeded();
        var ensured = await EnsureWallpaperAsync(preset);
        ApplyPath(tx, ensured.FullPath, _config.Current.WallpaperFit);

        var cfg = _config.Current;
        cfg.LastWallpaperKey = preset.Key;
        cfg.LastWallpaperIsCustom = isCustom;
        _config.Save();
    }

    public void ApplyCustom(JournalTransaction tx, string sourcePath)
    {
        CaptureOriginalIfNeeded();
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Файл обоев не найден.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = ".png";
        }

        var target = Path.Combine(AppPaths.WallpapersDir, "custom" + extension);
        if (NormalizeFit(_config.Current.WallpaperFit) == "crop")
        {
            target = Path.Combine(AppPaths.WallpapersDir, "custom-crop.png");
            CropToScreen(sourcePath, target);
        }
        else
        {
            File.Copy(sourcePath, target, true);
        }

        ApplyPath(tx, target, _config.Current.WallpaperFit);
        Native.RefreshShellIcons();

        var cfg = _config.Current;
        cfg.LastWallpaperKey = "custom";
        cfg.LastWallpaperIsCustom = true;
        _config.Save();
    }

    public void SetFit(JournalTransaction tx, string fit)
    {
        var normalized = NormalizeFit(fit);
        var path = GetCurrentWallpaperPath();
        if (path is null)
        {
            throw new InvalidOperationException("Сначала выберите обои, затем режим отображения.");
        }

        if (normalized == "crop")
        {
            var cropped = Path.Combine(AppPaths.WallpapersDir, "current-crop.png");
            CropToScreen(path, cropped);
            ApplyPath(tx, cropped, normalized);
        }
        else
        {
            ApplyPath(tx, path, normalized);
        }

        _config.Update(c => c.WallpaperFit = normalized);
    }

    public void RestoreOriginal(JournalTransaction tx)
    {
        CaptureOriginalIfNeeded();

        var cfg = _config.Current;
        if (!cfg.OriginalWallpaperCaptured)
        {
            throw new InvalidOperationException(
                "Исходные обои не были сохранены, поэтому восстановление отменено. " +
                "Приложение не будет очищать текущие обои без резервной копии.");
        }

        var target = !string.IsNullOrWhiteSpace(cfg.OriginalWallpaperPath) && File.Exists(cfg.OriginalWallpaperPath)
            ? cfg.OriginalWallpaperPath
            : !string.IsNullOrWhiteSpace(cfg.OriginalWallpaperRegistryPath) &&
              File.Exists(cfg.OriginalWallpaperRegistryPath)
                ? cfg.OriginalWallpaperRegistryPath
                : null;

        if (target is null && !string.IsNullOrWhiteSpace(cfg.OriginalWallpaperRegistryPath))
        {
            throw new FileNotFoundException(
                "Файл исходных обоев не найден. Текущие обои оставлены без изменений.",
                cfg.OriginalWallpaperRegistryPath);
        }

        var style = string.IsNullOrWhiteSpace(cfg.OriginalWallpaperStyle) ? "10" : cfg.OriginalWallpaperStyle;
        var tile = string.IsNullOrWhiteSpace(cfg.OriginalWallpaperTile) ? "0" : cfg.OriginalWallpaperTile;

        _journal.SetHkcuValue(tx, DesktopKey, "WallpaperStyle", style);
        _journal.SetHkcuValue(tx, DesktopKey, "TileWallpaper", tile);

        if (target is not null)
        {
            _journal.SetHkcuValue(tx, DesktopKey, "Wallpaper", target);
            Native.SetWallpaper(target);
        }
        else
        {
            // The original desktop was a solid colour: remove our wallpaper
            // value instead of leaving a stale path behind.
            _journal.DeleteHkcuValue(tx, DesktopKey, "Wallpaper");
            Native.SetWallpaper(null);
        }

        cfg.LastWallpaperKey = "";
        cfg.LastWallpaperIsCustom = false;
        _config.Save();
    }

    public string? GetCurrentWallpaperPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey, false);
            var path = key?.GetValue("Wallpaper") as string;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyPath(JournalTransaction tx, string path, string? fit)
    {
        var (style, tile) = FitRegistryValues(fit);
        _journal.SetHkcuValue(tx, DesktopKey, "WallpaperStyle", style);
        _journal.SetHkcuValue(tx, DesktopKey, "TileWallpaper", tile);
        _journal.SetHkcuValue(tx, DesktopKey, "Wallpaper", path);
        Native.SetWallpaper(path);
    }

    /// <summary>
    /// Picks a vivid, reasonably bright colour from the image. Used by the
    /// "accent from wallpaper" action; no external imaging library is needed.
    /// </summary>
    public string ExtractAccent(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл обоев не найден.", path);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var step = Math.Max(1, Math.Min(converted.PixelWidth, converted.PixelHeight) / 220);
        var buckets = new Dictionary<int, (double Score, long R, long G, long B, int Count)>();
        for (var y = 0; y < converted.PixelHeight; y += step)
        {
            for (var x = 0; x < converted.PixelWidth; x += step)
            {
                var index = y * stride + x * 4;
                var b = pixels[index];
                var g = pixels[index + 1];
                var r = pixels[index + 2];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var value = max / 255.0;
                var saturation = max == 0 ? 0 : (max - min) / (double)max;
                if (value < 0.18 || value > 0.97 || saturation < 0.16)
                {
                    continue;
                }

                // Favour vivid mid-tones and group nearby colours so noise does
                // not win over the actual dominant accent.
                var score = saturation * (1.0 - Math.Abs(value - 0.58) * 0.9);
                var key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                if (buckets.TryGetValue(key, out var bucket))
                {
                    buckets[key] = (bucket.Score + score, bucket.R + r, bucket.G + g, bucket.B + b, bucket.Count + 1);
                }
                else
                {
                    buckets[key] = (score, r, g, b, 1);
                }
            }
        }

        if (buckets.Count == 0)
        {
            return "#0078D4";
        }

        var best = buckets.Values
            .OrderByDescending(bucket => bucket.Score)
            .First();
        var color = Color.FromRgb(
            (byte)(best.R / best.Count),
            (byte)(best.G / best.Count),
            (byte)(best.B / best.Count));

        // Keep the result usable as a Windows accent: not too dark, not neon.
        if (color.R + color.G + color.B < 150)
        {
            color = Color.FromRgb(
                (byte)Math.Min(255, color.R + 45),
                (byte)Math.Min(255, color.G + 45),
                (byte)Math.Min(255, color.B + 45));
        }

        return ThemeService.ToHex(color);
    }

    private static void CropToScreen(string sourcePath, string targetPath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(sourcePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        var targetWidth = Math.Max(1280, (int)Math.Round(SystemParameters.PrimaryScreenWidth));
        var targetHeight = Math.Max(720, (int)Math.Round(SystemParameters.PrimaryScreenHeight));
        var scale = Math.Max(targetWidth / (double)image.PixelWidth, targetHeight / (double)image.PixelHeight);
        var scaledWidth = Math.Max(targetWidth, (int)Math.Round(image.PixelWidth * scale));
        var scaledHeight = Math.Max(targetHeight, (int)Math.Round(image.PixelHeight * scale));
        var left = Math.Max(0, (scaledWidth - targetWidth) / 2);
        var top = Math.Max(0, (scaledHeight - targetHeight) / 2);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(-left, -top, scaledWidth, scaledHeight));
        }

        var bitmap = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(targetPath);
        encoder.Save(stream);
    }

    private static Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void RenderPreset(string key, string fullPath, string thumbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);

        if (!File.Exists(fullPath))
        {
            RenderToFile(key, FullWidth, FullHeight, fullPath);
        }

        if (!File.Exists(thumbPath))
        {
            RenderToFile(key, ThumbWidth, ThumbHeight, thumbPath);
        }
    }

    private static void RenderToFile(string key, int width, int height, string path)
    {
        var visual = new DrawingVisual();
        var scaleX = width / (double)FullWidth;
        var scaleY = height / (double)FullHeight;
        if (width != FullWidth || height != FullHeight)
        {
            visual.Transform = new ScaleTransform(scaleX, scaleY);
        }

        using (var dc = visual.RenderOpen())
        {
            DrawPreset(dc, key, FullWidth, FullHeight);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawPreset(DrawingContext dc, string key, double w, double h)
    {
        switch (key)
        {
            case "aubergine":
                DrawAubergine(dc, w, h);
                break;
            case "bloom":
                DrawBloom(dc, w, h);
                break;
            case "graphite":
                DrawGraphite(dc, w, h);
                break;
            case "frost":
                DrawFrost(dc, w, h);
                break;
            case "midnight":
                DrawMidnight(dc, w, h);
                break;
            case "mint":
                DrawMint(dc, w, h);
                break;
            case "sunset":
                DrawSunset(dc, w, h);
                break;
            default:
                DrawAurora(dc, w, h);
                break;
        }
    }

    private static void FillGradient(DrawingContext dc, double w, double h, Color top, Color bottom,
        double angle = 90)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(bottom, 1));
        brush.Freeze();
        dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
    }

    private static void DrawGlow(DrawingContext dc, Point center, double radius, Color color, double alpha)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.45 * 255), color.R, color.G, color.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private static void DrawVignette(DrawingContext dc, double w, double h, double strength = 0.45)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.48),
            GradientOrigin = new Point(0.5, 0.48),
            RadiusX = 0.75,
            RadiusY = 0.75,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(strength * 255), 0, 0, 0), 1));
        brush.Freeze();
        dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
    }

    private static void DrawAurora(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x05, 0x0B, 0x18), Color.FromRgb(0x08, 0x17, 0x2A));

        DrawGlow(dc, new Point(w * 0.18, h * 0.28), w * 0.45, Color.FromRgb(0x1D, 0x5F, 0xFF), 0.55);
        DrawGlow(dc, new Point(w * 0.78, h * 0.22), w * 0.40, Color.FromRgb(0x7A, 0x3C, 0xFF), 0.42);
        DrawGlow(dc, new Point(w * 0.56, h * 0.78), w * 0.52, Color.FromRgb(0x00, 0xB8, 0xC8), 0.35);

        var random = new Random(4117);
        for (var i = 0; i < 4; i++)
        {
            var y = h * (0.28 + i * 0.12);
            var color = i % 2 == 0 ? Color.FromRgb(0x58, 0xC8, 0xFF) : Color.FromRgb(0xA2, 0x7B, 0xFF);
            DrawRibbon(dc, w, h, y, 90 + i * 38, color, 0.11 + i * 0.015, random);
        }

        for (var i = 0; i < 220; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h * 0.78;
            var radius = random.NextDouble() * 1.6 + 0.4;
            var alpha = (byte)(random.NextDouble() * 130 + 30);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 220, 235, 255));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }

        DrawHorizonGlow(dc, w, h, Color.FromRgb(0x1B, 0x4C, 0x7A), 0.35);
        DrawVignette(dc, w, h, 0.55);
    }

    private static void DrawRibbon(DrawingContext dc, double w, double h, double y, double thickness, Color color,
        double alpha, Random random)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(-w * 0.1, y), false, false);
            ctx.BezierTo(
                new Point(w * 0.18, y - h * 0.13 * random.NextDouble()),
                new Point(w * 0.30, y + h * 0.16 * random.NextDouble()),
                new Point(w * 0.48, y + h * 0.02),
                true, false);
            ctx.BezierTo(
                new Point(w * 0.66, y - h * 0.12 * random.NextDouble()),
                new Point(w * 0.82, y + h * 0.12 * random.NextDouble()),
                new Point(w * 1.1, y - h * 0.03),
                true, false);
        }

        geometry.Freeze();

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.8 * 255), color.R, color.G, color.B), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();

        foreach (var factor in new[] { 1.0, 0.45, 0.18 })
        {
            var pen = new Pen(brush, thickness * factor)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private static void DrawHorizonGlow(DrawingContext dc, double w, double h, Color color, double alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B), 1));
        brush.Freeze();
        dc.DrawRectangle(brush, null, new Rect(0, h * 0.62, w, h * 0.38));
    }

    private static void DrawAubergine(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x14, 0x08, 0x12), Color.FromRgb(0x2A, 0x0E, 0x1A));

        DrawGlow(dc, new Point(w * 0.82, h * 0.76), w * 0.50, Color.FromRgb(0xE9, 0x54, 0x20), 0.50);
        DrawGlow(dc, new Point(w * 0.20, h * 0.22), w * 0.45, Color.FromRgb(0x77, 0x28, 0x6B), 0.48);
        DrawGlow(dc, new Point(w * 0.55, h * 0.45), w * 0.36, Color.FromRgb(0x3E, 0x14, 0x44), 0.40);

        var center = new Point(w * 0.78, h * 0.72);
        for (var i = 1; i <= 9; i++)
        {
            var radius = w * (0.08 + i * 0.045);
            var alpha = (byte)Math.Max(8, 46 - i * 4);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xE9, 0x54, 0x20)), 2.2);
            pen.Freeze();
            dc.DrawEllipse(null, pen, center, radius, radius * 0.78);
        }

        var random = new Random(90210);
        for (var i = 0; i < 40; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h;
            var length = random.NextDouble() * 160 + 30;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(random.NextDouble() * 26 + 6), 0xFF, 0xC0, 0x90)), 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, y), new Point(x + length, y - length * 0.35));
        }

        DrawVignette(dc, w, h, 0.52);
    }

    private static void DrawBloom(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0xF7, 0xFA, 0xFF), Color.FromRgb(0xD8, 0xE7, 0xF8));

        DrawGlow(dc, new Point(w * 0.30, h * 0.42), w * 0.48, Color.FromRgb(0xFF, 0xFF, 0xFF), 0.75);
        DrawGlow(dc, new Point(w * 0.82, h * 0.25), w * 0.36, Color.FromRgb(0x9C, 0xC8, 0xFF), 0.35);
        DrawGlow(dc, new Point(w * 0.62, h * 0.85), w * 0.38, Color.FromRgb(0xC9, 0xB6, 0xFF), 0.30);

        var cx = w * 0.40;
        var cy = h * 0.52;
        for (var i = 0; i < 9; i++)
        {
            var angle = i * 40.0;
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.2, 0.5),
                GradientOrigin = new Point(0.18, 0.5),
                RadiusX = 0.55,
                RadiusY = 0.62,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(210, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(145, 180, 214, 255), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(35, 120, 160, 235), 0.78));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 120, 160, 235), 1));
            brush.Freeze();

            dc.PushTransform(new RotateTransform(angle, cx, cy));
            dc.DrawEllipse(brush, null, new Point(cx + w * 0.13, cy), w * 0.16, h * 0.075);
            dc.Pop();
        }

        var inner = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        inner.GradientStops.Add(new GradientStop(Color.FromArgb(245, 255, 255, 255), 0));
        inner.GradientStops.Add(new GradientStop(Color.FromArgb(120, 220, 236, 255), 0.55));
        inner.GradientStops.Add(new GradientStop(Color.FromArgb(0, 220, 236, 255), 1));
        inner.Freeze();
        dc.DrawEllipse(inner, null, new Point(cx, cy), w * 0.11, w * 0.11);

        var random = new Random(771);
        for (var i = 0; i < 26; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h;
            var r = random.NextDouble() * 7 + 2;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(random.NextDouble() * 70 + 20), 90, 130, 220));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), r, r);
        }

        var edge = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.8,
            RadiusY = 0.8,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(60, 190, 205, 230), 1));
        edge.Freeze();
        dc.DrawRectangle(edge, null, new Rect(0, 0, w, h));
    }

    private static void DrawGraphite(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x07, 0x08, 0x0A), Color.FromRgb(0x16, 0x19, 0x1E));

        DrawGlow(dc, new Point(w * 0.72, h * 0.16), w * 0.40, Color.FromRgb(0xD8, 0xE8, 0xFF), 0.22);
        DrawGlow(dc, new Point(w * 0.22, h * 0.82), w * 0.34, Color.FromRgb(0x6E, 0x8C, 0xB8), 0.18);

        var random = new Random(31337);
        for (var i = 0; i < 34; i++)
        {
            var baseY = h * (0.06 + i * 0.028);
            var amplitude = h * (0.01 + random.NextDouble() * 0.035);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(-20, baseY), false, false);
                var x = 0.0;
                var segment = w / 5.0;
                while (x < w + segment)
                {
                    var next = x + segment;
                    ctx.BezierTo(
                        new Point(x + segment * 0.35, baseY - amplitude * random.NextDouble()),
                        new Point(x + segment * 0.65, baseY + amplitude * random.NextDouble()),
                        new Point(next, baseY + amplitude * (random.NextDouble() - 0.5)),
                        true, false);
                    x = next;
                }
            }

            geometry.Freeze();
            var alpha = (byte)(random.NextDouble() * 26 + 6);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 220, 232, 245)), 1.1);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        var accent = new Pen(new SolidColorBrush(Color.FromArgb(60, 140, 190, 255)), 1.6);
        accent.Freeze();
        var accentGeometry = new StreamGeometry();
        using (var ctx = accentGeometry.Open())
        {
            ctx.BeginFigure(new Point(-20, h * 0.68), false, false);
            ctx.BezierTo(new Point(w * 0.22, h * 0.60), new Point(w * 0.34, h * 0.74), new Point(w * 0.52, h * 0.66), true, false);
            ctx.BezierTo(new Point(w * 0.70, h * 0.58), new Point(w * 0.82, h * 0.70), new Point(w + 20, h * 0.62), true, false);
        }

        dc.DrawGeometry(null, accent, accentGeometry);
        DrawVignette(dc, w, h, 0.6);
    }

    private static void DrawFrost(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x07, 0x10, 0x18), Color.FromRgb(0x10, 0x22, 0x2E));

        DrawGlow(dc, new Point(w * 0.64, h * 0.40), w * 0.50, Color.FromRgb(0x8F, 0xE0, 0xFF), 0.34);
        DrawGlow(dc, new Point(w * 0.18, h * 0.78), w * 0.34, Color.FromRgb(0x4A, 0x8F, 0xB8), 0.26);

        var random = new Random(5150);
        var origin = new Point(w * 0.62, h * 0.42);
        for (var i = 0; i < 18; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var length = 160 + random.NextDouble() * w * 0.30;
            var width = 30 + random.NextDouble() * 130;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            var px = -dy;
            var py = dx;

            var p1 = new Point(origin.X + px * width * 0.25, origin.Y + py * width * 0.25);
            var p2 = new Point(origin.X - px * width * 0.25, origin.Y - py * width * 0.25);
            var p3 = new Point(p2.X + dx * length + px * width * 0.05, p2.Y + dy * length + py * width * 0.05);
            var p4 = new Point(p1.X + dx * length - px * width * 0.05, p1.Y + dy * length - py * width * 0.05);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(p1, true, true);
                ctx.LineTo(p2, true, false);
                ctx.LineTo(p3, true, false);
                ctx.LineTo(p4, true, false);
            }

            geometry.Freeze();

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(random.NextDouble() * 50 + 30), 0xD8, 0xF2, 0xFF), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(random.NextDouble() * 24 + 8), 0x9C, 0xD4, 0xF0), 1));
            brush.Freeze();

            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(random.NextDouble() * 50 + 30), 0xCF, 0xEE, 0xFF)), 1.2);
            pen.Freeze();
            dc.DrawGeometry(brush, pen, geometry);
        }

        for (var i = 0; i < 80; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h;
            var radius = random.NextDouble() * 2.2 + 0.4;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(random.NextDouble() * 150 + 30), 220, 245, 255));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }

        DrawVignette(dc, w, h, 0.62);
    }

    private static void DrawMidnight(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x05, 0x08, 0x14), Color.FromRgb(0x0C, 0x14, 0x2B));

        DrawGlow(dc, new Point(w * 0.74, h * 0.30), w * 0.46, Color.FromRgb(0x1E, 0x3A, 0x7A), 0.38);
        DrawGlow(dc, new Point(w * 0.24, h * 0.74), w * 0.38, Color.FromRgb(0x8A, 0x6D, 0x1F), 0.24);

        var random = new Random(2201);
        for (var i = 0; i < 150; i++)
        {
            var x = random.NextDouble() * w;
            var y = random.NextDouble() * h;
            var radius = random.NextDouble() * 1.8 + 0.3;
            var gold = random.NextDouble() > 0.45;
            var color = gold
                ? Color.FromArgb((byte)(random.NextDouble() * 150 + 40), 0xE8, 0xC9, 0x6A)
                : Color.FromArgb((byte)(random.NextDouble() * 120 + 30), 0xC8, 0xD8, 0xFF);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }

        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(55, 0xD4, 0xAF, 0x37)), 1.6);
        linePen.Freeze();
        for (var i = 0; i < 4; i++)
        {
            var y = h * (0.22 + i * 0.16);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(-30, y), false, false);
                ctx.BezierTo(
                    new Point(w * 0.24, y - h * 0.07),
                    new Point(w * 0.44, y + h * 0.05),
                    new Point(w * 0.64, y - h * 0.02),
                    true, false);
                ctx.BezierTo(
                    new Point(w * 0.78, y - h * 0.06),
                    new Point(w * 0.90, y + h * 0.03),
                    new Point(w + 30, y - h * 0.01),
                    true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, linePen, geometry);
        }

        DrawVignette(dc, w, h, 0.58);
    }

    private static void DrawMint(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0xF2, 0xFC, 0xF8), Color.FromRgb(0xCF, 0xEF, 0xE3));

        DrawGlow(dc, new Point(w * 0.30, h * 0.32), w * 0.50, Color.FromRgb(0xFF, 0xFF, 0xFF), 0.85);
        DrawGlow(dc, new Point(w * 0.78, h * 0.70), w * 0.44, Color.FromRgb(0x6F, 0xD6, 0xB4), 0.32);
        DrawGlow(dc, new Point(w * 0.14, h * 0.86), w * 0.30, Color.FromRgb(0xB8, 0xE8, 0xD2), 0.36);

        var random = new Random(6611);
        for (var i = 0; i < 14; i++)
        {
            var cx = w * (0.08 + random.NextDouble() * 0.84);
            var cy = h * (0.18 + random.NextDouble() * 0.72);
            var length = w * (0.05 + random.NextDouble() * 0.10);
            var angle = random.NextDouble() * Math.PI * 2;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);
            var px = -dy;
            var py = dx;

            var leaf = new StreamGeometry();
            using (var ctx = leaf.Open())
            {
                ctx.BeginFigure(new Point(cx, cy), true, true);
                ctx.BezierTo(
                    new Point(cx + dx * length * 0.4 + px * length * 0.28, cy + dy * length * 0.4 + py * length * 0.28),
                    new Point(cx + dx * length * 0.7 + px * length * 0.10, cy + dy * length * 0.7 + py * length * 0.10),
                    new Point(cx + dx * length, cy + dy * length),
                    true, false);
                ctx.BezierTo(
                    new Point(cx + dx * length * 0.7 - px * length * 0.10, cy + dy * length * 0.7 - py * length * 0.10),
                    new Point(cx + dx * length * 0.4 - px * length * 0.28, cy + dy * length * 0.4 - py * length * 0.28),
                    new Point(cx, cy),
                    true, false);
            }

            leaf.Freeze();
            var brush = new LinearGradientBrush(
                Color.FromArgb((byte)(random.NextDouble() * 55 + 40), 0x2F, 0xA3, 0x7C),
                Color.FromArgb((byte)(random.NextDouble() * 30 + 12), 0x1E, 0x7A, 0x5C),
                new Point(0, 0), new Point(1, 1));
            brush.Freeze();
            dc.DrawGeometry(brush, null, leaf);
        }

        var randomDots = new Random(7712);
        for (var i = 0; i < 30; i++)
        {
            var x = randomDots.NextDouble() * w;
            var y = randomDots.NextDouble() * h;
            var r = randomDots.NextDouble() * 5 + 1.5;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(randomDots.NextDouble() * 60 + 18), 0x2F, 0xA3, 0x7C));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), r, r);
        }

        DrawVignette(dc, w, h, 0.18);
    }

    private static void DrawSunset(DrawingContext dc, double w, double h)
    {
        FillGradient(dc, w, h, Color.FromRgb(0x1B, 0x0C, 0x24), Color.FromRgb(0x3A, 0x12, 0x1E));

        DrawGlow(dc, new Point(w * 0.50, h * 0.62), w * 0.52, Color.FromRgb(0xFF, 0x7A, 0x45), 0.58);
        DrawGlow(dc, new Point(w * 0.22, h * 0.28), w * 0.34, Color.FromRgb(0xC2, 0x41, 0x0C), 0.34);
        DrawGlow(dc, new Point(w * 0.82, h * 0.20), w * 0.32, Color.FromRgb(0x8E, 0x2B, 0x8A), 0.30);

        var sun = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        sun.GradientStops.Add(new GradientStop(Color.FromArgb(245, 0xFF, 0xD3, 0x8A), 0));
        sun.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xFF, 0x9A, 0x5A), 0.55));
        sun.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x7A, 0x45), 1));
        sun.Freeze();
        dc.DrawEllipse(sun, null, new Point(w * 0.50, h * 0.60), w * 0.19, w * 0.19);

        var random = new Random(7788);
        for (var i = 0; i < 16; i++)
        {
            var y = h * (0.36 + i * 0.035);
            var alpha = (byte)Math.Max(8, 70 - i * 4);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xB0, 0x80)), 2 + i * 0.25);
            pen.Freeze();
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(-20, y), false, false);
                ctx.BezierTo(
                    new Point(w * 0.28, y - h * 0.03 * random.NextDouble()),
                    new Point(w * 0.62, y + h * 0.04 * random.NextDouble()),
                    new Point(w + 20, y - h * 0.02),
                    true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        DrawVignette(dc, w, h, 0.52);
    }
}
