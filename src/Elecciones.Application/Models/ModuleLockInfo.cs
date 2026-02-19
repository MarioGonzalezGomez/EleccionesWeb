namespace Elecciones.Application.Models;

public sealed class ModuleLockInfo
{
    public GraphicModule Module { get; init; }
    public bool IsLocked { get; init; }
    public string Owner { get; init; } = string.Empty;
    public DateTime? LockedAtUtc { get; init; }
    public DateTime? LastActionUtc { get; init; }
}
