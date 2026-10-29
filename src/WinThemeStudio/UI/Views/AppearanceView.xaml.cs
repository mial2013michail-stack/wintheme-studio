using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinThemeStudio.Core;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio.UI.Views;

public partial class AppearanceView : PageView
{
    private static readonly string[] AccentColors =
    {
        "#0078D4", "#0A84FF", "#7C5CFF", "#E95420", "#E5484D",
        "#F5A524", "#30A46C", "#14B8A6", "#E93D82", "#9AA3B2"
    };

    private bool _updating;
    private bool _busy;
    private bool _wallpapersReady;
    private bool _loadingWallpapers;

    public AppearanceView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
        BuildPalette();
    }

    public override void OnShown()
    {
        UpdateState();
        RebuildWallpapers();

        if (!_wallpapersReady && !_loadingWallpapers)
        {
            _ = LoadWallpapersAsync();
        }
    }

    private void BuildPalette()
    {
        AccentPalette.Children.Clear();
        foreach (var hex in AccentColors)
        {
            var button = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Background = new SolidColorBrush(ThemeService.ParseColor(hex)),
                Tag = hex,
                ToolTip = hex
            };
            button.Click += AccentSwatch_Click;
            AccentPalette.Children.Add(button);
        }
    }

    private void UpdateState()
    {
        _updating = true;
        try
        {
            var dark = Services.Theme.IsDarkMode();
            var accent = Services.Theme.GetAccentColor();
            var accentHex = ThemeService.ToHex(accent).ToUpperInvariant();

            LightTheme.IsChecked = !dark;
            DarkTheme.IsChecked = dark;
            ThemePreview.Background = new SolidColorBrush(dark
                ? Color.FromRgb(0x12, 0x16, 0x1F)
                : Color.FromRgb(0xF2, 0xF6, 0xFB));
            ThemeWindow.Background = new SolidColorBrush(dark
                ? Color.FromRgb(0x22, 0x28, 0x35)
                : Color.FromRgb(0xE1, 0xE9, 0xF3));
            ThemeHeader.Background = new SolidColorBrush(dark
                ? Color.FromRgb(0x2A, 0x31, 0x40)
                : Color.FromRgb(0xFF, 0xFF, 0xFF));
            ThemeLine.Background = new SolidColorBrush(dark
                ? Color.FromRgb(0x69, 0x74, 0x8A)
                : Color.FromRgb(0xB8, 0xC7, 0xD9));
            ThemeAccentBar.Background = new SolidColorBrush(accent);

            AccentPreview.Background = new SolidColorBrush(accent);
            AccentLabel.Text = accentHex;
            if (!CustomAccentBox.IsKeyboardFocusWithin)
            {
                CustomAccentBox.Text = accentHex;
            }

            TransparencyToggle.IsChecked = Services.Theme.IsTransparencyEnabled();
            AnimationsToggle.IsChecked = Services.Theme.AreAnimationsEnabled();

            var cfg = Services.Config.Current;
            var fit = WallpaperService.NormalizeFit(cfg.WallpaperFit);
            FitFill.IsChecked = fit == "fill";
            FitFit.IsChecked = fit == "fit";
            FitStretch.IsChecked = fit == "stretch";
            FitCenter.IsChecked = fit == "center";
            FitTile.IsChecked = fit == "tile";
            FitCrop.IsChecked = fit == "crop";

            var fitText = WallpaperService.FitDisplayName(fit);
            if (cfg.LastWallpaperIsCustom)
            {
                WallpaperStatus.Text = $"Сейчас: своё изображение · режим «{fitText}». Оригинальные обои Windows сохранены в личной папке.";
            }
            else if (!string.IsNullOrWhiteSpace(cfg.LastWallpaperKey))
            {
                var preset = WallpaperService.GetPreset(cfg.LastWallpaperKey);
                WallpaperStatus.Text = $"Сейчас: {preset.Name} · режим «{fitText}». {preset.Description}. Оригинальные обои сохранены.";
            }
            else
            {
                WallpaperStatus.Text = $"Сейчас: исходные обои Windows · режим «{fitText}». Перед первым изменением они сохранены в личной папке.";
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private async void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating || _busy || sender is not RadioButton { IsChecked: true } button)
        {
            return;
        }

        var dark = ReferenceEquals(button, DarkTheme);
        _busy = true;
        try
        {
            await Host.ExecuteChangeAsync(
                dark ? "Тёмная тема" : "Светлая тема",
                transaction =>
                {
                    Services.Theme.SetTheme(transaction, dark);
                    return Task.CompletedTask;
                },
                dark ? "Тёмная тема включена." : "Светлая тема включена.");
        }
        finally
        {
            _busy = false;
            UpdateState();
        }
    }

    private async void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not Button { Tag: string hex })
        {
            return;
        }

        await ApplyAccentAsync(hex);
    }

    private async void ApplyCustomAccent_Click(object sender, RoutedEventArgs e)
    {
        var text = CustomAccentBox.Text.Trim();
        try
        {
            var color = ThemeService.ParseColor(text);
            var normalized = ThemeService.ToHex(color).ToUpperInvariant();
            await ApplyAccentAsync(normalized);
        }
        catch (FormatException ex)
        {
            Host.ShowToast(ex.Message, true);
        }
    }

    private async Task ApplyAccentAsync(string hex)
    {
        if (_busy)
        {
            UpdateState();
            return;
        }

        _busy = true;
        try
        {
            var normalized = ThemeService.ToHex(ThemeService.ParseColor(hex)).ToUpperInvariant();
            await Host.ExecuteChangeAsync(
                "Акцент: " + normalized,
                transaction =>
                {
                    Services.Theme.SetAccent(transaction, normalized);
                    return Task.CompletedTask;
                },
                "Акцентный цвет изменён.");
        }
        finally
        {
            _busy = false;
            UpdateState();
        }
    }

    private async void AccentFromWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var path = Services.Wallpaper.GetCurrentWallpaperPath();
        if (path is null)
        {
            Host.ShowToast("Не удалось найти текущий файл обоев. Примените обои заново.", true);
            return;
        }

        try
        {
            var hex = Services.Wallpaper.ExtractAccent(path);
            await ApplyAccentAsync(hex);
            Host.ShowToast($"Акцент {hex.ToUpperInvariant()} взят с обоев.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось извлечь акцент из обоев", ex);
            Host.ShowToast("Не удалось определить акцент на этом изображении.", true);
        }
    }

    private async void Fit_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating || _busy || sender is not RadioButton { IsChecked: true, Tag: string fit })
        {
            return;
        }

        if (string.Equals(Services.Config.Current.WallpaperFit, fit, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _busy = true;
        try
        {
            var ok = await Host.ExecuteChangeAsync(
                "Режим обоев: " + WallpaperService.FitDisplayName(fit),
                transaction =>
                {
                    Services.Wallpaper.SetFit(transaction, fit);
                    return Task.CompletedTask;
                },
                "Режим отображения обоев изменён.");
            if (!ok)
            {
                UpdateState();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось изменить режим обоев", ex);
            Host.ShowToast(ex.Message, true);
            UpdateState();
        }
        finally
        {
            _busy = false;
            UpdateState();
        }
    }

    private async void Transparency_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = TransparencyToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            enabled ? "Прозрачность включена" : "Прозрачность выключена",
            transaction =>
            {
                Services.Theme.SetTransparency(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled ? "Прозрачность включена." : "Прозрачность выключена.");
        UpdateState();
    }

    private async void Animations_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = AnimationsToggle.IsChecked == true;
        await Host.ExecuteChangeAsync(
            enabled ? "Анимации включены" : "Анимации выключены",
            transaction =>
            {
                Services.Theme.SetAnimations(transaction, enabled);
                return Task.CompletedTask;
            },
            enabled ? "Анимации включены." : "Анимации выключены.");
        UpdateState();
    }

    private void RebuildWallpapers()
    {
        WallpaperList.Children.Clear();
        var cfg = Services.Config.Current;

        foreach (var preset in WallpaperService.GetPresets())
        {
            var active = !cfg.LastWallpaperIsCustom &&
                         string.Equals(cfg.LastWallpaperKey, preset.Key, StringComparison.OrdinalIgnoreCase);

            var thumbnail = Path.Combine(AppPaths.WallpaperThumbsDir, preset.Key + ".png");
            var source = ImageLoader.Load(thumbnail);

            var background = new Grid();
            background.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x18, 0x1D, 0x28),
                    Color.FromRgb(0x0C, 0x0F, 0x16),
                    new Point(0, 0),
                    new Point(1, 1))
            });

            if (source is not null)
            {
                background.Children.Add(new Image { Source = source, Stretch = Stretch.UniformToFill });
            }

            background.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 58,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0xD9, 0x05, 0x07, 0x0C),
                    new Point(0, 0),
                    new Point(0, 1))
            });

            var label = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(12, 0, 12, 10)
            };
            label.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            label.Children.Add(new TextBlock
            {
                Text = active ? "Применены" : preset.Description,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xE7, 0xED, 0xF8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 196
            });
            background.Children.Add(label);

            var card = new Button
            {
                Style = (Style)FindResource("WallpaperCard"),
                Tag = preset,
                ToolTip = preset.Description,
                Content = background,
                BorderBrush = active
                    ? (Brush)FindResource("AccentBrush")
                    : (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(active ? 2 : 1)
            };
            card.Click += Wallpaper_Click;
            WallpaperList.Children.Add(card);
        }
    }

    private async Task LoadWallpapersAsync()
    {
        _loadingWallpapers = true;
        try
        {
            await Services.Wallpaper.EnsureAllAsync();
            _wallpapersReady = true;
            RebuildWallpapers();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось подготовить обои", ex);
        }
        finally
        {
            _loadingWallpapers = false;
        }
    }

    private async void Wallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WallpaperPreset preset })
        {
            return;
        }

        var ok = await Host.ExecuteChangeAsync(
            "Обои: " + preset.Name,
            async transaction => await Services.Wallpaper.ApplyAsync(transaction, preset),
            $"Обои «{preset.Name}» применены.");
        if (ok)
        {
            UpdateState();
            RebuildWallpapers();
        }
    }

    private async void CustomWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var path = Services.Dialogs.PickImage(Host);
        if (path is null)
        {
            return;
        }

        var ok = await Host.ExecuteChangeAsync(
            "Своё изображение обоев",
            transaction =>
            {
                Services.Wallpaper.ApplyCustom(transaction, path);
                return Task.CompletedTask;
            },
            "Своё изображение применено.");
        if (ok)
        {
            UpdateState();
            RebuildWallpapers();
        }
    }

    private async void RestoreWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (!Host.Confirm("Вернуть исходные обои?",
                "Будет восстановлено изображение и режим отображения, которые были до первого изменения WinTheme Studio.",
                "Вернуть"))
        {
            return;
        }

        var ok = await Host.ExecuteChangeAsync(
            "Восстановление исходных обоев",
            transaction =>
            {
                Services.Wallpaper.RestoreOriginal(transaction);
                return Task.CompletedTask;
            },
            "Исходные обои восстановлены.");
        if (ok)
        {
            UpdateState();
            RebuildWallpapers();
        }
    }
}
