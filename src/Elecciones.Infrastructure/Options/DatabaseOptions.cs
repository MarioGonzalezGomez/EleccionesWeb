namespace Elecciones.Infrastructure.Options;

public sealed record ActiveElectionDescriptor(
    int Slot,
    string Label,
    string DatabaseName,
    string RegionalCode);

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public string HostBackup { get; set; } = string.Empty;
    public string HostLocal { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Name { get; set; } = string.Empty;
    public string Name2 { get; set; } = string.Empty;
    public string Name3 { get; set; } = string.Empty;
    public string Label1 { get; set; } = "Elección 1";
    public string Label2 { get; set; } = "Elección 2";
    public string Label3 { get; set; } = "Elección 3";
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

    public int ClampedActiveCount => Math.Clamp(NumEleccionesSimultaneas, 1, 3);

    public int ClampSlot(int requestedSlot) => Math.Clamp(requestedSlot, 1, ClampedActiveCount);

    public string GetElectionLabel(int slot)
    {
        var raw = slot switch
        {
            2 => Label2,
            3 => Label3,
            _ => Label1
        };

        return string.IsNullOrWhiteSpace(raw) ? $"Elección {Math.Clamp(slot, 1, 3)}" : raw.Trim();
    }

    public string GetDatabaseName(int slot)
    {
        return slot switch
        {
            2 => Name2?.Trim() ?? string.Empty,
            3 => Name3?.Trim() ?? string.Empty,
            _ => Name?.Trim() ?? string.Empty
        };
    }

    public string GetRegionalCode(int slot)
    {
        return slot switch
        {
            2 => CodigoRegionalBD2?.Trim() ?? string.Empty,
            3 => CodigoRegionalBD3?.Trim() ?? string.Empty,
            _ => CodigoRegionalBD1?.Trim() ?? string.Empty
        };
    }

    public IReadOnlyList<ActiveElectionDescriptor> GetActiveElections()
    {
        var count = ClampedActiveCount;
        var list = new List<ActiveElectionDescriptor>(count);
        for (var slot = 1; slot <= count; slot++)
        {
            list.Add(new ActiveElectionDescriptor(
                slot,
                GetElectionLabel(slot),
                GetDatabaseName(slot),
                GetRegionalCode(slot)));
        }

        return list;
    }
}
