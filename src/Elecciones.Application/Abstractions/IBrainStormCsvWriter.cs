using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IBrainStormCsvWriter
{
    Task<string> WriteAsync(
        BrainStormSnapshot snapshot,
        string exportName,
        CancellationToken cancellationToken = default);

    Task<string?> WriteSedesAsync(
        BrainStormSnapshot snapshot,
        string partyToken,
        CancellationToken cancellationToken = default);

    Task<string?> WriteRecuentosJsonAsync(
        BrainStormSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
