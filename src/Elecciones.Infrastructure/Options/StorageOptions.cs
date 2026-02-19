namespace Elecciones.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string CsvFolder { get; set; } = "Data/CSV";
}
