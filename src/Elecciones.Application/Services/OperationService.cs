using System.Globalization;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class OperationService : IOperationService
{
    private readonly IModuleLockService _lockService;
    private readonly IEleccionesDataService _dataService;
    private readonly IBrainStormCsvWriter _csvWriter;
    private readonly IGraphicsGateway _graphicsGateway;

    public OperationService(
        IModuleLockService lockService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        IGraphicsGateway graphicsGateway)
    {
        _lockService = lockService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _graphicsGateway = graphicsGateway;
    }

    public async Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "Operator is required."
            };
        }

        if (!_lockService.IsOwner(request.Module, request.OperatorId))
        {
            return new OperationResult
            {
                Success = false,
                Message = "The selected module is locked by another operator."
            };
        }

        var snapshot = await _dataService.GetSnapshotAsync(
            request.CircunscripcionCodigo,
            request.Oficiales,
            cancellationToken);

        var csvPath = await _csvWriter.WriteAsync(snapshot, request.ExportName, cancellationToken);

        var signal = BuildSignal(request, snapshot);
        var dispatchedTargets = await _graphicsGateway.SendAsync(request.Target, signal, cancellationToken);

        _lockService.MarkAction(request.Module, request.OperatorId);

        return new OperationResult
        {
            Success = true,
            Message = BuildResultMessage(dispatchedTargets),
            CsvPath = csvPath,
            Signal = signal,
            DispatchedTargets = dispatchedTargets
        };
    }

    private static string BuildSignal(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var oficiales = request.Oficiales ? "OFICIAL" : "SONDEO";
        var timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return string.Join(
            ';',
            $"module={request.Module}",
            $"action={request.Action}",
            $"circ={snapshot.Circunscripcion.Codigo}",
            $"mode={oficiales}",
            $"avance={snapshot.Avance}",
            $"escrutado={snapshot.Escrutado.ToString("F2", CultureInfo.InvariantCulture)}",
            $"at={timestamp}");
    }

    private static string BuildResultMessage(IReadOnlyList<string> dispatchedTargets)
    {
        if (dispatchedTargets.Count == 0)
        {
            return "Action executed. No graphics target was enabled.";
        }

        return $"Action executed and sent to: {string.Join(", ", dispatchedTargets)}";
    }
}
