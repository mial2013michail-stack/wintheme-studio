using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinThemeStudio.Core;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio.UI.Views;

public partial class DashboardView : PageView
{
    public DashboardView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        var cfg = Services.Config.Current;
        var profile = ProfileService.GetProfile(cfg.LastAppliedProfile);
        var dark = Services.Theme.IsDarkMode();
        var accent = Services.Theme.GetAccentColor();
        var wallpaperPath = Services.Wallpaper.GetCurrentWallpaperPath();

        CurrentProfileName.Text = profile.Name;
        CurrentProfileTag.Text = profile.Tagline;
        ProfileAccentDot.Fill = new SolidColorBrush(ThemeService.ParseColor(profile.AccentHex));
        ProfileAccentHex.Text = profile.AccentHex.ToUpperInvariant();

        ThemeValue.Text = dark ? "Тёмная" : "Светлая";
        ThemeHint.Text = dark ? "Тёмное оформление" : "Светлое оформление";

        AccentDot.Background = new SolidColorBrush(accent);
        AccentValue.Text = ThemeService.ToHex(accent).ToUpperInvariant();

        var preset = string.IsNullOrWhiteSpace(cfg.LastWallpaperKey)
            ? null
            : WallpaperService.GetPreset(cfg.LastWallpaperKey);
        WallpaperValue.Text = cfg.LastWallpaperIsCustom
            ? "Своё изображение"
            : preset?.Name ?? "Обои Windows";
        WallpaperHint.Text = cfg.LastWallpaperIsCustom
            ? "Применено из файла"
            : preset?.Description ?? "Исходные обои сохранены";

        var changes = Services.Journal.ActiveTransactionCount;
        ChangesValue.Text = changes.ToString();
        ChangesHint.Text = changes == 0 ? "Изменений нет" : "Можно отменить";

        HeroImage.Source = wallpaperPath is not null
            ? ImageLoader.Load(wallpaperPath)
            : preset is not null
                ? ImageLoader.Load(Path.Combine(AppPaths.WallpaperThumbsDir, preset.Key + ".png"))
                : null;

        var overlayParts = new List<string>();
        if (cfg.DockEnabled)
        {
            overlayParts.Add("док");
        }

        OverlayValue.Text = overlayParts.Count == 0
            ? "Док выключен. Включите его на странице «Док», если нужна плавающая панель приложений."
            : "Активен док. Он автоматически скрывается в полноэкранных играх.";

        BuildJournalList();
    }

    private void BuildJournalList()
    {
        JournalListPanel.Children.Clear();
        var items = Services.Journal.ActiveTransactions().Take(4).ToList();
        if (items.Count == 0)
        {
            JournalListPanel.Children.Add(new TextBlock
            {
                Text = "Пока ничего не менялось. Примените профиль или настройте внешний вид — здесь появится история.",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return;
        }

        foreach (var transaction in items)
        {
            var title = new TextBlock
            {
                Text = transaction.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var subtitle = new TextBlock
            {
                Text = $"{transaction.CreatedDisplay} · {PluralEntries(transaction.EntryCount)}",
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(0, 3, 0, 0)
            };

            var text = new StackPanel();
            text.Children.Add(title);
            text.Children.Add(subtitle);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);

            var badge = new Border
            {
                Style = (Style)FindResource("Pill"),
                Background = (Brush)FindResource("SuccessSoftBrush"),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x45, 0xD6, 0xA0)),
                Padding = new Thickness(9, 3, 9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Применено",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xE9, 0xCB))
                }
            };
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);

            JournalListPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
                BorderBrush = (Brush)FindResource("StrokeBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 6),
                Child = row
            });
        }
    }

    private static string PluralEntries(int count)
    {
        var tail = count % 100;
        var last = count % 10;
        var word = tail is >= 11 and <= 14
            ? "изменений"
            : last switch
            {
                1 => "изменение",
                2 or 3 or 4 => "изменения",
                _ => "изменений"
            };
        return $"{count} {word}";
    }

    private void OpenProfiles_Click(object sender, RoutedEventArgs e) => Host.Navigate("profiles");

    private void OpenAppearance_Click(object sender, RoutedEventArgs e) => Host.Navigate("appearance");

    private void OpenIcons_Click(object sender, RoutedEventArgs e) => Host.Navigate("icons");

    private void OpenDock_Click(object sender, RoutedEventArgs e) => Host.Navigate("dock");

    private void OpenRestore_Click(object sender, RoutedEventArgs e) => Host.Navigate("restore");
}
