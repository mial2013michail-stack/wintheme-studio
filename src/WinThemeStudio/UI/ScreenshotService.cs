using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinThemeStudio.Core;
using WinThemeStudio.Services;

namespace WinThemeStudio.UI;

public static class ScreenshotService
{
    private static readonly (string Key, string File, int Delay)[] Pages =
    {
        ("dashboard", "01-dashboard.png", 700),
        ("profiles", "02-profiles.png", 900),
        ("appearance", "03-appearance.png", 1400),
        ("explorer", "04-explorer.png", 700),
        ("icons", "05-icons.png", 2200),
        ("dock", "06-dock.png", 700),
        ("restore", "07-restore.png", 700),
        ("about", "08-about.png", 700)
    };

    public static async Task<IReadOnlyList<string>> CaptureAsync(
        MainWindow window,
        AppServices services,
        string directory,
        double width = 1280,
        double height = 840)
    {
        Directory.CreateDirectory(directory);

        await services.Icons.EnsurePackAsync();
        await services.Wallpaper.EnsureAllAsync();

        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("Окно не содержит визуального дерева.");
        }

        if (content is Panel panel)
        {
            panel.Background = (Brush)Application.Current.FindResource("BgBrush");
        }

        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var results = new List<string>();
        foreach (var (key, file, delay) in Pages)
        {
            window.Navigate(key);
            window.PageScroll.ScrollToTop();
            await Task.Delay(delay);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var topPath = Path.Combine(directory, file);
            await CaptureFrameAsync(content, topPath, width, height);
            results.Add(topPath);
            Logger.Info("Снимок интерфейса сохранён: " + topPath);

            // Pages are taller than the headless viewport. Capture the lower
            // sections as well so visual QA covers the whole page, not only the
            // first screen.
            var scrollable = window.PageScroll.ScrollableHeight;
            if (scrollable <= 24)
            {
                continue;
            }

            var viewport = window.PageScroll.ViewportHeight;
            var offsets = new List<double>();
            if (scrollable > viewport * 0.8)
            {
                offsets.Add(Math.Round(scrollable * 0.55));
            }

            offsets.Add(Math.Round(scrollable));

            var section = 2;
            var previous = 0d;
            var baseName = Path.GetFileNameWithoutExtension(file);
            foreach (var offset in offsets)
            {
                if (offset - previous < 24)
                {
                    continue;
                }

                previous = offset;
                window.PageScroll.ScrollToVerticalOffset(offset);
                await Task.Delay(220);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                var lowerPath = Path.Combine(directory, $"{baseName}-{section}.png");
                await CaptureFrameAsync(content, lowerPath, width, height);
                results.Add(lowerPath);
                Logger.Info($"Снимок нижней части страницы сохранён: {lowerPath} (смещение {offset:0})");
                section++;
            }
        }

        return results;
    }

    private static async Task CaptureFrameAsync(FrameworkElement content, string path, double width, double height)
    {
        content.UpdateLayout();
        await content.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        const double scale = 1.25;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(content);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
