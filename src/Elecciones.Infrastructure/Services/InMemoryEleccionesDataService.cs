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

    private static readonly IReadOnlyList<MedioSummary> Medios =
    [
        new("RTVE", "RTVE"),
        new("MED01", "Sondeo Medio 1"),
        new("MED02", "Sondeo Medio 2")
    ];

    public Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Circunscripciones);
    }

    public Task<IReadOnlyList<MedioSummary>> GetMediosAsync(
        int electionSlot = 1,
        int connectionType = 0,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Medios);
    }

    public Task<BrainStormSnapshot> GetSnapshotAsync(
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default)
    {
        var circCode = ResolveCircCode(query);
        var circ = Circunscripciones.FirstOrDefault(x => x.Codigo == circCode) ?? Circunscripciones[0];

        var seed = Math.Abs(circ.Codigo.GetHashCode());
        var baseEsc = 10 + (seed % 8);

        var partidos = new List<PartidoSnapshot>
        {
            new("001", "00001", "PP", "Partido Popular", string.Empty, baseEsc + 8, baseEsc + 7, baseEsc + 9, baseEsc + 6, 34.4, 32.2, 842100, 790000, 1200, 0, 0),
            new("002", "00002", "PSOE", "Partido Socialista", string.Empty, baseEsc + 7, baseEsc + 6, baseEsc + 8, baseEsc + 8, 31.9, 33.1, 780200, 800000, 1600, 1, 0),
            new("003", "00003", "VOX", "VOX", string.Empty, baseEsc + 2, baseEsc + 2, baseEsc + 3, baseEsc + 1, 12.1, 10.8, 302110, 270000, 980, 0, 1),
            new("004", "00004", "SUMAR", "SUMAR", string.Empty, baseEsc + 1, baseEsc + 1, baseEsc + 2, baseEsc + 3, 9.4, 11.2, 224500, 245000, 730, 0, 0),
            new("005", "00005", "OTROS", "Otros", string.Empty, baseEsc, baseEsc, baseEsc + 1, baseEsc, 6.3, 5.8, 147800, 139000, 410, 0, 0)
        };

        if (!oficiales && !string.IsNullOrWhiteSpace(query.MedioCodigo) && !IsRtve(query.MedioCodigo))
        {
            var variant = Math.Abs(query.MedioCodigo.Trim().GetHashCode()) % 3;
            partidos = ApplySondeoVariant(partidos, variant);
        }

        if (!query.IncludeZeroSeats)
        {
            partidos = partidos
                .Where(p => oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0)
                .ToList();
        }

        var snapshot = new BrainStormSnapshot
        {
            Circunscripcion = circ,
            Oficiales = oficiales,
            Avance = query.Avance is >= 1 and <= 4 ? query.Avance.Value : 3,
            Escrutado = 78.50,
            Participacion = 66.20,
            ParticipacionHistorica = 64.10,
            ParticipacionMedia = 65.00,
            EscaniosTotales = 67,
            MayoriaAbsoluta = 34,
            VotantesTotales = 2456710,
            VotantesFaltan = 834000,
            AnioUltimasElecciones = 2023,
            NumPartidos = partidos.Count(p => oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0),
            NombreSondeo = query.MedioCodigo ?? string.Empty,
            GeneratedAtUtc = DateTime.UtcNow,
            Partidos = partidos
        };

        return Task.FromResult(snapshot);
    }

    private static string ResolveCircCode(SnapshotQuery query)
    {
        return query.Kind switch
        {
            SnapshotQueryKind.Circunscripcion => string.IsNullOrWhiteSpace(query.CircunscripcionCodigo) ? "9900000" : query.CircunscripcionCodigo,
            SnapshotQueryKind.MasVotadosAutonomias => "9900000",
            SnapshotQueryKind.PartidoAutonomias => "9900000",
            SnapshotQueryKind.MasVotadosProvincias or SnapshotQueryKind.PartidoProvincias
                => string.IsNullOrWhiteSpace(query.AutonomiaCodigo) ? "0200000" : $"{query.AutonomiaCodigo}00000",
            _ => "9900000"
        };
    }

    private static bool IsRtve(string medioCodigo)
    {
        return string.Equals(medioCodigo.Trim(), "RTVE", StringComparison.OrdinalIgnoreCase);
    }

    private static List<PartidoSnapshot> ApplySondeoVariant(List<PartidoSnapshot> partidos, int variant)
    {
        if (variant == 0)
        {
            return partidos;
        }

        var adjusted = new List<PartidoSnapshot>(partidos.Count);
        for (var i = 0; i < partidos.Count; i++)
        {
            var partido = partidos[i];
            var offset = ((i + variant) % 2 == 0) ? 1 : -1;
            var nuevoHasta = Math.Max(0, partido.EscaniosHastaSondeo + offset);
            var nuevoDesde = Math.Max(0, partido.EscaniosDesdeSondeo + (offset > 0 ? 0 : -offset));

            adjusted.Add(partido with
            {
                EscaniosHastaSondeo = nuevoHasta,
                EscaniosDesdeSondeo = nuevoDesde
            });
        }

        return adjusted
            .OrderByDescending(x => x.EscaniosHastaSondeo)
            .ThenByDescending(x => x.EscaniosDesdeSondeo)
            .ThenByDescending(x => x.PorcentajeVoto)
            .ToList();
    }
}
