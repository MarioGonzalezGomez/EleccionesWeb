namespace Elecciones.Application.Models;

public sealed record GlobalOperationalSettings
{
    public int ElectionSlot { get; init; } = 1;
    public int ConnectionType { get; init; } = 0;
    public GraphicsTarget Target { get; init; } = GraphicsTarget.Ipf;
    public bool Oficiales { get; init; }
    public int Avance { get; init; }
    public string ExportName { get; init; } = "BrainStorm";
    public bool IncludeZeroSeats { get; init; }
    public bool AutoRefreshEnabled { get; init; }
    public int AutoRefreshIntervalSeconds { get; init; } = 5;
    public DateTimeOffset? LastAutoRefreshCheckUtc { get; init; }
    public DateTimeOffset? LastAutoRefreshAppliedUtc { get; init; }
    public string LastAutoRefreshStatus { get; init; } = string.Empty;
}

