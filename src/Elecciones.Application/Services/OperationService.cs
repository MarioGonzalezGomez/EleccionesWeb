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
    private readonly IOperationAuditService _auditService;

    public OperationService(
        IModuleLockService lockService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        IGraphicsGateway graphicsGateway,
        ISignalComposer signalComposer,
        IOperatorAuthorizationService authorizationService,
        IOperationAuditService auditService)
    {
        _lockService = lockService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _graphicsGateway = graphicsGateway;
        _signalComposer = signalComposer;
        _authorizationService = authorizationService;
        _auditService = auditService;
    }

    public async Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OperatorId))
        {
            var result = new OperationResult
            {
                Success = false,
                Message = "Operator is required."
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        if (!_authorizationService.CanOperate(request.OperatorId, request.Module))
        {
            var role = _authorizationService.GetRole(request.OperatorId);
            var result = new OperationResult
            {
                Success = false,
                Message = $"Operator '{request.OperatorId}' with role '{role}' cannot operate module '{request.Module}'."
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        if (!_authorizationService.CanExecuteAction(request.OperatorId, request.Module, request.Action))
        {
            var result = new OperationResult
            {
                Success = false,
                Message = $"Operator '{request.OperatorId}' cannot execute action '{request.Action}' on module '{request.Module}'."
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        if (request.Command != GraphicCommand.None
            && !_authorizationService.CanExecuteCommand(request.OperatorId, request.Module, request.Command))
        {
            var result = new OperationResult
            {
                Success = false,
                Message = $"Operator '{request.OperatorId}' cannot execute command '{request.Command}' on module '{request.Module}'."
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        var queryError = ValidateQuery(request.Query);
        if (!string.IsNullOrEmpty(queryError))
        {
            var result = new OperationResult
            {
                Success = false,
                Message = queryError
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        if (!_lockService.IsOwner(request.Module, request.OperatorId))
        {
            var result = new OperationResult
            {
                Success = false,
                Message = "The selected module is locked by another operator."
            };

            await _auditService.RecordAsync(request, result, cancellationToken);
            return result;
        }

        var snapshot = await _dataService.GetSnapshotAsync(
            request.Query,
            request.Oficiales,
            cancellationToken);

        var csvPath = await _csvWriter.WriteAsync(snapshot, request.ExportName, cancellationToken);
        await _csvWriter.WriteRecuentosJsonAsync(snapshot, cancellationToken);
        await WriteSedesCsvIfNeededAsync(request, snapshot, cancellationToken);

        var signal = _signalComposer.Compose(request, snapshot);
        var dispatchedTargets = await _graphicsGateway.SendAsync(request.Target, signal, cancellationToken);

        _lockService.MarkAction(request.Module, request.OperatorId, request.Action, request.Scene);

        var successResult = new OperationResult
        {
            Success = true,
            Message = BuildResultMessage(dispatchedTargets),
            CsvPath = csvPath,
            Signal = signal,
            DispatchedTargets = dispatchedTargets
        };

        await _auditService.RecordAsync(request, successResult, cancellationToken);
        return successResult;
    }

    private async Task WriteSedesCsvIfNeededAsync(
        OperationRequest request,
        BrainStormSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (request.Command is not (GraphicCommand.SedesEntra or GraphicCommand.SedesEncadena))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.CommandValue))
        {
            return;
        }

        await _csvWriter.WriteSedesAsync(snapshot, request.CommandValue, cancellationToken);
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
