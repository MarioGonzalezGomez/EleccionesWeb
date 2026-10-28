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
    public Dictionary<string, string> ModuleJsonFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveCsvFolderForModule(GraphicModule? module)
    {
        if (module.HasValue
            && ModuleCsvFolders is not null
            && ModuleCsvFolders.TryGetValue(module.Value.ToString(), out var modulePath)
            && !string.IsNullOrWhiteSpace(modulePath))
        {
            return modulePath.Trim();
        }

        return CsvFolder;
    }

    public string ResolveJsonFolderForModule(GraphicModule? module)
    {
        if (module.HasValue
            && ModuleJsonFolders is not null
            && ModuleJsonFolders.TryGetValue(module.Value.ToString(), out var modulePath)
            && !string.IsNullOrWhiteSpace(modulePath))
        {
            return modulePath.Trim();
        }

        return JsonFolder;
    }
}
