using System.Globalization;
using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye la estructura estándar <c>datos_partidos.csv</c> para el nuevo grafismo:
/// todos los partidos de la circunscripción seleccionada ordenados por código de partido,
/// con indicadores de tendencia (+, -, =, *) y celdas históricas vacías en partidos nuevos (*).
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
        "tendencia"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "datos_partidos.csv");

        var sourceParties = snapshot.PartidosCircunscripcionCompletos is { Count: > 0 }
            ? snapshot.PartidosCircunscripcionCompletos
            : snapshot.Partidos;

        var orderedParties = sourceParties
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => ParseSortablePartyCode(p.Codigo))
            .ThenBy(p => NormalizePartyCode(p.Codigo), StringComparer.Ordinal)
            .ToList();

        var rows = new List<IReadOnlyList<string>>(orderedParties.Count);

        for (var i = 0; i < orderedParties.Count; i++)
        {
            var p = orderedParties[i];
            var bloquePartido = $"Partido{(i + 1):D2}";
            var codNormalizado = NormalizePartyCode(p.Codigo);
            var escanosActuales = ResolveDisplaySeats(p, snapshot.Oficiales);
            var porcentajeActual = FormatUpToTwoDecimals(p.PorcentajeVoto);
            var votosActuales = FormatInt(p.Votos);

            var esPartidoNuevo = IsNewPartyWithoutHistoricalRecord(p);
            string escanosAnt;
            string porcentajeAnt;
            string votosAnt;
            string tendencia;

            if (esPartidoNuevo)
            {
                escanosAnt = string.Empty;
                porcentajeAnt = string.Empty;
                votosAnt = string.Empty;
                tendencia = "*";
            }
            else
            {
                escanosAnt = FormatInt(p.EscaniosHistoricos);
                porcentajeAnt = FormatUpToTwoDecimals(p.PorcentajeVotoHistorico);
                votosAnt = FormatInt(p.VotantesHistoricos);

                var diffEscanos = escanosActuales - p.EscaniosHistoricos;
                tendencia = diffEscanos > 0
                    ? "+"
                    : diffEscanos < 0
                        ? "-"
                        : "=";
            }

            rows.Add(
            [
                bloquePartido,
                codNormalizado,
                p.Siglas,
                FormatInt(escanosActuales),
                porcentajeActual,
                votosActuales,
                escanosAnt,
                porcentajeAnt,
                votosAnt,
                tendencia
            ]);
        }

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
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

    private static (int Bucket, long NumericValue) ParseSortablePartyCode(string? rawCode)
    {
        var normalized = NormalizePartyCode(rawCode);
        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return (0, parsed);
        }

        return (1, long.MaxValue);
    }

    private static bool IsNewPartyWithoutHistoricalRecord(PartidoSnapshot partido)
    {
        return partido.EscaniosHistoricos == 0
            && partido.VotantesHistoricos == 0
            && Math.Abs(partido.PorcentajeVotoHistorico) < 0.0001;
    }

    private static string FormatUpToTwoDecimals(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }
}
