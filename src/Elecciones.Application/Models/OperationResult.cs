namespace Elecciones.Application.Models;

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string CsvPath { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public IReadOnlyList<string> DispatchedTargets { get; init; } = [];
}
