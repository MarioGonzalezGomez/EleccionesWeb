using System.Globalization;
using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye <c>mayorias_provincias.csv</c> con todas las provincias, comunidades autónomas
/// y el total general de España (<c>9900000</c>), incluyendo el código del partido con mayoría
/// (5 dígitos, o vacío si aún no hay escrutinio/ganador), los escaños totales que reparte
/// la circunscripción y su porcentaje de escrutado actual.
/// </summary>
public static class MayoriasProvinciasCsvBuilder
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "cod_lugar",
        "cod_partido",
        "escanos",
        "escrutado"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "mayorias_provincias.csv");
        var entries = ResolveEntries(snapshot);

        var rows = entries
            .OrderBy(e => e.CodigoLugar, StringComparer.Ordinal)
            .Select(e => (IReadOnlyList<string>)
            [
                e.CodigoLugar.Trim(),
                FormatFiveDigitWinnerCode(e.CodigoPartidoGanador),
                FormatInt(e.EscaniosCircunscripcion),
                FormatUpToTwoDecimals(e.Escrutado)
            ])
            .ToList();

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
    }

    public static string FormatFiveDigitWinnerCode(string? rawPartyCode)
    {
        if (string.IsNullOrWhiteSpace(rawPartyCode))
        {
            return string.Empty;
        }

        var trimmed = rawPartyCode.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric > 0)
        {
            return numeric <= 99999
                ? numeric.ToString("D5", CultureInfo.InvariantCulture)
                : numeric.ToString(CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static IReadOnlyList<CircunscripcionMayoriaSnapshot> ResolveEntries(BrainStormSnapshot snapshot)
    {
        if (snapshot.MayoriasCircunscripciones is { Count: > 0 })
        {
            return snapshot.MayoriasCircunscripciones;
        }

        // Fallback si el snapshot procede de un test unitario sintético sin MayoriasCircunscripciones poblado
        var list = new List<CircunscripcionMayoriaSnapshot>();

        foreach (var prov in snapshot.ProvinciasAutonomia)
        {
            if (string.IsNullOrWhiteSpace(prov.Codigo))
            {
                continue;
            }

            list.Add(new CircunscripcionMayoriaSnapshot(
                prov.Codigo,
                prov.Nombre,
                prov.CodigoGanador,
                prov.SiglaGanador,
                prov.Escanios,
                prov.EscaniosGanador,
                prov.Escrutado,
                prov.Participacion));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Circunscripcion.Codigo)
            && !list.Any(x => string.Equals(x.CodigoLugar, snapshot.Circunscripcion.Codigo, StringComparison.OrdinalIgnoreCase)))
        {
            var topParty = OrderPartiesForDisplay(snapshot)
                .FirstOrDefault(p => ResolveDisplaySeats(p, snapshot.Oficiales) > 0 || p.PorcentajeVoto > 0 || p.Votos > 0);

            list.Add(new CircunscripcionMayoriaSnapshot(
                snapshot.Circunscripcion.Codigo,
                snapshot.Circunscripcion.Nombre,
                topParty?.Codigo ?? string.Empty,
                topParty?.Siglas ?? string.Empty,
                snapshot.EscaniosTotales,
                topParty is not null ? ResolveDisplaySeats(topParty, snapshot.Oficiales) : 0,
                snapshot.Escrutado,
                snapshot.Participacion));
        }

        return list;
    }

    private static string FormatUpToTwoDecimals(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }
}
