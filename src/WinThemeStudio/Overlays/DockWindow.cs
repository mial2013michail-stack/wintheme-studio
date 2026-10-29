using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinThemeStudio.Core;
using WinThemeStudio.Interop;
using WinThemeStudio.Models;
using WinThemeStudio.Services;

namespace WinThemeStudio.Overlays;

public sealed class DockWindow : Window
{
    private readonly ConfigService _config;
    private readonly IconPackService _icons;
    private readonly Border _outer;
    private readonly Border _panel;
    private readonly StackPanel _itemsPanel;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly DispatcherTimer _introTimer;
    private readonly Dictionary<string, BitmapSource?> _iconCache = new();

    private double _iconSize = 48;
    private bool _autoHide;
    private bool _hideFullscreen;
    private bool _magnify;
    private bool _blur;
    private string _position = "bottom";
    private bool _suppressed;
    private bool _visible;

    public DockWindow(ConfigService config, IconPackService icons)
    {
        _config = config;
        _icons = icons;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        SnapsToDevicePixels = true;

        _outer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Padding = new Thickness(10)
        };
        _panel = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 5,
                Opacity = 0.42,
                Color = Colors.Black
            }
        };
        _itemsPanel = new StackPanel();
        _panel.Child = _itemsPanel;
        _outer.Child = _panel;
        Content = _outer;

        MouseEnter += OnMouseEnter;
        MouseLeave += OnMouseLeave;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_autoHide && !IsMouseOver)
            {
                HideDock();
            }
        };

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _fullscreenTimer.Tick += OnFullscreenTick;
        _fullscreenTimer.Start();

        _introTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
        _introTimer.Tick += (_, _) =>
        {
            _introTimer.Stop();
            if (_autoHide && !IsMouseOver)
            {
                HideDock();
            }
        };

        SourceInitialized += (_, _) =>
        {
            NativeMethods.MakeOverlayWindow(this);
            NativeMethods.ApplyOverlayBackdrop(this, _blur);
        };

        Reload();
        _visible = true;
        if (_hideFullscreen)
        {
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            if (IsForegroundFullscreen())
            {
                // Do not flash the overlay over a game that is already running.
                _suppressed = true;
                return;
            }
        }

        Show();
        _introTimer.Start();
    }

    public void Reload()
    {
        _autoHide = _config.Current.DockAutoHide;
        _hideFullscreen = _config.Current.DockHideFullscreen;
        _magnify = _config.Current.DockMagnify;
        _blur = _config.Current.DockBlur;
        _position = _config.Current.DockPosition;
        _iconSize = Math.Clamp(_config.Current.DockSize, 32, 84);

        _panel.Background = new SolidColorBrush(_blur
            ? Color.FromArgb(0xD9, 0x0F, 0x12, 0x18)
            : Color.FromArgb(0xF5, 0x10, 0x13, 0x19));
        _panel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        _itemsPanel.Orientation = _position is "left" or "right" ? Orientation.Vertical : Orientation.Horizontal;

        // Drop images first: building the items below reads the preview paths and
        // must not reuse icons from a previous pack or size.
        _iconCache.Clear();

        _itemsPanel.Children.Clear();
        var items = _config.Current.DockItems;
        if (items.Count == 0)
        {
            items = new List<DockItemConfig>
            {
                new() { Name = "Проводник", Kind = "builtin", Target = "explorer" }
            };
        }

        foreach (var item in items)
        {
            _itemsPanel.Children.Add(CreateDockItem(item));
        }

        var extent = _iconSize + 18;
        var panelThickness = 16 + 20 + 2;
        if (_position is "left" or "right")
        {
            Width = _iconSize + 46;
            Height = items.Count * extent + panelThickness;
        }
        else
        {
            Width = items.Count * extent + panelThickness;
            Height = _iconSize + 46;
        }

        // Config changes can happen while an overlay is suppressed by a
        // fullscreen app. Once the option is switched off the dock must come
        // back without waiting for the foreground window to change again.
        if (!_hideFullscreen && _suppressed)
        {
            _suppressed = false;
            if (!IsVisible)
            {
                Show();
            }

            _visible = true;
        }

        if (!_autoHide)
        {
            _hideTimer.Stop();
            _introTimer.Stop();
            if (!_suppressed)
            {
                _visible = true;
            }
        }

        if (IsVisible)
        {
            NativeMethods.ApplyOverlayBackdrop(this, _blur);
            PositionWindow(_visible && !_suppressed);
        }

        if (_autoHide && IsVisible && !_suppressed)
        {
            // Give the user a moment to see the dock after changing settings
            // before auto-hide takes it away again.
            _introTimer.Stop();
            _introTimer.Start();
        }
    }

    public void ShowOverlay()
    {
        if (_hideFullscreen && (_suppressed || IsForegroundFullscreen()))
        {
            _suppressed = true;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        _visible = true;
        PositionWindow(true);
        if (_autoHide)
        {
            _introTimer.Stop();
            _introTimer.Start();
        }
    }

    private bool IsForegroundFullscreen()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        return handle != IntPtr.Zero && NativeMethods.IsForegroundFullscreen(handle);
    }

    private Border CreateDockItem(DockItemConfig item)
    {
        var image = new Image
        {
            Source = LoadIcon(item),
            Stretch = Stretch.Uniform,
            Width = _iconSize,
            Height = _iconSize
        };

        var host = new Border
        {
            Width = _iconSize + 8,
            Height = _iconSize + 8,
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            Padding = new Thickness(4),
            Margin = new Thickness(5, 0, 5, 0),
            Cursor = Cursors.Hand,
            ToolTip = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name,
            Child = image,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        if (_position is "left" or "right")
        {
            host.Margin = new Thickness(0, 5, 0, 5);
        }

        host.MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            host.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            if (_magnify)
            {
                AnimateScale(host, 1.22);
            }
        };

        host.MouseLeave += (_, _) =>
        {
            host.Background = Brushes.Transparent;
            if (_magnify)
            {
                AnimateScale(host, 1.0);
            }

            if (_autoHide)
            {
                _hideTimer.Start();
            }
        };

        host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Launch(item);
            if (_autoHide)
            {
                _hideTimer.Start();
            }
        };

        return host;
    }

    private BitmapSource? LoadIcon(DockItemConfig item)
    {
        var key = item.Kind switch
        {
            "builtin" => BuiltinIconKey(item.Target),
            "url" => "browser",
            _ => IconPackService.GuessKey(item.Target, item.Name)
        };

        if (_iconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? source = null;
        var style = IconPackService.NormalizeStyle(_config.Current.LastAppliedProfile);
        var preview = _icons.GetPreviewPath(key, style);
        if (File.Exists(preview))
        {
            source = LoadBitmap(preview);
        }

        if (source is null && item.Kind is "file" or "shortcut" && File.Exists(item.Target))
        {
            source = NativeMethods.GetFileIcon(item.Target, 96);
        }

        if (source is null)
        {
            var fallback = _icons.GetPreviewPath("app", style);
            source = File.Exists(fallback) ? LoadBitmap(fallback) : null;
        }

        _iconCache[key] = source;
        return source;
    }

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

    private void Launch(DockItemConfig item)
    {
        try
        {
            if (string.Equals(item.Kind, "builtin", StringComparison.OrdinalIgnoreCase))
            {
                switch (item.Target)
                {
                    case "explorer":
                        Start("explorer.exe");
                        break;
                    case "browser":
                        Start("https://www.bing.com");
                        break;
                    case "terminal":
                        Start("wt.exe", fallback: "powershell.exe");
                        break;
                    case "settings":
                        Start("ms-settings:");
                        break;
                    case "notepad":
                        Start("notepad.exe");
                        break;
                    case "taskmgr":
                        Start("taskmgr.exe");
                        break;
                    case "trash":
                        Start("explorer.exe", "shell:RecycleBinFolder");
                        break;
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(item.Target))
            {
                Start(item.Target, item.Arguments);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Не удалось запустить {item.Name}: {ex.Message}");
        }
    }

    private static void Start(string file, string? arguments = null, string? fallback = null)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments ?? "",
                UseShellExecute = true
            };
            Process.Start(info);
        }
        catch when (fallback is not null)
        {
            Process.Start(new ProcessStartInfo(fallback) { UseShellExecute = true });
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _hideTimer.Stop();
        _introTimer.Stop();
        ShowDock();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_autoHide)
        {
            _hideTimer.Start();
        }
    }

    private void ShowDock()
    {
        if (_suppressed)
        {
            if (_hideFullscreen)
            {
                return;
            }

            _suppressed = false;
            if (!IsVisible)
            {
                Show();
            }
        }

        _visible = true;
        PositionWindow(true, animate: true);
    }

    private void HideDock()
    {
        _visible = false;
        PositionWindow(false, animate: true);
    }

    private void PositionWindow(bool visible, bool animate = false)
    {
        var area = SystemParameters.WorkArea;
        double left;
        double top;

        if (_position == "left")
        {
            left = visible ? area.Left + 8 : area.Left - Width + 14;
            top = area.Top + (area.Height - Height) / 2;
        }
        else if (_position == "right")
        {
            left = visible ? area.Right - Width - 8 : area.Right - 14;
            top = area.Top + (area.Height - Height) / 2;
        }
        else
        {
            left = area.Left + (area.Width - Width) / 2;
            top = visible ? area.Bottom - Height - 8 : area.Bottom - 14;
        }

        if (animate)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(LeftProperty, new DoubleAnimation(left, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = ease
            });
            BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = ease
            });
        }
        else
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = left;
            Top = top;
        }
    }

    private void OnFullscreenTick(object? sender, EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!_hideFullscreen)
        {
            if (_suppressed)
            {
                _suppressed = false;
                if (!IsVisible)
                {
                    Show();
                }

                _visible = true;
                PositionWindow(true);
                if (_autoHide)
                {
                    _introTimer.Stop();
                    _introTimer.Start();
                }
            }

            return;
        }

        if (NativeMethods.IsForegroundFullscreen(handle))
        {
            if (!_suppressed)
            {
                _suppressed = true;
                _hideTimer.Stop();
                _introTimer.Stop();
                Hide();
            }
        }
        else if (_suppressed)
        {
            _suppressed = false;
            if (!IsVisible)
            {
                Show();
            }

            _visible = true;
            PositionWindow(true);
            if (_autoHide)
            {
                _introTimer.Stop();
                _introTimer.Start();
            }
        }
    }

    private static void AnimateScale(UIElement element, double target)
    {
        if (element.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        _fullscreenTimer.Stop();
        _introTimer.Stop();
        base.OnClosed(e);
    }
}
