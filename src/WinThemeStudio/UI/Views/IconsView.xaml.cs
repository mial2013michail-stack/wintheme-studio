using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;
using WinThemeStudio.Services;
using WinThemeStudio.UI;

namespace WinThemeStudio.UI.Views;

public partial class IconsView : PageView
{
    private IReadOnlyList<ShortcutItem> _shortcuts = Array.Empty<ShortcutItem>();
    private bool _loadingShortcuts;
    private bool _loadingIcons;
    private string _previewStyle = "";

    private string ActiveIconStyle => Services.Icons.EffectiveStyle;

    public IconsView(MainWindow host, AppServices services) : base(host, services)
    {
        InitializeComponent();
        Services.Journal.Changed += (_, _) =>
        {
            if (IsVisible)
            {
                _ = LoadShortcutsAsync();
            }
        };
    }

    public override void OnShown()
    {
        LoadStudioControls();

        if (IconGrid.Children.Count == 0 ||
            !string.Equals(_previewStyle, ActiveIconStyle, StringComparison.Ordinal))
        {
            BuildIconGrid();
            _ = EnsureIconsAsync();
        }

        UpdateFolderIconState();

        if (!_loadingShortcuts)
        {
            _ = LoadShortcutsAsync();
        }
    }

    private void LoadStudioControls()
    {
        var cfg = Services.Config.Current;
        StyleBox.Items.Clear();
        var styles = new[]
        {
            ("auto", "Авто (по профилю)"),
            ("default", "Default"),
            ("aurora", "Aurora"),
            ("aubergine", "Aubergine"),
            ("minimal", "Minimal"),
            ("midnight", "Midnight"),
            ("mint", "Mint"),
            ("sunset", "Sunset"),
            ("studio", "Студия")
        };

        var index = 0;
        var selected = 0;
        foreach (var (key, name) in styles)
        {
            StyleBox.Items.Add(new ComboBoxItem { Content = name, Tag = key });
            if (string.Equals(key, IconPackService.NormalizeStyle(cfg.IconStyle), StringComparison.Ordinal))
            {
                selected = index;
            }

            index++;
        }

        StyleBox.SelectedIndex = selected;
        TintBox.Text = cfg.IconTint ?? "";
        var radius = cfg.IconRadius < 0 ? 54 : cfg.IconRadius;
        RadiusSlider.Value = radius;
        RadiusValue.Text = cfg.IconRadius < 0 ? "авто" : radius.ToString();
        MonochromeToggle.IsChecked = cfg.IconMonochrome;
        DriveIconsToggle.IsChecked = cfg.UseDriveIcons;
        FullTrashToggle.IsChecked = cfg.UseFullTrashIcon;
    }

