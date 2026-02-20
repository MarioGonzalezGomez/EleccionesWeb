using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class OperationService : IOperationService
{
    private readonly IModuleLockService _lockService;
    private readonly IEleccionesDataService _dataService;
    private readonly IBrainStormCsvWriter _csvWriter;
    private readonly IGraphicsGateway _graphicsGateway;
    private readonly ISignalComposer _signalComposer;
    private readonly IOperatorAuthorizationService _authorizationService;

    public OperationService(
        IModuleLockService lockService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        IGraphicsGateway graphicsGateway,
        ISignalComposer signalComposer,
        IOperatorAuthorizationService authorizationService)
    {
        _lockService = lockService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _graphicsGateway = graphicsGateway;
        _signalComposer = signalComposer;
        _authorizationService = authorizationService;
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

        if (!_authorizationService.CanOperate(request.OperatorId, request.Module))
        {
            var role = _authorizationService.GetRole(request.OperatorId);
            return new OperationResult
            {
                Success = false,
                Message = $"Operator '{request.OperatorId}' with role '{role}' cannot operate module '{request.Module}'."
            };
        }

        var queryError = ValidateQuery(request.Query);
        if (!string.IsNullOrEmpty(queryError))
        {
            return new OperationResult
            {
                Success = false,
                Message = queryError
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
            request.Query,
            request.Oficiales,
            cancellationToken);

        var csvPath = await _csvWriter.WriteAsync(snapshot, request.ExportName, cancellationToken);

        var signal = _signalComposer.Compose(request, snapshot);
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

    private static string BuildResultMessage(IReadOnlyList<string> dispatchedTargets)
    {
        if (dispatchedTargets.Count == 0)
        {
            return "Action executed. No graphics target was enabled.";
        }

        return $"Action executed and sent to: {string.Join(", ", dispatchedTargets)}";
    }

    private static string ValidateQuery(SnapshotQuery? query)
    {
        if (query is null)
        {
            return "Query is required.";
        }

        if (query.Kind == SnapshotQueryKind.Circunscripcion
            && string.IsNullOrWhiteSpace(query.CircunscripcionCodigo))
        {
            return "Circunscripcion code is required for Circunscripcion query mode.";
        }

        if (query.Kind is SnapshotQueryKind.MasVotadosProvincias or SnapshotQueryKind.PartidoProvincias)
        {
            if (string.IsNullOrWhiteSpace(query.AutonomiaCodigo))
            {
                return "Autonomia code is required for provincias query modes.";
            }
        }

        if (query.Kind is SnapshotQueryKind.PartidoAutonomias or SnapshotQueryKind.PartidoProvincias)
        {
            if (string.IsNullOrWhiteSpace(query.PartidoCodigo))
            {
                return "Partido code is required for partido query modes.";
            }
        }

        return string.Empty;
    }
}
