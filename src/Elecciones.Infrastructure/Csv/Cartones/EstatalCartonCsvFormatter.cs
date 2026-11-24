using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Generador CSV específico para el cartón Estatal (2 archivos CSV: datos_partidos.csv + Provincia).
/// </summary>
public sealed class EstatalCartonCsvFormatter : ICartonCsvFormatter
{
    public CartonKind Kind => CartonKind.Estatal;

    private static readonly IReadOnlyList<string> ProvinciaHeaders =
    [
        "Codigo",
        "Nombre",
        "Comunidad",
        "Modo",
        "Escrutado",
        "EscrutadoGeneral",
        "EscaniosTotales",
        "MayoriaAbsoluta",
        "Participacion",
        "ParticipacionHistorica",
        "VotantesTotales",
        "VotantesFaltan",
        "NumPartidos",
        "NombreSondeo"
    ];

    public IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions)
    {
        return
        [
            DatosPartidosCsvBuilder.Build(snapshot, CartonCsvRoles.Partidos, fileOptions.EstatalPartidosFileName),
            BuildProvinciaDescriptor(snapshot, fileOptions)
        ];
    }

    private static CartonCsvDescriptor BuildProvinciaDescriptor(
        BrainStormSnapshot snapshot,
        CartonCsvFilesOptions fileOptions)
    {
        var fileName = EnsureCsvFileName(fileOptions.EstatalProvinciaFileName, "Carton_Estatal_Provincia.csv");
        var escrutadoGral = snapshot.EscrutadoGeneral > 0 ? snapshot.EscrutadoGeneral : snapshot.Escrutado;

        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            [
                snapshot.Circunscripcion.Codigo,
                snapshot.Circunscripcion.Nombre,
                snapshot.ComunidadCircunscripcion,
                snapshot.Oficiales ? "OFICIAL" : "SONDEO",
                FormatDecimal(snapshot.Escrutado),
                FormatDecimal(escrutadoGral),
                FormatInt(snapshot.EscaniosTotales),
                FormatInt(snapshot.MayoriaAbsoluta),
                FormatDecimal(snapshot.Participacion),
                FormatDecimal(snapshot.ParticipacionHistorica),
                FormatInt(snapshot.VotantesTotales),
                FormatInt(snapshot.VotantesFaltan),
                FormatInt(snapshot.NumPartidos),
                snapshot.NombreSondeo
            ]
        ];

        return new CartonCsvDescriptor(CartonCsvRoles.Provincia, fileName, ProvinciaHeaders, rows);
    }
}
