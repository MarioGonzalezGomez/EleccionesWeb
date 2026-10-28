using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IGlobalOperationalSettingsService
{
    event Action<GlobalOperationalSettings>? OnSettingsChanged;

    GlobalOperationalSettings GetSettings();
    void UpdateSettings(Func<GlobalOperationalSettings, GlobalOperationalSettings> updater);
    void RecordAutoRefreshCheck(DateTimeOffset checkedAtUtc, DateTimeOffset? appliedAtUtc, string statusMessage);
}

