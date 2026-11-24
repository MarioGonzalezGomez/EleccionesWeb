using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;

namespace Elecciones.Infrastructure.Csv.Cartones;

/// <summary>
/// Generador CSV específico para el cartón de Mayorías (2 archivos CSV: datos_partidos.csv + mayorias_provincias.csv).
/// </summary>
public sealed class MayoriasCartonCsvFormatter : ICartonCsvFormatter
{
    public CartonKind Kind => CartonKind.Mayorias;

    public IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions)
    {
        return
        [
            DatosPartidosCsvBuilder.Build(snapshot, CartonCsvRoles.Partidos, fileOptions.MayoriasPartidosFileName),
            MayoriasProvinciasCsvBuilder.Build(snapshot, CartonCsvRoles.Provincia, fileOptions.MayoriasProvinciaFileName)
        ];
    }
}
