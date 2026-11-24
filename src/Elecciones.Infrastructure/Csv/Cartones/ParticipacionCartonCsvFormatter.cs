using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using static Elecciones.Infrastructure.Csv.Cartones.CartonCsvFormattingHelpers;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Generador CSV específico para el cartón de Participación (1 archivo CSV).
/// Estructura preparada para ajustar columnas exactas según especificación de grafismo.
/// </summary>
public sealed class ParticipacionCartonCsvFormatter : ICartonCsvFormatter
{
    public CartonKind Kind => CartonKind.Participacion;

    private static readonly IReadOnlyList<string> Headers =
    [
        "Codigo",
        "Nombre",
        "Avance",
        "Participacion",
        "ParticipacionHistorica",
        "MediaParticipacion",
        "Avance1",
        "Avance1Historico",
        "HoraAvance1",
        "HoraAvance1Historico",
        "Avance2",
        "Avance2Historico",
        "HoraAvance2",
        "HoraAvance2Historico",
        "Avance3",
        "Avance3Historico",
        "HoraAvance3",
        "HoraAvance3Historico",
        "HoraParticipacion",
        "HoraParticipacionHistorico",
        "VotantesTotales"
    ];

    public IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions)
    {
        var fileName = EnsureCsvFileName(fileOptions.ParticipacionFileName, "Carton_Participacion.csv");

        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            [
                snapshot.Circunscripcion.Codigo,
                snapshot.Circunscripcion.Nombre,
                FormatInt(snapshot.Avance),
                FormatDecimal(snapshot.Participacion),
                FormatDecimal(snapshot.ParticipacionHistorica),
                FormatDecimal(snapshot.ParticipacionMedia),
                FormatDecimal(snapshot.Avance1),
                FormatDecimal(snapshot.Avance1Historico),
                snapshot.HoraAvance1,
                snapshot.HoraAvance1Historico,
                FormatDecimal(snapshot.Avance2),
                FormatDecimal(snapshot.Avance2Historico),
                snapshot.HoraAvance2,
                snapshot.HoraAvance2Historico,
                FormatDecimal(snapshot.Avance3),
                FormatDecimal(snapshot.Avance3Historico),
                snapshot.HoraAvance3,
                snapshot.HoraAvance3Historico,
                snapshot.HoraParticipacion,
                snapshot.HoraParticipacionHistorico,
                FormatInt(snapshot.VotantesTotales)
            ]
        ];

        return
        [
            new CartonCsvDescriptor(CartonCsvRoles.Unico, fileName, Headers, rows)
        ];
    }
}
