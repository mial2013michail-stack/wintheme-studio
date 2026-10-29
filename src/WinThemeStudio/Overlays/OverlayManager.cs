using WinThemeStudio.Services;

namespace WinThemeStudio.Overlays;

public sealed class OverlayManager
{
    private readonly ConfigService _config;
    private readonly IconPackService _icons;

    private DockWindow? _dock;

    public OverlayManager(ConfigService config, IconPackService icons)
    {
        _config = config;
        _icons = icons;
    }

    public void Refresh()
    {
        if (_config.Current.DockEnabled)
        {
            _dock ??= new DockWindow(_config, _icons);
            _dock.Reload();
            _dock.ShowOverlay();
        }
        else if (_dock is not null)
        {
            _dock.Close();
            _dock = null;
        }
    }

    public void Stop()
    {
        _dock?.Close();
        _dock = null;
    }
}
