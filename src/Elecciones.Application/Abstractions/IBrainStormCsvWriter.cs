using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IBrainStormCsvWriter
{
    Task<string> WriteAsync(
        BrainStormSnapshot snapshot,
        string exportName,
        CancellationToken cancellationToken = default);
}
