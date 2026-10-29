using System.Windows.Controls;
using WinThemeStudio.Services;

namespace WinThemeStudio.UI.Views;

public class PageView : UserControl
{
    public PageView()
    {
    }

    protected PageView(MainWindow host, AppServices services)
    {
        Host = host;
        Services = services;
    }

    protected MainWindow Host { get; } = null!;
    protected AppServices Services { get; } = null!;

    public virtual void OnShown()
    {
    }
}
