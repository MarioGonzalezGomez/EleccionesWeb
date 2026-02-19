namespace Elecciones.Infrastructure.Entities;

public sealed class CircunscripcionEntity
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public double Escrutado { get; set; }
    public int Escanios { get; set; }
    public int Avance1 { get; set; }
    public int Avance2 { get; set; }
    public int Avance3 { get; set; }
    public double Participacion { get; set; }
    public int Votantes { get; set; }
    public double ParticipacionHist { get; set; }
    public string Comunidad { get; set; } = string.Empty;

    public ICollection<CircunscripcionPartidoEntity> Partidos { get; set; } = [];
}
