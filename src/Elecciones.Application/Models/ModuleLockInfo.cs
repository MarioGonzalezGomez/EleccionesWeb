namespace Elecciones.Application.Models;

public sealed class ModuleLockInfo
{
    public GraphicModule Module { get; init; }
    public bool IsLocked { get; init; }
    public bool IsOnAir { get; init; }
    public string Owner { get; init; } = string.Empty;
    public DateTime? LockedAtUtc { get; init; }
    public DateTime? LastActionUtc { get; init; }
    public OperationActionType? LastActionType { get; init; }
    public string LastScene { get; init; } = string.Empty;
}
