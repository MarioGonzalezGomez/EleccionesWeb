using Elecciones.Infrastructure.Services;

namespace EleccionesWeb.Services;

public sealed class DatabaseChangeListenerHostedService(DatabaseChangeListenerService listener) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return listener.RunPollingLoopAsync(stoppingToken);
    }
}

