namespace Elecciones.Infrastructure.Entities;

public sealed class PartidoEntity
{
    public string Codigo { get; set; } = string.Empty;
    public string Siglas { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Padre { get; set; } = string.Empty;

    public ICollection<CircunscripcionPartidoEntity> Circunscripciones { get; set; } = [];
}
