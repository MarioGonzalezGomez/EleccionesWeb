namespace Elecciones.Application.Models;

public enum CartonKind
{
    Participacion = 1,
    Mayorias = 2,
    Estatal = 3,
    Cuadraditos = 4,
    UltimoEscano = 5
}

public sealed record CartonCsvFileArtifact(
    string LogicalRole,
    string FileName,
    string FullPath
);

public sealed record CartonCsvExportResult(
    CartonKind Kind,
    IReadOnlyList<CartonCsvFileArtifact> Files
)
{
    public string PrimaryPath => Files.FirstOrDefault()?.FullPath ?? string.Empty;
}

public static class CartonKindResolver
{
    public static CartonKind Resolve(OperationRequest request)
    {
        if (request.CartonKind.HasValue)
        {
            return request.CartonKind.Value;
        }

        return ResolveFromScene(request.Scene);
    }

    public static CartonKind ResolveFromScene(string? scene)
    {
        if (string.IsNullOrWhiteSpace(scene))
        {
            return CartonKind.Participacion;
        }

        return scene.Trim().ToUpperInvariant() switch
        {
            "PARTICIPACION" => CartonKind.Participacion,
            "MAYORIAS" or "CCAA_CARTONES" => CartonKind.Mayorias,
            "ESTATAL" or "CARTON_PARTIDOS" => CartonKind.Estatal,
            "CUADRADITOS" or "CARRUSEL" => CartonKind.Cuadraditos,
            "ULTIMO_ESCANO" or "ULTIMOESCANO" => CartonKind.UltimoEscano,
            _ => CartonKind.Participacion
        };
    }

    public static string ToDefaultSceneName(CartonKind kind) => kind switch
    {
        CartonKind.Participacion => "PARTICIPACION",
        CartonKind.Mayorias => "MAYORIAS",
        CartonKind.Estatal => "ESTATAL",
        CartonKind.Cuadraditos => "CUADRADITOS",
        CartonKind.UltimoEscano => "ULTIMO_ESCANO",
        _ => "PARTICIPACION"
    };
}
