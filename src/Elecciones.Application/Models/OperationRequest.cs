namespace Elecciones.Application.Models;

public sealed class OperationRequest
{
    public string OperatorId { get; init; } = string.Empty;
    public GraphicModule Module { get; init; }
    public OperationActionType Action { get; init; }
    public GraphicsTarget Target { get; init; }
    public string CircunscripcionCodigo { get; init; } = string.Empty;
    public bool Oficiales { get; init; }
    public string ExportName { get; init; } = "BrainStorm";
}
