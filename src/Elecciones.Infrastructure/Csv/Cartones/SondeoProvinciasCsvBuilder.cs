using System.Globalization;
using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye <c>sondeo_provincias.csv</c> con todas las provincias, comunidades autónomas
/// y el total general de España (<c>9900000</c>), incluyendo el partido ganador según escaños
/// <c>hasta</c> de sondeo (5 dígitos), los escaños totales que reparte la circunscripción
/// y la diferencia (<c>hasta - históricos</c>) de dicho partido ganador.
/// </summary>
public static class SondeoProvinciasCsvBuilder
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "cod_lugar",
        "cod_partido",
        "escanos",
        "diferencia"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "sondeo_provincias.csv");
        var entries = ResolveEntries(snapshot);

        var rows = entries
            .OrderBy(e => e.CodigoLugar, StringComparer.Ordinal)
            .Select(e =>
            {
                var winnerCode = MayoriasProvinciasCsvBuilder.FormatFiveDigitWinnerCode(e.CodigoPartidoGanador);
                var diffText = string.IsNullOrEmpty(winnerCode)
                    ? string.Empty
                    : e.DiferenciaEscaniosGanador.ToString(CultureInfo.InvariantCulture);

                return (IReadOnlyList<string>)
                [
                    e.CodigoLugar.Trim(),
                    winnerCode,
                    FormatInt(e.EscaniosCircunscripcion),
                    diffText
                ];
            })
            .ToList();

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
    }

    private static IReadOnlyList<CircunscripcionMayoriaSnapshot> ResolveEntries(BrainStormSnapshot snapshot)
    {
        if (snapshot.SondeoMayoriasCircunscripciones is { Count: > 0 })
        {
            return snapshot.SondeoMayoriasCircunscripciones;
        }

        if (snapshot.MayoriasCircunscripciones is { Count: > 0 })
        {
            return snapshot.MayoriasCircunscripciones;
        }

        var list = new List<CircunscripcionMayoriaSnapshot>();
        if (!string.IsNullOrWhiteSpace(snapshot.Circunscripcion.Codigo))
        {
            var topSondeo = snapshot.Partidos
                .Where(p => !string.IsNullOrWhiteSpace(p.Codigo) && !string.Equals(p.Codigo, "99999", StringComparison.Ordinal))
                .OrderByDescending(p => p.EscaniosHastaSondeo)
                .ThenByDescending(p => p.EscaniosDesdeSondeo)
                .ThenByDescending(p => p.PorcentajeVoto)
                .FirstOrDefault(p => p.EscaniosHastaSondeo > 0 || p.EscaniosDesdeSondeo > 0 || p.PorcentajeVoto > 0);

            var diff = topSondeo is null ? 0 : topSondeo.EscaniosHastaSondeo - topSondeo.EscaniosHistoricos;

            list.Add(new CircunscripcionMayoriaSnapshot(
                snapshot.Circunscripcion.Codigo,
                snapshot.Circunscripcion.Nombre,
                topSondeo?.Codigo ?? string.Empty,
                topSondeo?.Siglas ?? string.Empty,
                snapshot.EscaniosTotales,
                topSondeo?.EscaniosHastaSondeo ?? 0,
                snapshot.Escrutado,
                snapshot.Participacion,
                diff));
        }

        return list;
    }
}
