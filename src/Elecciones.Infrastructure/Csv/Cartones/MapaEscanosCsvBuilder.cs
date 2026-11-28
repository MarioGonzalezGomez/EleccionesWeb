using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye <c>mapa_escanos.csv</c> con los escaños oficiales de cada partido en las provincias.
/// </summary>
public static class MapaEscanosCsvBuilder
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "cod_lugar",
        "cod_partido",
        "escanos",
        "disputa_con"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "mapa_escanos.csv");
        var rows = snapshot.MapaEscanos
            .Where(entry => entry.Escanos > 0
                && !string.IsNullOrWhiteSpace(entry.CodigoLugar)
                && !string.IsNullOrWhiteSpace(entry.CodigoPartido))
            .OrderBy(entry => entry.CodigoLugar, StringComparer.Ordinal)
            .ThenBy(entry => DatosPartidosCsvBuilder.ParseSortablePartyCode(entry.CodigoPartido))
            .ThenBy(entry => DatosPartidosCsvBuilder.NormalizePartyCode(entry.CodigoPartido), StringComparer.Ordinal)
            .Select(entry => (IReadOnlyList<string>)
            [
                entry.CodigoLugar.Trim(),
                DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(entry.CodigoPartido),
                FormatInt(entry.Escanos),
                string.IsNullOrWhiteSpace(entry.DisputaCon)
                    ? string.Empty
                    : DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(entry.DisputaCon)
            ])
            .ToList();

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
    }
}
