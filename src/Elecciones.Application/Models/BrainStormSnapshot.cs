namespace Elecciones.Application.Models;

public sealed class BrainStormSnapshot
{
    public CircunscripcionSummary Circunscripcion { get; init; } = new("", "");
    public bool Oficiales { get; init; }
    public int Avance { get; init; }
    public double Escrutado { get; init; }
    public int EscaniosTotales { get; init; }
    public int MayoriaAbsoluta { get; init; }
    public int VotantesTotales { get; init; }
    public int AnioUltimasElecciones { get; init; }
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<PartidoSnapshot> Partidos { get; init; } = [];
}
