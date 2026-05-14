using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IEleccionesDataService
{
    Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedioSummary>> GetMediosAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default);

    Task<BrainStormSnapshot> GetSnapshotAsync(
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default);
}
