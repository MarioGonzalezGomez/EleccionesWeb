using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Csv.Cartones;

namespace Elecciones.Infrastructure.Services;

public sealed class InMemoryEleccionesDataService : IEleccionesDataService
{
    private static readonly IReadOnlyList<CircunscripcionSummary> Circunscripciones =
    [
        new("9900000", "España"),
        new("0100000", "Andalucía"),
        new("0129000", "Málaga"),
        new("0141000", "Sevilla"),
        new("0141091", "Sevilla Capital"),
        new("0200000", "Aragón"),
        new("0250000", "Zaragoza"),
        new("0900000", "Cataluña"),
        new("0908000", "Barcelona"),
        new("0908019", "Barcelona Capital"),
        new("1200000", "Madrid"),
        new("1400000", "País Vasco"),
        new("1448000", "Bizkaia")
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
        var slot = Math.Clamp(query.ElectionSlot, 1, 3);
        var circCode = ResolveCircCode(query);
        var circ = Circunscripciones.FirstOrDefault(x => x.Codigo == circCode) ?? Circunscripciones[0];

        var seed = slot == 1
            ? Math.Abs(circ.Codigo.GetHashCode())
            : Math.Abs(HashCode.Combine(circ.Codigo, slot * 7919));
        var baseEsc = slot switch
        {
            2 => 14 + (seed % 10),
            3 => 6 + (seed % 6),
            _ => 10 + (seed % 8)
        };
        var slotVoteFactor = slot switch
        {
            2 => 0.92,
            3 => 0.68,
            _ => 1.0
        };

        var partidos = slot switch
        {
            2 => new List<PartidoSnapshot>
            {
                new("001", "00001", "PP", "Partido Popular", string.Empty, baseEsc + 12, baseEsc + 10, baseEsc + 14, baseEsc + 7, 36.8, 33.5, (int)(890400 * slotVoteFactor), (int)(810000 * slotVoteFactor), 1450, 1, 0),
                new("002", "00002", "PSOE", "Partido Socialista", string.Empty, baseEsc + 6, baseEsc + 5, baseEsc + 8, baseEsc + 9, 30.2, 32.8, (int)(742300 * slotVoteFactor), (int)(795000 * slotVoteFactor), 1120, 0, 1),
                new("003", "00003", "VOX", "VOX", string.Empty, Math.Max(1, baseEsc - 4), Math.Max(1, baseEsc - 5), Math.Max(2, baseEsc - 3), Math.Max(1, baseEsc - 3), 10.6, 11.4, (int)(260150 * slotVoteFactor), (int)(276000 * slotVoteFactor), 640, 0, 0),
                new("004", "00004", "SUMAR", "SUMAR", string.Empty, Math.Max(1, baseEsc - 6), Math.Max(0, baseEsc - 7), Math.Max(1, baseEsc - 5), Math.Max(1, baseEsc - 4), 8.1, 9.9, (int)(198400 * slotVoteFactor), (int)(239000 * slotVoteFactor), 510, 0, 0),
                new("005", "00005", "OTROS", "Otros", string.Empty, Math.Max(1, baseEsc - 5), Math.Max(1, baseEsc - 6), Math.Max(2, baseEsc - 4), Math.Max(1, baseEsc - 5), 7.9, 6.5, (int)(191200 * slotVoteFactor), (int)(158000 * slotVoteFactor), 390, 0, 0)
            },
            3 => new List<PartidoSnapshot>
            {
                new("002", "00002", "PSOE", "Partido Socialista", string.Empty, baseEsc + 5, baseEsc + 4, baseEsc + 6, baseEsc + 4, 32.6, 31.0, (int)(798500 * slotVoteFactor), (int)(750000 * slotVoteFactor), 980, 0, 1),
                new("001", "00001", "PP", "Partido Popular", string.Empty, baseEsc + 4, baseEsc + 4, baseEsc + 6, baseEsc + 2, 31.8, 28.4, (int)(779100 * slotVoteFactor), (int)(688000 * slotVoteFactor), 1310, 1, 0),
                new("003", "00003", "VOX", "VOX", string.Empty, baseEsc + 1, baseEsc, baseEsc + 2, baseEsc, 13.4, 12.1, (int)(328200 * slotVoteFactor), (int)(293000 * slotVoteFactor), 720, 0, 0),
                new("004", "00004", "SUMAR", "SUMAR", string.Empty, baseEsc, Math.Max(0, baseEsc - 1), baseEsc + 1, baseEsc + 2, 10.2, 12.5, (int)(249800 * slotVoteFactor), (int)(302000 * slotVoteFactor), 540, 0, 0),
                new("005", "00005", "OTROS", "Otros", string.Empty, Math.Max(0, baseEsc - 2), Math.Max(0, baseEsc - 2), Math.Max(1, baseEsc - 1), Math.Max(0, baseEsc - 1), 5.8, 6.1, (int)(142000 * slotVoteFactor), (int)(147000 * slotVoteFactor), 310, 0, 0)
            },
            _ => new List<PartidoSnapshot>
            {
                new("001", "00001", "PP", "Partido Popular", string.Empty, baseEsc + 8, baseEsc + 7, baseEsc + 9, baseEsc + 6, 34.4, 32.2, 842100, 790000, 1200, 0, 0),
                new("002", "00002", "PSOE", "Partido Socialista", string.Empty, baseEsc + 7, baseEsc + 6, baseEsc + 8, baseEsc + 8, 31.9, 33.1, 780200, 800000, 1600, 1, 0),
                new("003", "00003", "VOX", "VOX", string.Empty, baseEsc + 2, baseEsc + 2, baseEsc + 3, baseEsc + 1, 12.1, 10.8, 302110, 270000, 980, 0, 1),
                new("004", "00004", "SUMAR", "SUMAR", string.Empty, baseEsc + 1, baseEsc + 1, baseEsc + 2, baseEsc + 3, 9.4, 11.2, 224500, 245000, 730, 0, 0),
                new("021", "00021", "PACMA", "PACMA", string.Empty, 0, 0, 0, 0, 0.85, 1.08, 21400, 26100, 0, 0, 0),
                new("099", "00099", "SALF", "Se Acabó La Fiesta", string.Empty, 0, 0, 0, 0, 2.45, 0, 61800, 0, 0, 0, 0),
                new("99999", "99999", "OTROS", "Otros", string.Empty, baseEsc, baseEsc, baseEsc + 1, baseEsc, 6.3, 5.8, 147800, 139000, 410, 0, 0)
            }
        };

        if (!oficiales && !string.IsNullOrWhiteSpace(query.MedioCodigo) && !IsRtve(query.MedioCodigo))
        {
            var variant = Math.Abs(query.MedioCodigo.Trim().GetHashCode()) % 3;
            partidos = ApplySondeoVariant(partidos, variant);
        }

        var partidosCompletos = partidos.ToList();

        if (!query.IncludeZeroSeats)
        {
            partidos = partidos
                .Where(p => oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0)
                .ToList();
        }

        var escrutadoGeneral = slot switch
        {
            2 => 82.15,
            3 => 91.40,
            _ => 84.60
        };
        var escrutadoCirc = string.Equals(circ.Codigo, "9900000", StringComparison.Ordinal)
            ? escrutadoGeneral
            : 78.50;

        var mayoriasCircunscripciones = new List<CircunscripcionMayoriaSnapshot>
        {
            new("0100000", "Andalucía", "00001", "PP", 61, 25, 81.20, 65.40, 4),
            new("0104000", "Almería", "00001", "PP", 6, 3, 83.10, 64.20, 1),
            new("0141000", "Sevilla", "00002", "PSOE", 12, 6, 79.40, 66.80, -1),
            new("0200000", "Aragón", "00001", "PP", 13, 7, 85.00, 67.10, 2),
            new("0250000", "Zaragoza", "00001", "PP", 7, 4, 86.30, 68.00, 1),
            new("0900000", "Cataluña", "00002", "PSOE", 48, 19, 80.50, 63.90, 0),
            new("0908000", "Barcelona", "00002", "PSOE", 32, 13, 79.80, 64.50, -1),
            new("1200000", "Comunidad de Madrid", "00001", "PP", 37, 16, 88.20, 70.40, 5),
            new("1228000", "Madrid", "00001", "PP", 37, 16, 88.20, 70.40, 5),
            new("9900000", "España", slot == 3 ? "00002" : "00001", slot == 3 ? "PSOE" : "PP", 350, 137, escrutadoGeneral, 66.20, slot == 3 ? 1 : 48)
        };

        var sondeoMayoriasCircunscripciones = new List<CircunscripcionMayoriaSnapshot>
        {
            new("0100000", "Andalucía", "00001", "PP", 61, 26, 81.20, 65.40, 5),
            new("0104000", "Almería", "00001", "PP", 6, 3, 83.10, 64.20, 1),
            new("0141000", "Sevilla", "00002", "PSOE", 12, 5, 79.40, 66.80, -2),
            new("0200000", "Aragón", "00001", "PP", 13, 7, 85.00, 67.10, 2),
            new("0250000", "Zaragoza", "00001", "PP", 7, 4, 86.30, 68.00, 1),
            new("0900000", "Cataluña", "00002", "PSOE", 48, 20, 80.50, 63.90, 1),
            new("0908000", "Barcelona", "00002", "PSOE", 32, 14, 79.80, 64.50, 0),
            new("1200000", "Comunidad de Madrid", "00001", "PP", 37, 17, 88.20, 70.40, 6),
            new("1228000", "Madrid", "00001", "PP", 37, 17, 88.20, 70.40, 6),
            new("9900000", "España", slot == 3 ? "00002" : "00001", slot == 3 ? "PSOE" : "PP", 350, 140, escrutadoGeneral, 66.20, slot == 3 ? 2 : 51)
        };

        var snapshot = new BrainStormSnapshot
        {
            Circunscripcion = circ,
            ComunidadCircunscripcion = circ.Codigo.Length >= 2 ? circ.Codigo[..2] : string.Empty,
            Oficiales = oficiales,
            Avance = query.Avance is >= 1 and <= 4 ? query.Avance.Value : 3,
            Escrutado = escrutadoCirc,
            EscrutadoGeneral = escrutadoGeneral,
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
            Partidos = partidos,
            PartidosCircunscripcionCompletos = partidosCompletos,
            PartidosEleccion = partidosCompletos
                .Where(p => !string.Equals(DatosPartidosCsvBuilder.NormalizePartyCode(p.Codigo), "0", StringComparison.Ordinal))
                .GroupBy(p => DatosPartidosCsvBuilder.NormalizePartyCode(p.Codigo), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var p = g.First();
                    var code = DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(p.Codigo);
                    return new PartidoEleccionSummary(code, p.Siglas, p.Nombre, p.Padre);
                })
                .OrderBy(p => DatosPartidosCsvBuilder.ParseSortablePartyCode(p.Codigo))
                .ThenBy(p => DatosPartidosCsvBuilder.NormalizePartyCode(p.Codigo), StringComparer.Ordinal)
                .ToList(),
            MayoriasCircunscripciones = mayoriasCircunscripciones,
            SondeoMayoriasCircunscripciones = sondeoMayoriasCircunscripciones
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
