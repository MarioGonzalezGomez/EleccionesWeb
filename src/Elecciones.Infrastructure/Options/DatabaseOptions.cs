namespace Elecciones.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public string HostBackup { get; set; } = string.Empty;
    public string HostLocal { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Name { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 5;
    public int NumEleccionesSimultaneas { get; set; } = 1;
    public int DefaultElectionSlot { get; set; } = 1;
    public int ConnectionDefault1 { get; set; } = 1;
    public int ConnectionDefault2 { get; set; } = 1;
    public int ConnectionDefault3 { get; set; } = 1;
    public int TipoElecciones { get; set; } = 1;
    public bool Regional { get; set; }
    public string CodigoRegionalBD1 { get; set; } = string.Empty;
    public string CodigoRegionalBD2 { get; set; } = string.Empty;
    public string CodigoRegionalBD3 { get; set; } = string.Empty;
    public string Name2 { get; set; } = string.Empty;
    public string Name3 { get; set; } = string.Empty;
}
