namespace Elecciones.Infrastructure.Entities;

public sealed class MedioPartidoEntity
{
    public string CodCircunscripcion { get; set; } = string.Empty;
    public string CodMedio { get; set; } = string.Empty;
    public string CodPartido { get; set; } = string.Empty;

    public int EscaniosDesde { get; set; }
    public int EscaniosHasta { get; set; }
    public double Votos { get; set; }
}
