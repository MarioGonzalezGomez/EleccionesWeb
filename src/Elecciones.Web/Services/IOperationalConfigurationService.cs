using Elecciones.GraphicsGateway.Options;
using Elecciones.Infrastructure.Options;

namespace EleccionesWeb.Services;

public interface IOperationalConfigurationService
{
    OperationalConfigurationSnapshot GetSnapshot();
    Task<IReadOnlyList<EndpointConnectivityStatus>> CheckConnectivityAsync(CancellationToken cancellationToken = default);
    Task<SaveOperationalConfigurationResult> SaveAsync(OperationalConfigurationSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed class OperationalConfigurationSnapshot
{
    public GraphicsEndpointsOptions GraphicsEndpoints { get; init; } = new();
    public OperationalOptions Operational { get; init; } = new();
}

public sealed record EndpointConnectivityStatus(
    string Name,
    bool Enabled,
    string Host,
    int Port,
    bool Reachable,
    string Message);

public sealed record SaveOperationalConfigurationResult(
    bool Success,
    string Message,
    string SavedFilePath);
