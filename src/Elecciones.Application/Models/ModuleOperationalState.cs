namespace Elecciones.Application.Models;

public sealed record ModuleOperationalState
{
    public GraphicModule Module { get; init; }
    public bool IsOnAir { get; init; }
    public string ActiveScene { get; init; } = string.Empty;
    public SnapshotQuery? ActiveQuery { get; init; }
    public bool Oficiales { get; init; } = true;
    public GraphicsTarget Target { get; init; } = GraphicsTarget.Both;
    public string LastOperatorId { get; init; } = string.Empty;
    public bool IsSedeExpanding { get; init; }
    public IReadOnlyList<string> ExpandedVideos { get; init; } = [];
    public IReadOnlyList<string> PactosIzquierda { get; init; } = [];
    public IReadOnlyList<string> PactosDerecha { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

