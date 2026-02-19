using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Infrastructure.Services;

public sealed class InMemoryEleccionesDataService : IEleccionesDataService
{
    private static readonly IReadOnlyList<CircunscripcionSummary> Circunscripciones =
    [
        new("9900000", "Espana"),
        new("0200000", "Aragon"),
        new("2800000", "Madrid"),
        new("0800000", "Barcelona"),
        new("4100000", "Sevilla")
    ];

    public Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Circunscripciones);
    }

    public Task<BrainStormSnapshot> GetSnapshotAsync(
        string circunscripcionCodigo,
        bool oficiales,
        CancellationToken cancellationToken = default)
    {
        var circ = Circunscripciones.FirstOrDefault(x => x.Codigo == circunscripcionCodigo) ?? Circunscripciones[0];

        var seed = Math.Abs(circ.Codigo.GetHashCode());
        var baseEsc = 10 + (seed % 8);

        var partidos = new List<PartidoSnapshot>
        {
            new("001", "PP", baseEsc + 8, baseEsc + 7, baseEsc + 9, baseEsc + 6, 34.4, 842100),
            new("002", "PSOE", baseEsc + 7, baseEsc + 6, baseEsc + 8, baseEsc + 8, 31.9, 780200),
            new("003", "VOX", baseEsc + 2, baseEsc + 2, baseEsc + 3, baseEsc + 1, 12.1, 302110),
            new("004", "SUMAR", baseEsc + 1, baseEsc + 1, baseEsc + 2, baseEsc + 3, 9.4, 224500),
            new("005", "OTROS", baseEsc, baseEsc, baseEsc + 1, baseEsc, 6.3, 147800)
        };

        var snapshot = new BrainStormSnapshot
        {
            Circunscripcion = circ,
            Oficiales = oficiales,
            Avance = 3,
            Escrutado = 78.50,
            EscaniosTotales = 67,
            MayoriaAbsoluta = 34,
            VotantesTotales = 2456710,
            AnioUltimasElecciones = 2023,
            GeneratedAtUtc = DateTime.UtcNow,
            Partidos = partidos
        };

        return Task.FromResult(snapshot);
    }
}
