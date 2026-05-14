namespace Elecciones.Application.Models;

public sealed class SnapshotQuery
{
    public int ElectionSlot { get; init; } = 1;
    public int ConnectionType { get; init; }
    public SnapshotQueryKind Kind { get; init; } = SnapshotQueryKind.Circunscripcion;
    public string CircunscripcionCodigo { get; init; } = string.Empty;
    public string AutonomiaCodigo { get; init; } = string.Empty;
    public string PartidoCodigo { get; init; } = string.Empty;
    public int? Avance { get; init; }
    public bool IncludeZeroSeats { get; init; }
    public string MedioCodigo { get; init; } = string.Empty;
}
