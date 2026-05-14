namespace Elecciones.Application.Models;

public sealed class BrainStormSnapshot
{
    public CircunscripcionSummary Circunscripcion { get; init; } = new("", "");
    public bool Oficiales { get; init; }
    public int Avance { get; init; }
    public double Escrutado { get; init; }
    public double Participacion { get; init; }
    public double ParticipacionHistorica { get; init; }
    public double ParticipacionMedia { get; init; }
    public int EscaniosTotales { get; init; }
    public int MayoriaAbsoluta { get; init; }
    public int VotantesTotales { get; init; }
    public int VotantesFaltan { get; init; }
    public int AnioUltimasElecciones { get; init; }
    public int NumPartidos { get; init; }
    public string NombreSondeo { get; init; } = string.Empty;
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<PartidoSnapshot> Partidos { get; init; } = [];
    public IReadOnlyList<PartidoSnapshot> CsvTemplatePartidos { get; init; } = [];
}
