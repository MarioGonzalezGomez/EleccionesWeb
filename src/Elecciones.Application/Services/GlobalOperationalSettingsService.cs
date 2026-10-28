using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class GlobalOperationalSettingsService : IGlobalOperationalSettingsService
{
    private readonly object _sync = new();
    private GlobalOperationalSettings _current;

    public event Action<GlobalOperationalSettings>? OnSettingsChanged;

    public GlobalOperationalSettingsService(GlobalOperationalSettings? initial = null)
    {
        _current = initial ?? new GlobalOperationalSettings();
    }

    public GlobalOperationalSettings GetSettings()
    {
        lock (_sync)
        {
            return _current;
        }
    }

    public void UpdateSettings(Func<GlobalOperationalSettings, GlobalOperationalSettings> updater)
    {
        GlobalOperationalSettings updated;
        lock (_sync)
        {
            var next = updater(_current);
            updated = next with
            {
                ElectionSlot = Math.Clamp(next.ElectionSlot, 1, 3),
                ConnectionType = Math.Clamp(next.ConnectionType, 0, 3),
                Avance = Math.Clamp(next.Avance, 0, 4),
                ExportName = string.IsNullOrWhiteSpace(next.ExportName) ? "BrainStorm" : next.ExportName.Trim(),
                AutoRefreshIntervalSeconds = Math.Clamp(next.AutoRefreshIntervalSeconds, 2, 60)
            };
            _current = updated;
        }

        OnSettingsChanged?.Invoke(updated);
    }

    public void RecordAutoRefreshCheck(DateTimeOffset checkedAtUtc, DateTimeOffset? appliedAtUtc, string statusMessage)
    {
        GlobalOperationalSettings updated;
        lock (_sync)
        {
            updated = _current with
            {
                LastAutoRefreshCheckUtc = checkedAtUtc,
                LastAutoRefreshAppliedUtc = appliedAtUtc ?? _current.LastAutoRefreshAppliedUtc,
                LastAutoRefreshStatus = statusMessage ?? string.Empty
            };
            _current = updated;
        }

        OnSettingsChanged?.Invoke(updated);
    }
}

