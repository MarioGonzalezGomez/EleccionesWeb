using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Elecciones.Infrastructure.Services;

/// <summary>
/// Enruta en caliente las consultas entre <see cref="MySqlEleccionesDataService"/> (cuando <c>Database:Enabled</c> es true)
/// e <see cref="InMemoryEleccionesDataService"/> (modo demo sin red), permitiendo activar y cambiar bases de datos sin reiniciar.
/// </summary>
public sealed class DynamicEleccionesDataService : IEleccionesDataService
{
    private readonly IOptionsMonitor<DatabaseOptions> _databaseMonitor;
    private readonly MySqlEleccionesDataService _mySqlService;
    private readonly InMemoryEleccionesDataService _inMemoryService;

    public DynamicEleccionesDataService(
        IOptionsMonitor<DatabaseOptions> databaseMonitor,
        MySqlEleccionesDataService mySqlService,
        InMemoryEleccionesDataService inMemoryService)
    {
        _databaseMonitor = databaseMonitor;
        _mySqlService = mySqlService;
        _inMemoryService = inMemoryService;
    }

    private IEleccionesDataService Current =>
        _databaseMonitor.CurrentValue.Enabled ? _mySqlService : _inMemoryService;

    public Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default)
        => Current.GetCircunscripcionesAsync(electionSlot, connectionType, cancellationToken);

    public Task<IReadOnlyList<MedioSummary>> GetMediosAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default)
        => Current.GetMediosAsync(electionSlot, connectionType, cancellationToken);

    public Task<BrainStormSnapshot> GetSnapshotAsync(
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default)
        => Current.GetSnapshotAsync(query, oficiales, cancellationToken);
}
