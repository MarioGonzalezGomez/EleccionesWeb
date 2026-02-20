namespace Elecciones.Infrastructure.Entities;

public sealed class MedioEntity
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int Comparar { get; set; }
}
