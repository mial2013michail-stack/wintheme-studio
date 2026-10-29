using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinThemeStudio.Core;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio.UI.Views;

public partial class DockView : PageView
{
    private static readonly (string Target, string Name)[] Builtins =
    {
        ("explorer", "Проводник"),
        ("browser", "Браузер"),
        ("terminal", "Терминал"),
        ("settings", "Параметры"),
        ("notepad", "Блокнот"),
        ("taskmgr", "Диспетчер задач"),
        ("trash", "Корзина")
    };

    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private bool _updating;

    public DockView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
        _sizeTimer.Tick += (_, _) =>
        {
            _sizeTimer.Stop();
            var cfg = Services.Config.Current;
            cfg.DockSize = Math.Round(DockSizeSlider.Value);
            Services.Config.Save();
            Services.Overlays.Refresh();
            UpdateStatus();
        };
    }

    public override void OnShown()
    {
        var cfg = Services.Config.Current;
        _updating = true;
        try
        {
            DockEnabledToggle.IsChecked = cfg.DockEnabled;
            DockBottom.IsChecked = cfg.DockPosition == "bottom";
            DockLeft.IsChecked = cfg.DockPosition == "left";
            DockRight.IsChecked = cfg.DockPosition == "right";
            DockSizeSlider.Value = cfg.DockSize;
            DockSizeLabel.Text = $"{(int)cfg.DockSize} px";
            DockAutoHideToggle.IsChecked = cfg.DockAutoHide;
            DockBlurToggle.IsChecked = cfg.DockBlur;
            DockMagnifyToggle.IsChecked = cfg.DockMagnify;
            DockFullscreenToggle.IsChecked = cfg.DockHideFullscreen;
        }
        finally
        {
            _updating = false;
        }

        BuildBuiltinCombo();
        BuildItemsPanel();
        UpdateStatus();
    }

    private void BuildBuiltinCombo()
    {
        var selected = BuiltinCombo.SelectedItem as ComboBoxItem;
        var selectedTarget = selected?.Tag as string;
        BuiltinCombo.Items.Clear();
        foreach (var (target, name) in Builtins)
        {
            BuiltinCombo.Items.Add(new ComboBoxItem { Content = name, Tag = target });
        }

        BuiltinCombo.SelectedItem = BuiltinCombo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selectedTarget, StringComparison.Ordinal))
            ?? BuiltinCombo.Items[0];
    }

    private void BuildItemsPanel()
    {
        DockItemsPanel.Children.Clear();
        var items = Services.Config.Current.DockItems;
        if (items.Count == 0)
        {
            DockItemsPanel.Children.Add(new TextBlock
            {
                Text = "Док пуст. Добавьте встроенный элемент или своё приложение.",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 8)
            });
            return;
        }

        foreach (var item in items)
        {
            DockItemsPanel.Children.Add(CreateItemRow(item));
        }
    }

    private Border CreateItemRow(DockItemConfig item)
    {
        var iconKey = item.Kind == "builtin"
            ? BuiltinIconKey(item.Target)
            : IconPackService.GuessKey(item.Target, item.Name);
        var icon = ImageLoader.Load(Services.Icons.GetPreviewPath(
            iconKey,
            IconPackService.NormalizeStyle(Services.Config.Current.LastAppliedProfile)));

        var image = new Image
        {
            Source = icon,
            Width = 28,
            Height = 28,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var kind = item.Kind == "builtin"
            ? "Встроенный элемент"
            : item.Kind == "url" ? "Ссылка" : "Приложение";
        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name,
            Style = (Style)FindResource("SettingTitle"),
            FontSize = 13
        };
        var details = new TextBlock
        {
            Text = kind + " · " + (string.IsNullOrWhiteSpace(item.Target) ? "без цели" : item.Target),
            Style = (Style)FindResource("Caption"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        info.Children.Add(name);
        info.Children.Add(details);

        var up = MakeRowButton("↑", "Выше");
        up.Click += (_, _) => MoveItem(item, -1);

        var down = MakeRowButton("↓", "Ниже");
        down.Click += (_, _) => MoveItem(item, 1);

        var remove = MakeRowButton("×", "Удалить из дока");
        remove.Foreground = (Brush)FindResource("DangerBrush");
        remove.FontSize = 17;
        remove.Click += (_, _) => RemoveItem(item);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(image);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(up, 2);
        grid.Children.Add(up);
        Grid.SetColumn(down, 3);
        grid.Children.Add(down);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(remove);

        return new Border
        {
            Style = (Style)FindResource("SettingRow"),
            Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(11, 8, 8, 8),
            Child = grid
        };
    }

    private static Button MakeRowButton(string content, string tooltip) => new()
    {
        Content = content,
        Style = (Style)Application.Current.FindResource("GhostButton"),
        Width = 29,
        Height = 29,
        Padding = new Thickness(0),
        FontSize = 14,
        ToolTip = tooltip,
        Margin = new Thickness(2, 0, 0, 0)
    };

    private static string BuiltinIconKey(string target) => target switch
    {
        "explorer" => "folder",
        "browser" => "browser",
        "terminal" => "terminal",
        "settings" => "settings",
        "notepad" => "document",
        "taskmgr" => "settings",
        "trash" => "trash",
        _ => "app"
    };

    private void UpdateStatus()
    {
        var cfg = Services.Config.Current;
        var position = cfg.DockPosition switch
        {
            "left" => "слева",
            "right" => "справа",
            _ => "снизу"
        };

        if (cfg.DockEnabled)
        {
            DockStatus.Text = $"Док включён · {position} · {cfg.DockItems.Count} элементов · " +
                              (cfg.DockHideFullscreen ? "скрывается в полноэкранных играх" : "виден поверх приложений");
        }
        else
        {
            DockStatus.Text = "Док выключен. Включите его, чтобы получить плавающую панель приложений.";
        }
    }

    private void SaveAndRefresh()
    {
        Services.Config.Save();
        Services.Overlays.Refresh();
        UpdateStatus();
    }

    private void DockEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var enabled = DockEnabledToggle.IsChecked == true;
        var cfg = Services.Config.Current;
        cfg.DockEnabled = enabled;
        if (enabled && cfg.DockItems.Count == 0)
        {
            cfg.DockItems = ConfigService.CreateDefaultDockItems();
            BuildItemsPanel();
        }

        SaveAndRefresh();
    }

    private void DockPosition_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not RadioButton { IsChecked: true, Tag: string position })
        {
            return;
        }

        Services.Config.Current.DockPosition = position;
        SaveAndRefresh();
    }

    private void DockSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DockSizeLabel is null)
        {
            return;
        }

        DockSizeLabel.Text = $"{(int)Math.Round(e.NewValue)} px";
        if (_updating)
        {
            return;
        }

        _sizeTimer.Stop();
        _sizeTimer.Start();
    }

    private void DockAutoHide_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        Services.Config.Current.DockAutoHide = DockAutoHideToggle.IsChecked == true;
        SaveAndRefresh();
    }

    private void DockBlur_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        Services.Config.Current.DockBlur = DockBlurToggle.IsChecked == true;
        SaveAndRefresh();
    }

    private void DockMagnify_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        Services.Config.Current.DockMagnify = DockMagnifyToggle.IsChecked == true;
        SaveAndRefresh();
    }

    private void DockFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        Services.Config.Current.DockHideFullscreen = DockFullscreenToggle.IsChecked == true;
        SaveAndRefresh();
    }

    private void AddBuiltin_Click(object sender, RoutedEventArgs e)
    {
        if (BuiltinCombo.SelectedItem is not ComboBoxItem { Tag: string target })
        {
            return;
        }

        var cfg = Services.Config.Current;
        if (cfg.DockItems.Any(item => item.Kind == "builtin" &&
                                      string.Equals(item.Target, target, StringComparison.OrdinalIgnoreCase)))
        {
            Host.ShowToast("Этот элемент уже есть в доке.");
            return;
        }

        var name = Builtins.First(item => item.Target == target).Name;
        cfg.DockItems.Add(new DockItemConfig { Name = name, Kind = "builtin", Target = target });
        SaveAndRefresh();
        BuildItemsPanel();
        Host.ShowToast($"«{name}» добавлен в док.");
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var path = Services.Dialogs.PickExecutable(Host);
        if (path is null)
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Приложение";
        }

        var cfg = Services.Config.Current;
        cfg.DockItems.Add(new DockItemConfig
        {
            Name = name,
            Kind = "shortcut",
            Target = path
        });
        SaveAndRefresh();
        BuildItemsPanel();
        Host.ShowToast($"«{name}» добавлен в док.");
    }

    private void ResetItems_Click(object sender, RoutedEventArgs e)
    {
        if (!Host.Confirm("Сбросить содержимое дока?",
                "В док вернётся стандартный набор встроенных приложений. Добавленные вручную ярлыки будут убраны из дока; файлы приложений не затрагиваются.",
                "Сбросить"))
        {
            return;
        }

        Services.Config.Current.DockItems = ConfigService.CreateDefaultDockItems();
        SaveAndRefresh();
        BuildItemsPanel();
        Host.ShowToast("Содержимое дока сброшено.");
    }

    private void MoveItem(DockItemConfig item, int delta)
    {
        var items = Services.Config.Current.DockItems;
        var index = items.FindIndex(candidate => candidate.Id == item.Id);
        if (index < 0)
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        SaveAndRefresh();
        BuildItemsPanel();
    }

    private void RemoveItem(DockItemConfig item)
    {
        var items = Services.Config.Current.DockItems;
        items.RemoveAll(candidate => candidate.Id == item.Id);
        SaveAndRefresh();
        BuildItemsPanel();
        Host.ShowToast("Элемент убран из дока.");
    }
}
