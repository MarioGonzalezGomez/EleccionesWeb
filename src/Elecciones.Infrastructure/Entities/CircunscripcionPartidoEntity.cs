namespace Elecciones.Infrastructure.Entities;

public sealed class CircunscripcionPartidoEntity
{
    public string CodCircunscripcion { get; set; } = string.Empty;
    public string CodPartido { get; set; } = string.Empty;

    public int EscaniosHasta { get; set; }
    public int EscaniosDesdeSondeo { get; set; }
    public int EscaniosHastaSondeo { get; set; }
    public int EscaniosHistoricos { get; set; }
    public double Votos { get; set; }
    public double VotosSondeo { get; set; }
    public int Votantes { get; set; }

    public CircunscripcionEntity? Circunscripcion { get; set; }
    public PartidoEntity? Partido { get; set; }
}
