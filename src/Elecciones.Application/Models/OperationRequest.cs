namespace Elecciones.Application.Models;

public sealed class OperationRequest
{
    public string OperatorId { get; init; } = string.Empty;
    public GraphicModule Module { get; init; }
    public string Scene { get; init; } = string.Empty;
    public OperationActionType Action { get; init; }
    public GraphicsTarget Target { get; init; }
    public bool Oficiales { get; init; }
    public string ExportName { get; init; } = "BrainStorm";
    public SnapshotQuery Query { get; init; } = new();
}