    private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RadiusValue is not null)
        {
            RadiusValue.Text = ((int)Math.Round(e.NewValue)).ToString();
        }
    }

    private async void ApplyStudio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var style = (StyleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        var tint = TintBox.Text.Trim();
        if (tint.Length > 0)
        {
            if (!tint.StartsWith('#'))
            {
                tint = "#" + tint;
            }

            try
            {
                ThemeService.ParseColor(tint);
            }
            catch
            {
                Host.ShowToast("Оттенок должен быть HEX-кодом, например #7C5CFF.", true);
                return;
            }
        }

        button.IsEnabled = false;
        StudioStatus.Text = "Сохранение настроек студии…";
        try
        {
            Services.Config.Update(cfg =>
            {
                cfg.IconStyle = style;
                cfg.IconTint = tint;
                cfg.IconRadius = (int)Math.Round(RadiusSlider.Value);
                cfg.IconMonochrome = MonochromeToggle.IsChecked == true;
                cfg.UseDriveIcons = DriveIconsToggle.IsChecked == true;
                cfg.UseFullTrashIcon = FullTrashToggle.IsChecked == true;
            });

            _previewStyle = "";
            await EnsureIconsAsync();
            BuildIconGrid();
            await LoadShortcutsAsync();
            UpdateFolderIconState();
            StudioStatus.Text = style == "studio"
                ? "Студия обновлена: оттенок, скругление и монохром применены к превью."
                : $"Стиль «{ProfileService.StyleName(style)}» выбран. Студия сохранена для ручного стиля.";
            Host.ShowToast("Настройки студии иконок сохранены.");
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось обновить студию иконок", ex);
            StudioStatus.Text = "Не удалось обновить студию. Подробности в журнале.";
            Host.ShowToast("Не удалось обновить студию иконок.", true);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void UpdateFolderIconState()
    {
        var applied = Services.ShellIcons.ArePackIconsApplied(AppPaths.IconPackDir);
        FolderIconState.Text = applied
            ? "Применены значки WinTheme Studio"
            : "Системные значки Windows";
    }

    private async void ApplyFolderIcons_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                var style = ActiveIconStyle;
                var cfg = Services.Config.Current;
                var ok = await Host.ExecuteChangeAsync(
                    "Значки папок, корзины и дисков",
                    transaction =>
                    {
                        Services.ShellIcons.ApplyIcons(
                            transaction,
                            Services.Icons.GetIconPath("folder", style),
                            Services.Icons.GetIconPath("trash", style),
                            cfg.UseFullTrashIcon ? Services.Icons.GetIconPath("trash-full", style) : null,
                            cfg.UseDriveIcons ? Services.Icons.GetIconPath("drive", style) : null,
                            cfg.UseDriveIcons);
                        NativeMethods.RefreshShellIcons();
                        return Task.CompletedTask;
                    },
                    "Значки папок, корзины и дисков обновлены. Перезапустите Проводник, если они не появились сразу.");
                if (ok)
                {
                    UpdateFolderIconState();
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void RestoreFolderIcons_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                var ok = await Host.ExecuteChangeAsync(
                    "Возврат значков папок и корзины",
                    transaction =>
                    {
                        Services.ShellIcons.RemoveOwned(transaction, AppPaths.IconPackDir);
                        NativeMethods.RefreshShellIcons();
                        return Task.CompletedTask;
                    },
                    "Системные значки папок и корзины восстановлены. Перезапустите Проводник, если они не появились сразу.");
                if (ok)
                {
                    UpdateFolderIconState();
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    private void BuildIconGrid()
    {
        var style = ActiveIconStyle;
        _previewStyle = style;
        IconGrid.Children.Clear();
        var custom = Services.Config.Current.CustomIconPaths;

        foreach (var definition in IconPackService.GetDefinitions())
        {
            var preview = ImageLoader.Load(Services.Icons.GetPreviewPath(definition.Key, style));
            var stack = new StackPanel();

            if (preview is not null)
            {
                stack.Children.Add(new Image
                {
                    Source = preview,
                    Width = 48,
                    Height = 48,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "◆",
                    FontSize = 28,
                    Foreground = (Brush)FindResource("AccentTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Height = 48
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = definition.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = definition.Description,
                FontSize = 9.5,
                Foreground = (Brush)FindResource("DimBrush"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            var hasCustom = custom.ContainsKey(definition.Key) && File.Exists(custom[definition.Key]);
            if (hasCustom)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "СВОЙ",
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AccentTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            var customButton = new Button
            {
                Content = hasCustom ? "Сбросить" : "Своё…",
                Style = (Style)FindResource("LinkButton"),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Tag = definition.Key
            };
            customButton.Click += CustomIcon_Click;
            stack.Children.Add(customButton);

            IconGrid.Children.Add(new Border
            {
                Style = (Style)FindResource("IconTile"),
                Width = 112,
                Height = 148,
                Padding = new Thickness(8, 10, 8, 8),
                ToolTip = definition.Description,
                Child = stack
            });
        }
    }

    private async void CustomIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var custom = Services.Config.Current.CustomIconPaths;
        if (custom.ContainsKey(key) && File.Exists(custom[key]))
        {
            Services.Config.Update(cfg => cfg.CustomIconPaths.Remove(key));
            await RegeneratePackAsync("Свой значок сброшен. Используется значок студии.");
            return;
        }

        var path = Services.Dialogs.PickIcon(Window.GetWindow(this));
        if (path is null)
        {
            return;
        }

        Services.Config.Update(cfg => cfg.CustomIconPaths[key] = path);
        await RegeneratePackAsync("Свой значок добавлен в студию. Он используется в стиле «Студия».");
    }

    private async Task RegeneratePackAsync(string message)
    {
        try
        {
            _previewStyle = "";
            await Services.Icons.EnsurePackAsync();
            BuildIconGrid();
            await LoadShortcutsAsync();
            Host.ShowToast(message);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось обновить свой значок", ex);
            Host.ShowToast("Не удалось обновить набор значков.", true);
        }
    }

    private async Task EnsureIconsAsync()
    {
        if (_loadingIcons)
        {
            return;
        }

        _loadingIcons = true;
        try
        {
            await Services.Icons.EnsurePackAsync();
            BuildIconGrid();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось подготовить набор иконок", ex);
        }
        finally
        {
            _loadingIcons = false;
        }
    }

    private async Task LoadShortcutsAsync()
    {
        if (_loadingShortcuts)
        {
            return;
        }

        _loadingShortcuts = true;
        ShortcutCountLabel.Text = "Загрузка ярлыков…";
        try
        {
            _shortcuts = await Services.Shortcuts.LoadAsync(ActiveIconStyle);
            BuildShortcutList();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось загрузить ярлыки", ex);
            ShortcutCountLabel.Text = "Не удалось прочитать ярлыки";
            Host.ShowToast("Не удалось прочитать ярлыки рабочего стола.", true);
        }
        finally
        {
            _loadingShortcuts = false;
        }
    }

    private void BuildShortcutList()
    {
        ShortcutList.Children.Clear();
        var modified = _shortcuts.Count(item => item.IsModified);
        ShortcutCountLabel.Text = _shortcuts.Count == 0
            ? "Ярлыки не найдены"
            : $"{_shortcuts.Count} {PluralShortcuts(_shortcuts.Count)} · изменено {modified}";

        if (_shortcuts.Count == 0)
        {
            ShortcutList.Children.Add(new TextBlock
            {
                Text = "На рабочем столе и панели задач не найдено ярлыков .lnk. Создайте ярлык или закрепите приложение.",
                Style = (Style)FindResource("Muted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8)
            });
            return;
        }

        foreach (var item in _shortcuts)
        {
            ShortcutList.Children.Add(CreateShortcutRow(item));
        }
    }

    private Border CreateShortcutRow(ShortcutItem item)
    {
        var iconSource = item.Icon ?? ImageLoader.Load(Services.Icons.GetPreviewPath(item.CategoryKey, ActiveIconStyle));
        var icon = new Image
        {
            Source = iconSource,
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };

        var location = item.LocationKey switch
        {
            "taskbar" => "Панель задач",
            "startmenu" => "Меню «Пуск»",
            _ => "Рабочий стол"
        };
        var name = new TextBlock
        {
            Text = item.Name,
            Style = (Style)FindResource("SettingTitle"),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var details = new TextBlock
        {
            Text = location + " · " + item.TargetDisplay + (item.IsModified ? " · изменён" : ""),
            Style = (Style)FindResource("Caption"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 10, 0) };
        info.Children.Add(name);
        info.Children.Add(details);

        var combo = new ComboBox
        {
            Style = (Style)FindResource("ComboBoxStyle"),
            Width = 164,
            SelectedValuePath = "Tag",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        ComboBoxItem? selected = null;
        foreach (var definition in IconPackService.GetDefinitions())
        {
            var option = new ComboBoxItem { Content = definition.Name, Tag = definition.Key };
            combo.Items.Add(option);
            if (string.Equals(definition.Key, item.CategoryKey, StringComparison.OrdinalIgnoreCase))
            {
                selected = option;
            }
        }

        combo.SelectedItem = selected ?? combo.Items[0];

        var apply = new Button
        {
            Content = "Применить",
            Style = (Style)FindResource("SmallButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        apply.Click += async (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: string iconKey })
            {
                return;
            }

            apply.IsEnabled = false;
            try
            {
                var ok = await Host.ExecuteChangeAsync(
                    "Иконка ярлыка: " + item.Name,
                    transaction =>
                    {
                        Services.Shortcuts.ApplyIcon(transaction, item, iconKey, ActiveIconStyle);
                        return Task.CompletedTask;
                    },
                    "Иконка ярлыка обновлена.");
                if (ok)
                {
                    BuildShortcutList();
                }
            }
            finally
            {
                apply.IsEnabled = true;
            }
        };

        var restore = new Button
        {
            Content = "Сбросить",
            Style = (Style)FindResource("GhostButton"),
            FontSize = 11.5,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = item.IsModified
        };
        restore.Click += async (_, _) =>
        {
            restore.IsEnabled = false;
            try
            {
                var ok = await Host.ExecuteChangeAsync(
                    "Возврат иконки: " + item.Name,
                    transaction =>
                    {
                        Services.Shortcuts.RestoreOriginal(transaction, item);
                        return Task.CompletedTask;
                    },
                    "Исходная иконка ярлыка восстановлена.");
                if (ok)
                {
                    BuildShortcutList();
                }
            }
            finally
            {
                restore.IsEnabled = true;
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        Grid.SetColumn(combo, 2);
        grid.Children.Add(combo);
        Grid.SetColumn(apply, 3);
        grid.Children.Add(apply);
        Grid.SetColumn(restore, 4);
        grid.Children.Add(restore);

        return new Border
        {
            Style = (Style)FindResource("SettingRow"),
            Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(12, 9, 10, 9),
            Child = grid
        };
    }

    private async void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_shortcuts.Count == 0)
        {
            Host.ShowToast("Ярлыки не найдены. Обновите список позже.");
            return;
        }

        if (!Host.Confirm("Применить иконки ко всем ярлыкам?",
                $"Будет изменено ярлыков: {_shortcuts.Count} (рабочий стол, панель задач и меню «Пуск»). Для каждого ярлыка, который уже менялся, сохраняется исходная копия. Значки приложений и файлы на диске не затрагиваются.",
                "Применить ко всем"))
        {
            return;
        }

        var result = new ShortcutApplyResult(0, 0, 0);
        var ok = await Host.ExecuteChangeAsync(
            "Иконки всех ярлыков",
            transaction =>
            {
                result = Services.Shortcuts.ApplyToAll(transaction, _shortcuts, ActiveIconStyle);
                return Task.CompletedTask;
            },
            "Иконки ярлыков обновлены.");
        if (ok)
        {
            BuildShortcutList();
            if (result.Failed > 0)
            {
                Host.ShowToast($"Не удалось изменить ярлыков: {result.Failed}. Подробности в журнале приложения.", true);
            }
            else
            {
                Host.ShowToast($"Обновлено ярлыков: {result.Applied}, уже были с этими значками: {result.Skipped}.");
            }
        }
    }

    private async void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var modified = _shortcuts.Count(item => item.IsModified);
        if (modified == 0)
        {
            Host.ShowToast("Среди ярлыков нет изменённых иконок.");
            return;
        }

        if (!Host.Confirm("Вернуть исходные иконки?",
                $"Будет восстановлено ярлыков: {modified}. Исходные копии ярлыков находятся в личной папке приложения.",
                "Вернуть исходные"))
        {
            return;
        }

        var result = new ShortcutApplyResult(0, 0, 0);
        var ok = await Host.ExecuteChangeAsync(
            "Возврат иконок ярлыков",
            transaction =>
            {
                result = Services.Shortcuts.RestoreAll(transaction, _shortcuts);
                return Task.CompletedTask;
            },
            "Исходные иконки ярлыков восстановлены.");
        if (ok)
        {
            BuildShortcutList();
            if (result.Failed > 0)
            {
                Host.ShowToast($"Не удалось восстановить ярлыков: {result.Failed}. Подробности в журнале приложения.", true);
            }
        }
    }

    private async void RefreshIcons_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                _previewStyle = "";
                await EnsureIconsAsync();
                BuildIconGrid();
                await LoadShortcutsAsync();
                Host.ShowToast("Превью иконок и список ярлыков обновлены.");
            }
            finally
            {
                button.IsEnabled = true;
            }
        }
    }

    private void OpenIconsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.IconPackDir);
            Process.Start(new ProcessStartInfo(AppPaths.IconPackDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("Не удалось открыть папку иконок: " + ex.Message);
            Host.ShowToast("Не удалось открыть папку иконок.", true);
        }
    }

    private static string PluralShortcuts(int count)
    {
        var tail = count % 100;
        var last = count % 10;
        return tail is >= 11 and <= 14
            ? "ярлыков"
            : last switch
            {
                1 => "ярлык",
                2 or 3 or 4 => "ярлыка",
                _ => "ярлыков"
            };
    }
}
