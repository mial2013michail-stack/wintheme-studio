using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinThemeStudio.Core;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio.UI.Views;

public partial class ProfilesView : PageView
{
    private bool _busy;
    private bool _updating;
    private string _previewKey = "";

    public ProfilesView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        LoadOptions();
        BuildScheduler();
        BuildCards();
    }

    private ProfileApplyOptions GetOptions() => new()
    {
        Theme = ApplyThemeToggle.IsChecked == true,
        Wallpaper = ApplyWallpaperToggle.IsChecked == true,
        Taskbar = ApplyTaskbarToggle.IsChecked == true,
        Explorer = ApplyExplorerToggle.IsChecked == true,
        Icons = ApplyIconsToggle.IsChecked == true
    };

    private void LoadOptions()
    {
        _updating = true;
        try
        {
            var cfg = Services.Config.Current;
            ApplyThemeToggle.IsChecked = cfg.ProfileApplyTheme;
            ApplyWallpaperToggle.IsChecked = cfg.ProfileApplyWallpaper;
            ApplyTaskbarToggle.IsChecked = cfg.ProfileApplyTaskbar;
            ApplyExplorerToggle.IsChecked = cfg.ProfileApplyExplorer;
            ApplyIconsToggle.IsChecked = cfg.ProfileApplyIcons;
        }
        finally
        {
            _updating = false;
        }
    }

    private void SaveOptions(ProfileApplyOptions options)
    {
        Services.Config.Update(cfg =>
        {
            cfg.ProfileApplyTheme = options.Theme;
            cfg.ProfileApplyWallpaper = options.Wallpaper;
            cfg.ProfileApplyTaskbar = options.Taskbar;
            cfg.ProfileApplyExplorer = options.Explorer;
            cfg.ProfileApplyIcons = options.Icons;
        });
    }

    private void ApplyOption_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        SaveOptions(GetOptions());
        if (_previewKey.Length > 0)
        {
            BuildPreview(ProfileService.GetProfile(_previewKey));
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _updating = true;
        try
        {
            ApplyThemeToggle.IsChecked = true;
            ApplyWallpaperToggle.IsChecked = true;
            ApplyTaskbarToggle.IsChecked = true;
            ApplyExplorerToggle.IsChecked = true;
            ApplyIconsToggle.IsChecked = true;
        }
        finally
        {
            _updating = false;
        }

        SaveOptions(GetOptions());
        if (_previewKey.Length > 0)
        {
            BuildPreview(ProfileService.GetProfile(_previewKey));
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        _updating = true;
        try
        {
            ApplyThemeToggle.IsChecked = false;
            ApplyWallpaperToggle.IsChecked = false;
            ApplyTaskbarToggle.IsChecked = false;
            ApplyExplorerToggle.IsChecked = false;
            ApplyIconsToggle.IsChecked = false;
        }
        finally
        {
            _updating = false;
        }

        SaveOptions(GetOptions());
        if (_previewKey.Length > 0)
        {
            BuildPreview(ProfileService.GetProfile(_previewKey));
        }
    }

    private void BuildScheduler()
    {
        _updating = true;
        try
        {
            var cfg = Services.Config.Current;
            SchedulerToggle.IsChecked = cfg.SchedulerEnabled;
            SchedulerTimeBox.Text = cfg.SchedulerTime;

            SchedulerProfileBox.Items.Clear();
            var index = 0;
            foreach (var profile in ProfileService.GetProfiles())
            {
                var item = new ComboBoxItem
                {
                    Content = profile.Name,
                    Tag = profile.Key
                };
                SchedulerProfileBox.Items.Add(item);
                if (string.Equals(profile.Key, cfg.SchedulerProfile, StringComparison.OrdinalIgnoreCase))
                {
                    SchedulerProfileBox.SelectedIndex = index;
                }

                index++;
            }

            if (SchedulerProfileBox.SelectedIndex < 0)
            {
                SchedulerProfileBox.SelectedIndex = 0;
            }

            SchedulerStatus.Text = string.IsNullOrWhiteSpace(cfg.SchedulerLastRun)
                ? "Ещё не запускалось."
                : "Последний запуск: " + FormatRun(cfg.SchedulerLastRun) + ".";
        }
        finally
        {
            _updating = false;
        }
    }

    private static string FormatRun(string value)
    {
        return DateTime.TryParse(value, out var date)
            ? date.ToString("dd.MM.yyyy")
            : value;
    }

    private void SchedulerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        SaveScheduler(showToast: false);
    }

    private void SaveScheduler_Click(object sender, RoutedEventArgs e)
    {
        SaveScheduler(showToast: true);
    }

    private void SaveScheduler(bool showToast)
    {
        var timeText = SchedulerTimeBox.Text.Trim();
        if (!SchedulerService.TryParseTime(timeText, out var time))
        {
            Host.ShowToast("Время должно быть в формате ЧЧ:ММ, например 09:00.", true);
            return;
        }

        var key = (SchedulerProfileBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "aurora";
        var enabled = SchedulerToggle.IsChecked == true;
        var normalized = time.ToString(@"hh\:mm");

        Services.Config.Update(cfg =>
        {
            cfg.SchedulerEnabled = enabled;
            cfg.SchedulerProfile = key;
            cfg.SchedulerTime = normalized;
        });

        var profile = ProfileService.GetProfile(key);
        SchedulerTimeBox.Text = normalized;
        SchedulerStatus.Text = enabled
            ? $"Профиль «{profile.Name}» будет применяться ежедневно в {normalized}."
            : "Расписание выключено.";
        if (showToast)
        {
            Host.ShowToast(enabled
                ? $"Расписание сохранено: «{profile.Name}» каждый день в {normalized}."
                : "Расписание выключено.");
        }
    }

    private void BuildCards()
    {
        ProfileCards.Children.Clear();
        var current = Services.Config.Current.LastAppliedProfile;

        foreach (var profile in ProfileService.GetProfiles())
        {
            var active = string.Equals(profile.Key, current, StringComparison.OrdinalIgnoreCase);
            ProfileCards.Children.Add(CreateCard(profile, active));
        }

        ProfileCards.IsEnabled = !_busy;
        if (_previewKey.Length > 0)
        {
            BuildPreview(ProfileService.GetProfile(_previewKey));
        }
    }

    private Border CreateCard(ProfileInfo profile, bool active)
    {
        var preview = CreatePreview(profile);

        var name = new TextBlock
        {
            Text = profile.Name,
            FontFamily = (FontFamily)FindResource("DisplayFont"),
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var tagline = new TextBlock
        {
            Text = profile.Tagline,
            Style = (Style)FindResource("Muted"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var nameStack = new StackPanel();
        nameStack.Children.Add(name);
        nameStack.Children.Add(tagline);

        var accentDot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ThemeService.ParseColor(profile.AccentHex)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        };

        var accentText = new TextBlock
        {
            Text = profile.AccentHex.ToUpperInvariant(),
            Style = (Style)FindResource("Caption"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var accentStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { accentDot, accentText }
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(nameStack);
        Grid.SetColumn(accentStack, 1);
        header.Children.Add(accentStack);

        var description = new TextBlock
        {
            Text = profile.Description,
            Style = (Style)FindResource("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 11, 0, 0)
        };

        var features = new TextBlock
        {
            Text = string.Join("  ·  ", profile.Features),
            Style = (Style)FindResource("Caption"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var applyButton = new Button
        {
            Content = active ? "Применить снова" : "Применить профиль",
            Style = (Style)FindResource(active ? "SecondaryButton" : "PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 9, 16, 9),
            Tag = profile
        };
        applyButton.Click += Apply_Click;

        var previewButton = new Button
        {
            Content = "Предпросмотр",
            Style = (Style)FindResource("SmallButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            Tag = profile
        };
        previewButton.Click += Preview_Click;

        var footer = new Grid { Margin = new Thickness(0, 15, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(applyButton);
        actions.Children.Add(previewButton);
        footer.Children.Add(actions);

        if (active)
        {
            var badge = new Border
            {
                Style = (Style)FindResource("Pill"),
                Background = (Brush)FindResource("SuccessSoftBrush"),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x45, 0xD6, 0xA0)),
                Padding = new Thickness(9, 4, 9, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Активен",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xE9, 0xCB))
                }
            };
            Grid.SetColumn(badge, 1);
            footer.Children.Add(badge);
        }

        var content = new StackPanel { Margin = new Thickness(17, 14, 17, 16) };
        content.Children.Add(header);
        content.Children.Add(description);
        content.Children.Add(features);
        content.Children.Add(footer);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(166) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(preview);
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        return new Border
        {
            Width = 458,
            MinHeight = 348,
            Margin = new Thickness(0, 0, 14, 14),
            Style = (Style)FindResource("HoverCard"),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = root
        };
    }

    private Border CreatePreview(ProfileInfo profile)
    {
        var brush = profile.PreviewKey switch
        {
            "aurora" => FindResource("AuroraPreview") as Brush,
            "aubergine" => FindResource("AuberginePreview") as Brush,
            "minimal" => FindResource("MinimalPreview") as Brush,
            "midnight" => FindResource("MidnightPreview") as Brush,
            "mint" => FindResource("MintPreview") as Brush,
            "sunset" => FindResource("SunsetPreview") as Brush,
            _ => null
        } ?? CreateWindowsPreviewBrush();

        var grid = new Grid();
        grid.Children.Add(new Border { Background = brush });

        var thumbnail = profile.Key switch
        {
            "aurora" => Path.Combine(AppPaths.WallpaperThumbsDir, "aurora.png"),
            "aubergine" => Path.Combine(AppPaths.WallpaperThumbsDir, "aubergine.png"),
            "minimal" => Path.Combine(AppPaths.WallpaperThumbsDir, "graphite.png"),
            "midnight" => Path.Combine(AppPaths.WallpaperThumbsDir, "midnight.png"),
            "mint" => Path.Combine(AppPaths.WallpaperThumbsDir, "mint.png"),
            "sunset" => Path.Combine(AppPaths.WallpaperThumbsDir, "sunset.png"),
            _ => null
        };

        var image = thumbnail is null ? null : ImageLoader.Load(thumbnail);
        if (image is not null)
        {
            grid.Children.Add(new Image
            {
                Source = image,
                Stretch = Stretch.UniformToFill,
                Opacity = 0.82
            });
        }

        grid.Children.Add(new Border
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(0x10, 0, 0, 0),
                Color.FromArgb(0xB0, 0x05, 0x07, 0x0C),
                new Point(0, 0),
                new Point(0, 1))
        });

        var miniDock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12)
        };

        for (var i = 0; i < 7; i++)
        {
            miniDock.Children.Add(new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(Color.FromArgb((byte)(45 + i * 12), 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1)
            });
        }

        grid.Children.Add(miniDock);

        var title = new TextBlock
        {
            Text = profile.Tagline.ToUpperInvariant(),
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(16, 13, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        grid.Children.Add(title);

        return new Border
        {
            Background = brush,
            ClipToBounds = true,
            Child = grid
        };
    }

    private static Brush CreateWindowsPreviewBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xEA, 0xF3, 0xFC), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9, 0xD8, 0xF4), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x6F, 0xAD, 0xE8), 1));
        brush.Freeze();
        return brush;
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProfileInfo profile })
        {
            BuildPreview(profile);
        }
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        _previewKey = "";
        PreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void BuildPreview(ProfileInfo profile)
    {
        _previewKey = profile.Key;
        PreviewPanel.Visibility = Visibility.Visible;
        PreviewTitle.Text = "Предпросмотр профиля «" + profile.Name + "»";

        CurrentPreview.Children.Clear();
        TargetPreview.Children.Clear();

        var cfg = Services.Config.Current;
        var currentDark = Services.Theme.IsDarkMode();
        var currentAccent = Services.Theme.GetAccentHex();
        var currentWallpaper = DescribeCurrentWallpaper(cfg);
        BuildPreviewSide(
            CurrentPreview,
            "Сейчас",
            currentDark ? "Тёмная тема" : "Светлая тема",
            currentAccent,
            currentWallpaper,
            Services.Wallpaper.GetCurrentWallpaperPath());

        BuildPreviewSide(
            TargetPreview,
            "После применения",
            profile.IsDark ? "Тёмная тема" : "Светлая тема",
            profile.AccentHex,
            DescribeProfileWallpaper(profile),
            ProfileThumbnail(profile));

        PlanList.Children.Clear();
        var options = GetOptions();
        var plan = Services.Profiles.Plan(profile.Key, options);
        if (plan.Count == 0)
        {
            PlanList.Children.Add(new TextBlock
            {
                Text = "Не выбран ни один раздел. Отметьте, что нужно применить, в блоке «Что применять».",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap
            });
            PreviewApplyButton.IsEnabled = false;
            PreviewHint.Text = "Профиль не изменит систему, пока не выбран хотя бы один раздел.";
            return;
        }

        PreviewApplyButton.IsEnabled = true;
        PreviewApplyButton.Tag = profile;
        PreviewHint.Text = "Будет изменено: " + options.Describe() + ". Всё записывается в журнал и отменяется.";

        foreach (var item in plan)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var area = new TextBlock
            {
                Text = item.Area,
                Style = (Style)FindResource("SettingTitle"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Top
            };
            row.Children.Add(area);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = item.Title,
                Style = (Style)FindResource("Body"),
                FontSize = 12.5
            });
            text.Children.Add(new TextBlock
            {
                Text = item.Detail,
                Style = (Style)FindResource("Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            PlanList.Children.Add(row);
        }
    }

    private void BuildPreviewSide(
        StackPanel panel,
        string heading,
        string theme,
        string accent,
        string wallpaper,
        string? imagePath)
    {
        panel.Children.Add(new TextBlock
        {
            Text = heading.ToUpperInvariant(),
            Style = (Style)FindResource("Caption"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        });

        var thumb = imagePath is null ? null : ImageLoader.Load(imagePath);
        if (thumb is not null)
        {
            panel.Children.Add(new Border
            {
                Height = 92,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Margin = new Thickness(0, 8, 0, 9),
                BorderBrush = (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                Child = new Image { Source = thumb, Stretch = Stretch.UniformToFill }
            });
        }

        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        themeRow.Children.Add(new TextBlock
        {
            Text = theme,
            Style = (Style)FindResource("SettingTitle"),
            FontSize = 13
        });
        panel.Children.Add(themeRow);

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        accentRow.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ThemeService.ParseColor(accent)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        });
        accentRow.Children.Add(new TextBlock
        {
            Text = accent.ToUpperInvariant(),
            Style = (Style)FindResource("Caption"),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(accentRow);

        panel.Children.Add(new TextBlock
        {
            Text = wallpaper,
            Style = (Style)FindResource("Caption"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });
    }

    private string DescribeCurrentWallpaper(AppConfig cfg)
    {
        var fit = WallpaperService.FitDisplayName(cfg.WallpaperFit);
        if (cfg.LastWallpaperIsCustom)
        {
            return $"Своё изображение · режим «{fit}»";
        }

        if (!string.IsNullOrWhiteSpace(cfg.LastWallpaperKey))
        {
            return $"{WallpaperService.GetPreset(cfg.LastWallpaperKey).Name} · режим «{fit}»";
        }

        return "Исходные обои Windows";
    }

    private static string DescribeProfileWallpaper(ProfileInfo profile)
    {
        if (string.IsNullOrWhiteSpace(profile.WallpaperKey))
        {
            return "Исходные обои Windows";
        }

        var preset = WallpaperService.GetPreset(profile.WallpaperKey);
        return preset.Name + ". " + preset.Description;
    }

    private static string? ProfileThumbnail(ProfileInfo profile)
    {
        if (string.IsNullOrWhiteSpace(profile.WallpaperKey))
        {
            return null;
        }

        var path = Path.Combine(AppPaths.WallpaperThumbsDir, profile.WallpaperKey + ".png");
        return File.Exists(path) ? path : null;
    }

    private async void PreviewApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProfileInfo profile })
        {
            await ApplyProfileAsync(profile);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProfileInfo profile })
        {
            await ApplyProfileAsync(profile);
        }
    }

    private async Task ApplyProfileAsync(ProfileInfo profile)
    {
        if (_busy)
        {
            return;
        }

        var options = GetOptions();
        if (!options.Any)
        {
            Host.ShowToast("Выберите хотя бы один раздел в блоке «Что применять».", true);
            return;
        }

        if (!Host.Confirm(
                "Применить профиль «" + profile.Name + "»?",
                $"Будут изменены: {options.Describe()}. Док не включается. " +
                "Исходные значения сохранятся в журнале, поэтому всё можно отменить.",
                "Применить"))
        {
            return;
        }

        if (!Host.TryBeginOperation())
        {
            return;
        }

        var applied = false;
        _busy = true;
        BusyPanel.Visibility = Visibility.Visible;
        ProfileCards.IsEnabled = false;
        BusyText.Text = "Подготовка профиля…";

        try
        {
            var progress = new Progress<string>(message => BusyText.Text = message);
            var transaction = await Services.Profiles.ApplyAsync(profile.Key, options, progress);
            Host.RefreshJournalState();
            applied = true;
            Host.ShowToast(transaction.EntryCount > 0
                ? $"Профиль «{profile.Name}» применён. Изменений: {transaction.EntryCount}."
                : $"Профиль «{profile.Name}» уже применён.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось применить профиль " + profile.Key, ex);
            Host.ShowToast("Не удалось применить профиль. Подробности в журнале.", true);
        }
        finally
        {
            _busy = false;
            BusyPanel.Visibility = Visibility.Collapsed;
            Host.EndOperation();
            BuildCards();
        }

        if (!applied)
        {
            return;
        }

        if (Host.Confirm("Перезапустить Проводник?",
                "Профиль применён. Чтобы обновить панель задач, меню «Пуск» и значки папок, Проводник нужно перезапустить. Все открытые окна Проводника закроются. Если на экране полноэкранная игра, перезапуск будет отложен.",
                "Перезапустить"))
        {
            try
            {
                await Task.Run(() => Services.Explorer.RestartExplorer());
                Host.ShowToast("Проводник перезапущен. Профиль полностью применён.");
            }
            catch (FullscreenAppActiveException ex)
            {
                Host.ShowToast(ex.Message, true);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось перезапустить Проводник после профиля", ex);
                Host.ShowToast("Профиль применён, но Проводник не удалось перезапустить.", true);
            }
        }
        else
        {
            Host.ShowToast("Профиль применён. Перезапустите Проводник, чтобы обновить панель задач и значки.");
        }
    }
}
