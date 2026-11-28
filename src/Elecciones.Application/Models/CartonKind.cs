namespace Elecciones.Application.Models;

public enum CartonKind
{
    Participacion = 1,
    Mayorias = 2,
    Estatal = 3,
    UltimoEscano = 4,
    Mapa = 5
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
            "MAYORIAS" or "SONDEO_MAYORIAS" or "CCAA_CARTONES" => CartonKind.Mayorias,
            "ESTATAL" or "SONDEO_ESTATAL" or "CARTON_PARTIDOS" => CartonKind.Estatal,
            "ULTIMO_ESCANO" or "ULTIMOESCANO" => CartonKind.UltimoEscano,
            "MAPA" or "MAPA_ESCANOS" => CartonKind.Mapa,
            _ => CartonKind.Participacion
        };
    }

    public static string ToDefaultSceneName(CartonKind kind, bool oficiales = true) => kind switch
    {
        CartonKind.Participacion => "PARTICIPACION",
        CartonKind.Mayorias => oficiales ? "MAYORIAS" : "SONDEO_MAYORIAS",
        CartonKind.Estatal => oficiales ? "ESTATAL" : "SONDEO_ESTATAL",
        CartonKind.UltimoEscano => "ULTIMOESCANO",
        CartonKind.Mapa => "MAPA_ESCANOS",
        _ => "PARTICIPACION"
    };
}
