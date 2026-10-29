using System.Windows.Threading;
using WinThemeStudio.Core;

namespace WinThemeStudio.Services;

/// <summary>
/// Applies a chosen profile once a day at the configured local time. The
/// scheduler never interrupts a fullscreen game or video.
/// </summary>
public sealed class SchedulerService : IDisposable
{
    private readonly ConfigService _config;
    private readonly ProfileService _profiles;
    private readonly ExplorerService _explorer;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _running;
    private string _lastAttempt = "";

    public SchedulerService(ConfigService config, ProfileService profiles, ExplorerService explorer)
    {
        _config = config;
        _profiles = profiles;
        _explorer = explorer;
        _timer.Tick += (_, _) => _ = TickAsync();
    }

    public event EventHandler<string>? ProfileApplied;
    public event EventHandler<string>? Failed;

    public bool IsStarted { get; private set; }

    public void Start()
    {
        if (IsStarted)
        {
            return;
        }

        IsStarted = true;
        _timer.Start();
        _ = TickAsync();
    }

    public void Stop()
    {
        IsStarted = false;
        _timer.Stop();
    }

    public static bool TryParseTime(string? value, out TimeSpan time)
    {
        return TimeSpan.TryParse(value, out time) &&
               time >= TimeSpan.Zero &&
               time < TimeSpan.FromDays(1);
    }

    private async Task TickAsync()
    {
        if (_running)
        {
            return;
        }

        var cfg = _config.Current;
        if (!cfg.SchedulerEnabled || !TryParseTime(cfg.SchedulerTime, out var scheduled))
        {
            return;
        }

        var now = DateTime.Now;
        if (now.TimeOfDay < scheduled)
        {
            return;
        }

        var today = now.ToString("yyyy-MM-dd");
        if (string.Equals(cfg.SchedulerLastRun, today, StringComparison.Ordinal) ||
            string.Equals(_lastAttempt, today, StringComparison.Ordinal))
        {
            return;
        }

        var profile = ProfileService.GetProfiles().FirstOrDefault(p =>
            string.Equals(p.Key, cfg.SchedulerProfile, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return;
        }

        if (_explorer.IsFullscreenForeground())
        {
            Logger.Info("Ежедневный профиль отложен: обнаружено полноэкранное приложение.");
            return;
        }

        _running = true;
        _lastAttempt = today;
        try
        {
            await _profiles.ApplyAsync(profile.Key);
            _config.Update(c => c.SchedulerLastRun = today);
            Logger.Info($"Ежедневный профиль {profile.Name} применён по расписанию.");
            ProfileApplied?.Invoke(this, profile.Name);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось применить ежедневный профиль", ex);
            Failed?.Invoke(this, profile.Name);
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
