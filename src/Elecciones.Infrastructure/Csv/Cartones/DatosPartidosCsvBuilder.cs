using System.Globalization;
using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye <c>datos_partidos.csv</c> (11 columnas) para los gráficos oficiales de Cartón:
/// incluye siempre todos los partidos de la elección seleccionada ordenados por código (5 dígitos) como
/// <c>Partido01</c>..<c>PartidoN</c>, rellenando a 0 los que no concurren en la circunscripción activa,
/// e incluyendo <c>dif_escanos</c> (+N, 0, -N, o vacío si no tiene histórico) y <c>tendencia</c> (+, -, =, *).
/// </summary>
public static class DatosPartidosCsvBuilder
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "partido",
        "cod_partido",
        "siglas",
        "escanos",
        "porcentaje",
        "votos",
        "escanos_ant",
        "porcentaje_ant",
        "votos_ant",
        "dif_escanos",
        "tendencia"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "datos_partidos.csv");
        var orderedParties = GetOrderedAllElectionPartiesForCircunscripcion(snapshot);
        var rows = new List<IReadOnlyList<string>>(orderedParties.Count);

        for (var i = 0; i < orderedParties.Count; i++)
        {
            var p = orderedParties[i];
            var bloquePartido = $"Partido{(i + 1):D2}";
            var codCincoDigitos = FormatFiveDigitPartyCode(p.Codigo);
            var escanosActuales = p.Escanios;
            var porcentajeActual = FormatUpToTwoDecimals(p.PorcentajeVoto);
            var votosActuales = FormatInt(p.Votos);

            var esPartidoNuevo = IsNewPartyWithoutHistoricalRecord(p);
            string escanosAnt;
            string porcentajeAnt;
            string votosAnt;
            string difEscanos;
            string tendencia;

            if (esPartidoNuevo)
            {
                escanosAnt = string.Empty;
                porcentajeAnt = string.Empty;
                votosAnt = string.Empty;
                difEscanos = string.Empty;
                tendencia = "*";
            }
            else
            {
                escanosAnt = FormatInt(p.EscaniosHistoricos);
                porcentajeAnt = FormatUpToTwoDecimals(p.PorcentajeVotoHistorico);
                votosAnt = FormatInt(p.VotantesHistoricos);

                var diff = escanosActuales - p.EscaniosHistoricos;
                if (diff > 0)
                {
                    difEscanos = $"+{diff.ToString(CultureInfo.InvariantCulture)}";
                    tendencia = "+";
                }
                else if (diff < 0)
                {
                    difEscanos = diff.ToString(CultureInfo.InvariantCulture);
                    tendencia = "-";
                }
                else
                {
                    difEscanos = "0";
                    tendencia = "=";
                }
            }

            rows.Add(
            [
                bloquePartido,
                codCincoDigitos,
                p.Siglas,
                FormatInt(escanosActuales),
                porcentajeActual,
                votosActuales,
                escanosAnt,
                porcentajeAnt,
                votosAnt,
                difEscanos,
                tendencia
            ]);
        }

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
    }

    internal static IReadOnlyList<PartidoSnapshot> GetOrderedAllElectionPartiesForCircunscripcion(
        BrainStormSnapshot snapshot)
    {
        var orderedCircParties = GetOrderedAllCircunscripcionParties(snapshot);

        var circByNormalizedCode = new Dictionary<string, PartidoSnapshot>(orderedCircParties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var circParty in orderedCircParties)
        {
            var norm = NormalizePartyCode(circParty.Codigo);
            if (!circByNormalizedCode.ContainsKey(norm))
            {
                circByNormalizedCode[norm] = circParty;
            }
        }

        var masterByNormalizedCode = new Dictionary<string, PartidoEleccionSummary>(StringComparer.OrdinalIgnoreCase);

        if (snapshot.PartidosEleccion is { Count: > 0 })
        {
            foreach (var master in snapshot.PartidosEleccion)
            {
                if (string.IsNullOrWhiteSpace(master.Codigo))
                {
                    continue;
                }

                var norm = NormalizePartyCode(master.Codigo);
                if (string.Equals(norm, "0", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!masterByNormalizedCode.ContainsKey(norm))
                {
                    var formattedCode = FormatFiveDigitPartyCode(master.Codigo);
                    var siglas = string.IsNullOrWhiteSpace(master.Siglas) ? formattedCode : master.Siglas.Trim();
                    var padre = string.IsNullOrWhiteSpace(master.Padre) ? formattedCode : FormatFiveDigitPartyCode(master.Padre);
                    masterByNormalizedCode[norm] = new PartidoEleccionSummary(formattedCode, siglas, master.Nombre?.Trim() ?? string.Empty, padre);
                }
            }
        }

        foreach (var circParty in orderedCircParties)
        {
            var norm = NormalizePartyCode(circParty.Codigo);
            if (!masterByNormalizedCode.ContainsKey(norm))
            {
                var formattedCode = FormatFiveDigitPartyCode(circParty.Codigo);
                var siglas = string.IsNullOrWhiteSpace(circParty.Siglas) ? formattedCode : circParty.Siglas.Trim();
                var padre = string.IsNullOrWhiteSpace(circParty.Padre) ? formattedCode : FormatFiveDigitPartyCode(circParty.Padre);
                masterByNormalizedCode[norm] = new PartidoEleccionSummary(formattedCode, siglas, circParty.Nombre?.Trim() ?? string.Empty, padre);
            }
        }

        if (masterByNormalizedCode.Count == 0)
        {
            return [];
        }

        var sortedMaster = masterByNormalizedCode
            .OrderBy(kvp => ParseSortablePartyCode(kvp.Value.Codigo))
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToList();

        var result = new List<PartidoSnapshot>(sortedMaster.Count);
        foreach (var (norm, master) in sortedMaster)
        {
            if (circByNormalizedCode.TryGetValue(norm, out var existing))
            {
                result.Add(string.IsNullOrWhiteSpace(existing.Siglas)
                    ? existing with { Siglas = master.Siglas }
                    : existing);
                continue;
            }

            result.Add(new PartidoSnapshot(
                Codigo: master.Codigo,
                Padre: master.Padre,
                Siglas: master.Siglas,
                Nombre: master.Nombre,
                Candidato: string.Empty,
                Escanios: 0,
                EscaniosDesdeSondeo: 0,
                EscaniosHastaSondeo: 0,
                EscaniosHistoricos: 0,
                PorcentajeVoto: 0,
                PorcentajeVotoHistorico: 0,
                Votos: 0,
                VotantesHistoricos: 0,
                RestoVotos: 0,
                EsUltimoEscano: 0,
                LuchaUltimoEscano: 0));
        }

        return result;
    }

    internal static List<PartidoSnapshot> GetOrderedAllCircunscripcionParties(BrainStormSnapshot snapshot)
    {
        var sourceParties = snapshot.PartidosCircunscripcionCompletos is { Count: > 0 }
            ? snapshot.PartidosCircunscripcionCompletos
            : snapshot.Partidos;

        return sourceParties
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo) && !string.Equals(NormalizePartyCode(p.Codigo), "0", StringComparison.Ordinal))
            .OrderBy(p => ParseSortablePartyCode(p.Codigo))
            .ThenBy(p => NormalizePartyCode(p.Codigo), StringComparer.Ordinal)
            .ToList();
    }

    public static string FormatFiveDigitPartyCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return "00000";
        }

        var trimmed = rawCode.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric >= 0)
        {
            return numeric <= 99999
                ? numeric.ToString("D5", CultureInfo.InvariantCulture)
                : numeric.ToString(CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    public static string NormalizePartyCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return "0";
        }

        var trimmed = rawCode.Trim().TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    internal static (int Bucket, long NumericValue) ParseSortablePartyCode(string? rawCode)
    {
        var normalized = NormalizePartyCode(rawCode);
        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return (0, parsed);
        }

        return (1, long.MaxValue);
    }

    internal static bool IsNewPartyWithoutHistoricalRecord(PartidoSnapshot partido)
    {
        return partido.EscaniosHistoricos == 0
            && partido.VotantesHistoricos == 0
            && Math.Abs(partido.PorcentajeVotoHistorico) < 0.0001;
    }

    internal static string FormatUpToTwoDecimals(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }
}
