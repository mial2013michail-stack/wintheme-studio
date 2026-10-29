using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinThemeStudio.Core;
using WinThemeStudio.Models;

namespace WinThemeStudio.Services;

public sealed class IconPackService
{
    private const string PackVersion = "5";
    private const double Canvas = 256;
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };
    private static readonly string[] Styles =
    {
        "default", "aurora", "aubergine", "minimal", "midnight", "mint", "sunset"
    };

    private readonly ConfigService _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IconPackService(ConfigService config)
    {
        _config = config;
    }

    public string EffectiveStyle =>
        ResolveStyle(_config.Current.IconStyle, _config.Current.LastAppliedProfile);

    public static IReadOnlyList<IconInfo> GetDefinitions() => new List<IconInfo>
    {
        new() { Key = "folder", Name = "Папка", Description = "Проводник и папки" },
        new() { Key = "browser", Name = "Браузер", Description = "Веб-браузеры" },
        new() { Key = "terminal", Name = "Терминал", Description = "Командная строка и терминалы" },
        new() { Key = "music", Name = "Музыка", Description = "Аудиоплееры" },
        new() { Key = "video", Name = "Видео", Description = "Видеоплееры" },
        new() { Key = "settings", Name = "Параметры", Description = "Настройки системы" },
        new() { Key = "document", Name = "Документ", Description = "Текстовые документы" },
        new() { Key = "image", Name = "Изображение", Description = "Графика и фотографии" },
        new() { Key = "code", Name = "Код", Description = "Редакторы и IDE" },
        new() { Key = "gamepad", Name = "Игры", Description = "Игровые лаунчеры" },
        new() { Key = "chat", Name = "Чат", Description = "Мессенджеры" },
        new() { Key = "mail", Name = "Почта", Description = "Почтовые клиенты" },
        new() { Key = "cloud", Name = "Облако", Description = "Облачные диски" },
        new() { Key = "calculator", Name = "Калькулятор", Description = "Вычисления" },
        new() { Key = "archive", Name = "Архив", Description = "Архивы и упаковщики" },
        new() { Key = "drive", Name = "Диск", Description = "Локальные и съёмные диски" },
        new() { Key = "trash", Name = "Корзина", Description = "Пустая корзина Windows" },
        new() { Key = "trash-full", Name = "Полная корзина", Description = "Корзина с файлами" }
    };

    public string GetIconPath(string key, string? style = null)
    {
        var safe = SanitizeKey(key);
        var resolved = style is null
            ? EffectiveStyle
            : ResolveStyle(style, _config.Current.LastAppliedProfile);
        return Path.Combine(AppPaths.IconPackDir, safe + StyleSuffix(resolved) + ".ico");
    }

    public string GetPreviewPath(string key, string? style = null)
    {
        var safe = SanitizeKey(key);
        var resolved = style is null
            ? EffectiveStyle
            : ResolveStyle(style, _config.Current.LastAppliedProfile);
        return Path.Combine(AppPaths.IconPackDir, "previews", safe + StyleSuffix(resolved) + ".png");
    }

    public static string ResolveStyle(string? requested, string? profileKey)
    {
        var style = NormalizeStyle(requested);
        return style == "auto" ? NormalizeStyle(profileKey) : style;
    }

    public static string NormalizeStyle(string? style) => style?.Trim().ToLowerInvariant() switch
    {
        "aurora" => "aurora",
        "aubergine" => "aubergine",
        "minimal" => "minimal",
        "midnight" => "midnight",
        "mint" => "mint",
        "sunset" => "sunset",
        "studio" => "studio",
        "auto" => "auto",
        _ => "default"
    };

    private static string StyleSuffix(string? style) => NormalizeStyle(style) switch
    {
        "aurora" => "-aurora",
        "aubergine" => "-aubergine",
        "minimal" => "-minimal",
        "midnight" => "-midnight",
        "mint" => "-mint",
        "sunset" => "-sunset",
        "studio" => "-studio",
        _ => ""
    };

    public async Task EnsurePackAsync(IProgress<string>? progress = null)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(AppPaths.IconPackDir);
            Directory.CreateDirectory(Path.Combine(AppPaths.IconPackDir, "previews"));

            var signature = StudioSignature();
            var versionFile = Path.Combine(AppPaths.IconPackDir, "version.txt");
            var current = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";
            var expectedVersion = PackVersion + "|" + signature;
            var definitions = GetDefinitions();
            var allStyles = Styles.Concat(new[] { "studio" }).ToArray();
            var missing = allStyles.Any(style => definitions.Any(d =>
                !File.Exists(GetIconPath(d.Key, style)) ||
                !File.Exists(GetPreviewPath(d.Key, style))));
            if (current == expectedVersion && !missing)
            {
                return;
            }

            foreach (var file in Directory.GetFiles(AppPaths.IconPackDir, "*.ico"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignore a file held by Explorer
                }
            }

            var previewDir = Path.Combine(AppPaths.IconPackDir, "previews");
            foreach (var file in Directory.GetFiles(previewDir, "*.png"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // ignore
                }
            }

            var options = StudioOptions.FromConfig(_config.Current);
            var customIcons = new Dictionary<string, string>(_config.Current.CustomIconPaths,
                StringComparer.OrdinalIgnoreCase);

            await RunStaAsync(() =>
            {
                foreach (var style in Styles)
                {
                    foreach (var definition in definitions)
                    {
                        progress?.Report($"Отрисовка значка «{definition.Name}»…");
                        GenerateIcon(
                            definition.Key,
                            style,
                            GetIconPath(definition.Key, style),
                            GetPreviewPath(definition.Key, style),
                            StudioOptions.None);
                    }
                }

                foreach (var definition in definitions)
                {
                    progress?.Report($"Отрисовка значка «{definition.Name}»…");
                    customIcons.TryGetValue(definition.Key, out var customPath);
                    GenerateIcon(
                        definition.Key,
                        "studio",
                        GetIconPath(definition.Key, "studio"),
                        GetPreviewPath(definition.Key, "studio"),
                        options,
                        customPath);
                }
            });

            File.WriteAllText(versionFile, expectedVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string StudioSignature()
    {
        var cfg = _config.Current;
        var custom = string.Join(";", cfg.CustomIconPaths
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key + "=" + pair.Value));
        return string.Join("|",
            cfg.IconTint ?? "",
            cfg.IconRadius.ToString(CultureInfo.InvariantCulture),
            cfg.IconMonochrome ? "1" : "0",
            custom);
    }

    public static string GuessKey(string? targetPath, string? displayName)
    {
        var text = ((displayName ?? "") + " " + (targetPath ?? "")).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "app";
        }

        if (ContainsAny(text, "recycle", "recycler", "корзин", "trash"))
        {
            return "trash";
        }

        if (ContainsAny(text, "chrome", "msedge", "edge", "firefox", "opera", "brave", "vivaldi", "browser", "браузер"))
        {
            return "browser";
        }

        if (ContainsAny(text, "windowsterminal", "wt.exe", "powershell", "pwsh", "cmd.exe", "terminal", "терминал"))
        {
            return "terminal";
        }

        if (ContainsAny(text, "spotify", "music", "groove", "музык", "audio"))
        {
            return "music";
        }

        if (ContainsAny(text, "vlc", "video", "media player", "mpc-hc", "видео"))
        {
            return "video";
        }

        if (ContainsAny(text, "onedrive", "dropbox", "cloud", "облак"))
        {
            return "cloud";
        }

        if (ContainsAny(text, "outlook", "thunderbird", "mail", "почт"))
        {
            return "mail";
        }

        if (ContainsAny(text, "discord", "telegram", "whatsapp", "slack", "chat", "чат"))
        {
            return "chat";
        }

        if (ContainsAny(text, "devenv", "vscode", "code.exe", "rider", "pycharm", "idea64", "android studio", "код"))
        {
            return "code";
        }

        if (ContainsAny(text, "steam", "epicgames", "game", "игр"))
        {
            return "gamepad";
        }

        if (ContainsAny(text, "7z", "winrar", "zip", "archive", "архив"))
        {
            return "archive";
        }

        if (ContainsAny(text, "calc", "калькул"))
        {
            return "calculator";
        }

        if (ContainsAny(text, "mspaint", "photos", "image", "изображ", "фото"))
        {
            return "image";
        }

        if (ContainsAny(text, "notepad", "word", "writer", "document", "документ"))
        {
            return "document";
        }

        if (ContainsAny(text, "explorer", "folder", "папк", "проводник"))
        {
            return "folder";
        }

        if (ContainsAny(text, "settings", "control", "taskmgr", "параметр"))
        {
            return "settings";
        }

        return "app";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "app";
        }

        var safe = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(safe) ? "app" : safe;
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

    private static void GenerateIcon(
        string key,
        string style,
        string icoPath,
        string previewPath,
        StudioOptions options,
        string? customPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

        if (!string.IsNullOrWhiteSpace(customPath) &&
            File.Exists(customPath) &&
            TryWriteCustomIcon(customPath, icoPath, previewPath))
        {
            return;
        }

        var frames = Sizes.Select(size => (Size: size, Bytes: RenderPng(key, style, size, options))).ToList();
        using (var stream = File.Create(icoPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);

            var offset = 6 + 16 * frames.Count;
            foreach (var frame in frames)
            {
                writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
                writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)frame.Bytes.Length);
                writer.Write((uint)offset);
                offset += frame.Bytes.Length;
            }

            foreach (var frame in frames)
            {
                writer.Write(frame.Bytes);
            }
        }

        File.WriteAllBytes(previewPath, RenderPng(key, style, 128, options));
    }

    private static bool TryWriteCustomIcon(string sourcePath, string icoPath, string previewPath)
    {
        try
        {
            using (var stream = File.OpenRead(sourcePath))
            {
                var decoder = BitmapDecoder.Create(
                    stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return false;
                }

                var frame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                    .First();
                var scale = 128.0 / Math.Max(1, Math.Max(frame.PixelWidth, frame.PixelHeight));
                var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                transformed.Freeze();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(transformed));
                using var preview = File.Create(previewPath);
                encoder.Save(preview);
            }

            File.Copy(sourcePath, icoPath, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] RenderPng(string key, string style, int size, StudioOptions options)
    {
        var visual = new DrawingVisual
        {
            Transform = new ScaleTransform(size / Canvas, size / Canvas)
        };

        using (var dc = visual.RenderOpen())
        {
            DrawIcon(dc, key, style, options);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    private static void DrawIcon(DrawingContext dc, string key, string style, StudioOptions options)
    {
        var baseColor = BaseColor(key, style);
        if (!string.IsNullOrWhiteSpace(options.Tint))
        {
            try
            {
                baseColor = Blend(baseColor, Parse(options.Tint), 0.6);
            }
            catch
            {
                // A malformed tint must not break icon generation.
            }
        }

        if (options.Monochrome)
        {
            baseColor = Grayscale(baseColor);
        }

        var rect = new Rect(14, 12, 228, 228);
        double radius = NormalizeStyle(style) switch
        {
            "aurora" => 62,
            "aubergine" => 38,
            "minimal" => 26,
            "midnight" => 46,
            "mint" => 30,
            "sunset" => 70,
            _ => 54
        };

        if (options.Radius >= 0)
        {
            radius = Math.Clamp(options.Radius, 0, 100) / 100.0 * 128.0;
        }

        var shadow = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        shadow.Freeze();
        dc.DrawRoundedRectangle(shadow, null, new Rect(rect.X, rect.Y + 7, rect.Width, rect.Height), radius, radius);

        var gradient = new LinearGradientBrush(Lighten(baseColor, 0.18), Darken(baseColor, 0.18), 90);
        gradient.Freeze();
        dc.DrawRoundedRectangle(gradient, null, rect, radius, radius);

        var glow = new RadialGradientBrush
        {
            Center = new Point(0.35, 0.22),
            GradientOrigin = new Point(0.35, 0.22),
            RadiusX = 0.55,
            RadiusY = 0.45,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        glow.Freeze();
        dc.DrawRoundedRectangle(glow, null, rect, radius, radius);

        var glyphColor = Luminance(baseColor) > 0.62
            ? Color.FromRgb(0x16, 0x1B, 0x24)
            : Color.FromArgb(248, 255, 255, 255);
        var white = new SolidColorBrush(glyphColor);
        white.Freeze();
        var pen = new Pen(white, 10)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        switch (key)
        {
            case "folder":
                dc.DrawRoundedRectangle(white, null, new Rect(52, 76, 84, 54), 12, 12);
                dc.DrawRoundedRectangle(white, null, new Rect(52, 98, 152, 102), 16, 16);
                break;

            case "browser":
                dc.DrawEllipse(null, pen, new Point(128, 128), 62, 62);
                dc.DrawLine(pen, new Point(66, 128), new Point(190, 128));
                dc.DrawEllipse(null, new Pen(white, 8), new Point(128, 128), 30, 62);
                break;

            case "terminal":
                dc.DrawRoundedRectangle(null, new Pen(white, 8), new Rect(52, 72, 152, 112), 18, 18);
                dc.DrawLine(pen, new Point(88, 108), new Point(110, 128));
                dc.DrawLine(pen, new Point(110, 128), new Point(88, 148));
                dc.DrawLine(pen, new Point(124, 148), new Point(158, 148));
                break;

            case "music":
                dc.DrawEllipse(white, null, new Point(100, 164), 18, 14);
                dc.DrawEllipse(white, null, new Point(160, 148), 18, 14);
                dc.DrawLine(pen, new Point(118, 164), new Point(118, 96));
                dc.DrawLine(pen, new Point(178, 148), new Point(178, 80));
                dc.DrawLine(new Pen(white, 14), new Point(118, 96), new Point(178, 80));
                break;

            case "video":
                dc.DrawRoundedRectangle(null, new Pen(white, 8), new Rect(52, 74, 152, 108), 20, 20);
                var play = new StreamGeometry();
                using (var ctx = play.Open())
                {
                    ctx.BeginFigure(new Point(112, 104), true, true);
                    ctx.LineTo(new Point(158, 128), true, false);
                    ctx.LineTo(new Point(112, 152), true, false);
                }

                play.Freeze();
                dc.DrawGeometry(white, null, play);
                break;

            case "settings":
                for (var i = 0; i < 8; i++)
                {
                    dc.PushTransform(new RotateTransform(i * 45, 128, 128));
                    dc.DrawRoundedRectangle(white, null, new Rect(116, 38, 24, 36), 8, 8);
                    dc.Pop();
                }

                dc.DrawEllipse(null, new Pen(white, 22), new Point(128, 128), 54, 54);
                break;

            case "document":
                var page = new StreamGeometry();
                using (var ctx = page.Open())
                {
                    ctx.BeginFigure(new Point(84, 52), false, true);
                    ctx.LineTo(new Point(148, 52), true, false);
                    ctx.LineTo(new Point(180, 84), true, false);
                    ctx.LineTo(new Point(180, 204), true, false);
                    ctx.LineTo(new Point(84, 204), true, false);
                }

                page.Freeze();
                dc.DrawGeometry(null, new Pen(white, 9), page);
                dc.DrawLine(new Pen(white, 7), new Point(148, 52), new Point(148, 84));
                dc.DrawLine(new Pen(white, 7), new Point(148, 84), new Point(180, 84));
                dc.DrawLine(new Pen(white, 7), new Point(106, 122), new Point(158, 122));
                dc.DrawLine(new Pen(white, 7), new Point(106, 148), new Point(158, 148));
                dc.DrawLine(new Pen(white, 7), new Point(106, 174), new Point(140, 174));
                break;

            case "image":
                dc.DrawRoundedRectangle(null, new Pen(white, 8), new Rect(50, 70, 156, 116), 20, 20);
                dc.DrawEllipse(white, null, new Point(94, 108), 13, 13);
                var mountain = new StreamGeometry();
                using (var ctx = mountain.Open())
                {
                    ctx.BeginFigure(new Point(66, 172), false, false);
                    ctx.LineTo(new Point(108, 128), true, false);
                    ctx.LineTo(new Point(136, 156), true, false);
                    ctx.LineTo(new Point(160, 136), true, false);
                    ctx.LineTo(new Point(192, 172), true, false);
                }

                mountain.Freeze();
                dc.DrawGeometry(null, new Pen(white, 9), mountain);
                break;

            case "code":
                dc.DrawLine(new Pen(white, 11), new Point(104, 102), new Point(76, 128));
                dc.DrawLine(new Pen(white, 11), new Point(76, 128), new Point(104, 154));
                dc.DrawLine(new Pen(white, 11), new Point(152, 102), new Point(180, 128));
                dc.DrawLine(new Pen(white, 11), new Point(180, 128), new Point(152, 154));
                dc.DrawLine(new Pen(white, 9), new Point(142, 90), new Point(114, 166));
                break;

            case "gamepad":
                dc.DrawRoundedRectangle(null, new Pen(white, 9), new Rect(48, 92, 160, 88), 44, 44);
                dc.DrawLine(new Pen(white, 9), new Point(100, 118), new Point(100, 154));
                dc.DrawLine(new Pen(white, 9), new Point(82, 136), new Point(118, 136));
                dc.DrawEllipse(white, null, new Point(160, 126), 8, 8);
                dc.DrawEllipse(white, null, new Point(180, 144), 8, 8);
                break;

            case "chat":
                dc.DrawRoundedRectangle(null, new Pen(white, 9), new Rect(50, 70, 156, 106), 30, 30);
                var tail = new StreamGeometry();
                using (var ctx = tail.Open())
                {
                    ctx.BeginFigure(new Point(90, 172), false, false);
                    ctx.LineTo(new Point(82, 208), true, false);
                    ctx.LineTo(new Point(124, 176), true, false);
                }

                tail.Freeze();
                dc.DrawGeometry(null, new Pen(white, 9), tail);
                dc.DrawEllipse(white, null, new Point(102, 122), 7, 7);
                dc.DrawEllipse(white, null, new Point(128, 122), 7, 7);
                dc.DrawEllipse(white, null, new Point(154, 122), 7, 7);
                break;

            case "mail":
                dc.DrawRoundedRectangle(null, new Pen(white, 9), new Rect(50, 78, 156, 100), 16, 16);
                dc.DrawLine(new Pen(white, 8), new Point(58, 92), new Point(128, 142));
                dc.DrawLine(new Pen(white, 8), new Point(198, 92), new Point(128, 142));
                break;

            case "cloud":
                dc.DrawEllipse(white, null, new Point(106, 142), 38, 38);
                dc.DrawEllipse(white, null, new Point(148, 124), 46, 46);
                dc.DrawEllipse(white, null, new Point(180, 146), 32, 32);
                dc.DrawRoundedRectangle(white, null, new Rect(96, 140, 100, 38), 18, 18);
                break;

            case "calculator":
                dc.DrawRoundedRectangle(null, new Pen(white, 9), new Rect(68, 54, 120, 152), 20, 20);
                dc.DrawRoundedRectangle(null, new Pen(white, 7), new Rect(86, 72, 84, 30), 6, 6);
                for (var row = 0; row < 3; row++)
                {
                    for (var col = 0; col < 3; col++)
                    {
                        dc.DrawEllipse(white, null, new Point(96 + col * 32, 132 + row * 26), 6, 6);
                    }
                }

                break;

            case "archive":
                dc.DrawRoundedRectangle(null, new Pen(white, 8), new Rect(58, 72, 140, 36), 8, 8);
                dc.DrawRoundedRectangle(null, new Pen(white, 8), new Rect(66, 108, 124, 90), 10, 10);
                dc.DrawLine(new Pen(white, 8), new Point(128, 108), new Point(128, 150));
                dc.DrawLine(new Pen(white, 8), new Point(128, 126), new Point(152, 126));
                break;

            case "drive":
                dc.DrawRoundedRectangle(null, new Pen(white, 9), new Rect(44, 76, 168, 104), 22, 22);
                dc.DrawLine(new Pen(white, 8), new Point(72, 146), new Point(184, 146));
                dc.DrawEllipse(white, null, new Point(176, 108), 7, 7);
                dc.DrawLine(new Pen(white, 7), new Point(76, 108), new Point(130, 108));
                break;

            case "trash":
                DrawTrash(dc, white, pen, full: false);
                break;

            case "trash-full":
                DrawTrash(dc, white, pen, full: true);
                break;

            default:
                dc.DrawRoundedRectangle(white, null, new Rect(68, 68, 48, 48), 14, 14);
                dc.DrawRoundedRectangle(white, null, new Rect(140, 68, 48, 48), 14, 14);
                dc.DrawRoundedRectangle(white, null, new Rect(68, 140, 48, 48), 14, 14);
                dc.DrawRoundedRectangle(white, null, new Rect(140, 140, 48, 48), 14, 14);
                break;
        }
    }

    private static void DrawTrash(DrawingContext dc, Brush glyph, Pen pen, bool full)
    {
        if (full)
        {
            // Crumpled contents peeking over the rim.
            dc.DrawRoundedRectangle(glyph, null, new Rect(92, 54, 34, 28), 11, 11);
            dc.DrawRoundedRectangle(glyph, null, new Rect(126, 44, 30, 34), 10, 10);
            dc.DrawRoundedRectangle(glyph, null, new Rect(148, 62, 24, 22), 9, 9);
        }

        dc.DrawLine(new Pen(glyph, 10), new Point(72, 84), new Point(184, 84));
        dc.DrawLine(new Pen(glyph, 9), new Point(106, 84), new Point(106, 62));
        dc.DrawLine(new Pen(glyph, 9), new Point(150, 84), new Point(150, 62));
        dc.DrawLine(new Pen(glyph, 9), new Point(106, 62), new Point(150, 62));
        var bin = new StreamGeometry();
        using (var ctx = bin.Open())
        {
            ctx.BeginFigure(new Point(82, 96), false, true);
            ctx.LineTo(new Point(174, 96), true, false);
            ctx.LineTo(new Point(164, 204), true, false);
            ctx.LineTo(new Point(92, 204), true, false);
        }

        bin.Freeze();
        dc.DrawGeometry(null, new Pen(glyph, 9), bin);
        dc.DrawLine(new Pen(glyph, 7), new Point(110, 118), new Point(114, 180));
        dc.DrawLine(new Pen(glyph, 7), new Point(146, 118), new Point(142, 180));
    }

    private static Color BaseColor(string key, string style)
    {
        var normalized = NormalizeStyle(style);
        var baseColor = key switch
        {
            "folder" => Parse("#E8A33D"),
            "browser" => Parse("#4A90E2"),
            "terminal" => Parse("#2EAD6B"),
            "music" => Parse("#E05A8A"),
            "video" => Parse("#D9534F"),
            "settings" => Parse("#7A8CA3"),
            "document" => Parse("#5C6BC0"),
            "image" => Parse("#26A69A"),
            "code" => Parse("#8E5BD9"),
            "gamepad" => Parse("#3FA46A"),
            "chat" => Parse("#2BB3C0"),
            "mail" => Parse("#4D7BE8"),
            "cloud" => Parse("#56A8E8"),
            "calculator" => Parse("#E08A3C"),
            "archive" => Parse("#A0714A"),
            "drive" => Parse("#5B6B82"),
            "trash" => Parse("#6B7280"),
            "trash-full" => Parse("#8A5A3C"),
            _ => Parse("#667085")
        };

        // Folders and the recycle bin carry the strongest profile signature.
        return key switch
        {
            "folder" => normalized switch
            {
                "aurora" => Parse("#4A90E2"),
                "aubergine" => Parse("#E95420"),
                "minimal" => Parse("#6B7280"),
                "midnight" => Parse("#C9A227"),
                "mint" => Parse("#2FA37C"),
                "sunset" => Parse("#FF7A45"),
                _ => baseColor
            },
            "trash" or "trash-full" => normalized switch
            {
                "aurora" => Parse("#5C6BC0"),
                "aubergine" => Parse("#8E5BD9"),
                "minimal" => Parse("#4B5563"),
                "midnight" => Parse("#8A6D1F"),
                "mint" => Parse("#1E7A5C"),
                "sunset" => Parse("#C2410C"),
                _ => baseColor
            },
            _ => StyleColor(baseColor, normalized)
        };
    }

    private static Color StyleColor(Color color, string style) => style switch
    {
        "aurora" => Blend(color, Parse("#2E86FF"), 0.35),
        "aubergine" => Blend(color, Parse("#8E44AD"), 0.35),
        "minimal" => Blend(Grayscale(color), Parse("#9AA3B2"), 0.35),
        "midnight" => Blend(color, Parse("#D4AF37"), 0.30),
        "mint" => Blend(color, Parse("#2FA37C"), 0.35),
        "sunset" => Blend(color, Parse("#FF7A45"), 0.38),
        _ => color
    };

    private static Color Grayscale(Color color)
    {
        var gray = (byte)Math.Clamp(color.R * 0.299 + color.G * 0.587 + color.B * 0.114, 0, 255);
        return Color.FromRgb(gray, gray, gray);
    }

    private static double Luminance(Color color) =>
        (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255.0;

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Clamp(from.R + (to.R - from.R) * amount, 0, 255),
        (byte)Math.Clamp(from.G + (to.G - from.G) * amount, 0, 255),
        (byte)Math.Clamp(from.B + (to.B - from.B) * amount, 0, 255));

    private static Color Parse(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
        (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
        (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));

    private static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.B * (1 - amount), 0, 255));

    private sealed record StudioOptions(string Tint, int Radius, bool Monochrome)
    {
        public static StudioOptions None { get; } = new("", -1, false);

        public static StudioOptions FromConfig(AppConfig config) =>
            new(config.IconTint ?? "", config.IconRadius, config.IconMonochrome);
    }
}
