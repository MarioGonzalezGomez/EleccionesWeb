using Elecciones.Application.Models;

namespace Elecciones.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string CsvFolder { get; set; } = "Data/CSV";
    public string JsonFolder { get; set; } = "Data/JSON";
    public string BrainStormCsvFormat { get; set; } = "NEW";
    public bool EnableRecuentosJson { get; set; } = true;
    public string RecuentosJsonFileName { get; set; } = "datos_escrutado";
    public Dictionary<string, string> ModuleCsvFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CsvFoldersByModule { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModuleJsonFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> JsonFoldersByModule { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CartonCsvFilesOptions CartonCsvFiles { get; set; } = new();

    public string ResolveCsvFolderForModule(GraphicModule? module)
    {
        if (module.HasValue)
        {
            var key = module.Value.ToString();
            if (ModuleCsvFolders is not null
                && ModuleCsvFolders.TryGetValue(key, out var modulePath)
                && !string.IsNullOrWhiteSpace(modulePath))
            {
                return modulePath.Trim();
            }

            if (CsvFoldersByModule is not null
                && CsvFoldersByModule.TryGetValue(key, out var byModulePath)
                && !string.IsNullOrWhiteSpace(byModulePath))
            {
                return byModulePath.Trim();
            }
        }

        return CsvFolder;
    }

    public string ResolveJsonFolderForModule(GraphicModule? module)
    {
        if (module.HasValue)
        {
            var key = module.Value.ToString();
            if (ModuleJsonFolders is not null
                && ModuleJsonFolders.TryGetValue(key, out var modulePath)
                && !string.IsNullOrWhiteSpace(modulePath))
            {
                return modulePath.Trim();
            }

            if (JsonFoldersByModule is not null
                && JsonFoldersByModule.TryGetValue(key, out var byModulePath)
                && !string.IsNullOrWhiteSpace(byModulePath))
            {
                return byModulePath.Trim();
            }
        }

        return JsonFolder;
    }

    public string ResolveCsvFolderOverride(string moduleName)
    {
        if (ModuleCsvFolders is not null
            && ModuleCsvFolders.TryGetValue(moduleName, out var modulePath)
            && !string.IsNullOrWhiteSpace(modulePath))
        {
            return modulePath.Trim();
        }

        if (CsvFoldersByModule is not null
            && CsvFoldersByModule.TryGetValue(moduleName, out var byModulePath)
            && !string.IsNullOrWhiteSpace(byModulePath))
        {
            return byModulePath.Trim();
        }

        return string.Empty;
    }
}

public sealed class CartonCsvFilesOptions
{
    public string Delimiter { get; set; } = ";";
    public string ParticipacionFileName { get; set; } = "Carton_Participacion.csv";
    public string MayoriasPartidosFileName { get; set; } = "datos_partidos.csv";
    public string MayoriasProvinciaFileName { get; set; } = "mayorias_provincias.csv";
    public string EstatalPartidosFileName { get; set; } = "datos_partidos.csv";
    public string EstatalProvinciaFileName { get; set; } = "Carton_Estatal_Provincia.csv";
    public string CuadraditosFileName { get; set; } = "datos_partidos.csv";
    public string UltimoEscanoFileName { get; set; } = "Carton_UltimoEscano.csv";
}
