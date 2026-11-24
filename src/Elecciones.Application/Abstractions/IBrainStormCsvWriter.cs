using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IBrainStormCsvWriter
{
    Task<string> WriteAsync(
        BrainStormSnapshot snapshot,
        string exportName,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null);

    Task<CartonCsvExportResult> WriteCartonAsync(
        CartonKind cartonKind,
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CancellationToken cancellationToken = default,
        GraphicModule? module = GraphicModule.Carton);

    Task<IReadOnlyList<CartonCsvFileArtifact>> WriteAllCartonFilesAsync(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CancellationToken cancellationToken = default,
        GraphicModule? module = GraphicModule.Carton);

    Task<string?> WriteSedesAsync(
        BrainStormSnapshot snapshot,
        string partyToken,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null);

    Task<string?> WriteRecuentosJsonAsync(
        BrainStormSnapshot snapshot,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null);
}
