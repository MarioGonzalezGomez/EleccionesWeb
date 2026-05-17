namespace Elecciones.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string CsvFolder { get; set; } = "Data/CSV";
    public string JsonFolder { get; set; } = "Data/JSON";
    public string BrainStormCsvFormat { get; set; } = "NEW";
    public bool EnableRecuentosJson { get; set; } = true;
    public string RecuentosJsonFileName { get; set; } = "datos_escrutado";
}
