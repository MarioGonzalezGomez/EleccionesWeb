namespace Elecciones.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 5;
}
