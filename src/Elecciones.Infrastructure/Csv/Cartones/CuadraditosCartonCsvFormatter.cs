using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Generador CSV específico para el cartón de Cuadraditos (usa datos_partidos.csv).
/// </summary>
public sealed class CuadraditosCartonCsvFormatter : ICartonCsvFormatter
{
    public CartonKind Kind => CartonKind.Cuadraditos;

    public IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions)
    {
        return
        [
            DatosPartidosCsvBuilder.Build(snapshot, CartonCsvRoles.Unico, fileOptions.CuadraditosFileName)
        ];
    }
}
