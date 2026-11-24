using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Generador CSV específico para el cartón de Último Escaño (1 archivo CSV).
/// Estructura preparada para ajustar columnas exactas según especificación de grafismo.
/// </summary>
public sealed class UltimoEscanoCartonCsvFormatter : ICartonCsvFormatter
{
    public CartonKind Kind => CartonKind.UltimoEscano;

    private static readonly IReadOnlyList<string> Headers =
    [
        "CodigoCircunscripcion",
        "NombreCircunscripcion",
        "Escrutado",
        "EscaniosTotales",
        "CodigoUltimo",
        "SiglasUltimo",
        "EscaniosUltimo",
        "VotosUltimo",
        "PorcentajeUltimo",
        "CodigoDisputa",
        "SiglasDisputa",
        "EscaniosDisputa",
        "VotosDisputa",
        "PorcentajeDisputa",
        "RestoVotos"
    ];

    public IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions)
    {
        var fileName = EnsureCsvFileName(fileOptions.UltimoEscanoFileName, "Carton_UltimoEscano.csv");
        var partidos = OrderPartiesForDisplay(snapshot);
        var ultimo = partidos.FirstOrDefault(p => p.EsUltimoEscano != 0);
        var disputa = partidos.FirstOrDefault(p => p.LuchaUltimoEscano != 0);

        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            [
                snapshot.Circunscripcion.Codigo,
                snapshot.Circunscripcion.Nombre,
                FormatDecimal(snapshot.Escrutado),
                FormatInt(snapshot.EscaniosTotales),
                ultimo?.Codigo ?? string.Empty,
                ultimo?.Siglas ?? string.Empty,
                FormatInt(ultimo is not null ? ResolveDisplaySeats(ultimo, snapshot.Oficiales) : 0),
                FormatInt(ultimo?.Votos ?? 0),
                FormatDecimal(ultimo?.PorcentajeVoto ?? 0),
                disputa?.Codigo ?? string.Empty,
                disputa?.Siglas ?? string.Empty,
                FormatInt(disputa is not null ? ResolveDisplaySeats(disputa, snapshot.Oficiales) : 0),
                FormatInt(disputa?.Votos ?? 0),
                FormatDecimal(disputa?.PorcentajeVoto ?? 0),
                FormatInt(disputa?.RestoVotos ?? 0)
            ]
        ];

        return
        [
            new CartonCsvDescriptor(CartonCsvRoles.Unico, fileName, Headers, rows)
        ];
    }
}
