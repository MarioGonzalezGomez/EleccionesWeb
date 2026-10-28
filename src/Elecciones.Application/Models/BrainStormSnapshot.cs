namespace Elecciones.Application.Models;

public sealed record BrainStormSnapshot
{
    public CircunscripcionSummary Circunscripcion { get; init; } = new("", "");
    public bool Oficiales { get; init; }
    public int Avance { get; init; }
    public double Escrutado { get; init; }
    public double Participacion { get; init; }
    public double ParticipacionHistorica { get; init; }
    public double ParticipacionMedia { get; init; }
    public double Avance1 { get; init; }
    public double Avance2 { get; init; }
    public double Avance3 { get; init; }
    public double Avance1Historico { get; init; }
    public double Avance2Historico { get; init; }
    public double Avance3Historico { get; init; }
    public string HoraAvance1 { get; init; } = string.Empty;
    public string HoraAvance2 { get; init; } = string.Empty;
    public string HoraAvance3 { get; init; } = string.Empty;
    public string HoraParticipacion { get; init; } = string.Empty;
    public string HoraAvance1Historico { get; init; } = string.Empty;
    public string HoraAvance2Historico { get; init; } = string.Empty;
    public string HoraAvance3Historico { get; init; } = string.Empty;
    public string HoraParticipacionHistorico { get; init; } = string.Empty;
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
    public IReadOnlyList<ProvinciaMapaSnapshot> ProvinciasAutonomia { get; init; } = [];
    public IReadOnlyList<string> TodasLasProvinciasMapa { get; init; } = [];
    public IReadOnlyList<VideoSlotSnapshot> VideoSlots { get; init; } = [];
}
