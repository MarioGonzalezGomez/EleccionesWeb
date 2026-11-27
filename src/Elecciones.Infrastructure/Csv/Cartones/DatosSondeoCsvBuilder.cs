using System.Globalization;
using Elecciones.Application.Models;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Construye <c>datos_sondeo.csv</c> para los gráficos de Cartón en modo Sondeo:
/// <c>partido;cod_partido;siglas;desde;hasta;diferencia</c>, calculando siempre la diferencia
/// entre <c>hasta</c> (<see cref="PartidoSnapshot.EscaniosHastaSondeo"/>) y los escaños históricos
/// (<c>+N</c>, <c>-N</c>, <c>=</c>, o <c>*</c> cuando el partido es nuevo).
/// </summary>
public static class DatosSondeoCsvBuilder
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "partido",
        "cod_partido",
        "siglas",
        "desde",
        "hasta",
        "diferencia"
    ];

    public static CartonCsvDescriptor Build(
        BrainStormSnapshot snapshot,
        string logicalRole,
        string? configuredFileName)
    {
        var fileName = EnsureCsvFileName(configuredFileName, "datos_sondeo.csv");
        var orderedParties = DatosPartidosCsvBuilder.GetOrderedAllElectionPartiesForCircunscripcion(snapshot);
        var rows = new List<IReadOnlyList<string>>(orderedParties.Count);

        for (var i = 0; i < orderedParties.Count; i++)
        {
            var p = orderedParties[i];
            var bloquePartido = $"Partido{(i + 1):D2}";
            var codCincoDigitos = DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(p.Codigo);
            var desde = p.EscaniosDesdeSondeo;
            var hasta = p.EscaniosHastaSondeo;

            string diferencia;
            if (DatosPartidosCsvBuilder.IsNewPartyWithoutHistoricalRecord(p))
            {
                diferencia = "*";
            }
            else
            {
                var diff = hasta - p.EscaniosHistoricos;
                diferencia = diff > 0
                    ? $"+{diff.ToString(CultureInfo.InvariantCulture)}"
                    : diff < 0
                        ? diff.ToString(CultureInfo.InvariantCulture)
                        : "=";
            }

            rows.Add(
            [
                bloquePartido,
                codCincoDigitos,
                p.Siglas,
                FormatInt(desde),
                FormatInt(hasta),
                diferencia
            ]);
        }

        return new CartonCsvDescriptor(logicalRole, fileName, Headers, rows);
    }
}
