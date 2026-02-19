using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IEleccionesDataService
{
    Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(CancellationToken cancellationToken = default);

    Task<BrainStormSnapshot> GetSnapshotAsync(
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default);
}
