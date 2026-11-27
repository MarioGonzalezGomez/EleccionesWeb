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
    private readonly IOperationStateService? _stateService;
    private readonly ISignalDiagnosticsService? _diagnosticsService;

    public OperationService(
        IModuleLockService lockService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        IGraphicsGateway graphicsGateway,
        ISignalComposer signalComposer,
        IOperatorAuthorizationService authorizationService,
        IOperationAuditService auditService,
        IOperationStateService? stateService = null,
        ISignalDiagnosticsService? diagnosticsService = null)
    {
        _lockService = lockService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _graphicsGateway = graphicsGateway;
        _signalComposer = signalComposer;
        _authorizationService = authorizationService;
        _auditService = auditService;
        _stateService = stateService;
        _diagnosticsService = diagnosticsService;
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

        var isPureLanguageCommand = GraphicLanguageExtensions.TryFromCommand(request.Command, out _);
        if (!isPureLanguageCommand)
        {
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

        BrainStormSnapshot snapshot;
        var csvPath = string.Empty;
        IReadOnlyList<string> generatedCsvPaths = [];

        if (isPureLanguageCommand)
        {
            snapshot = new BrainStormSnapshot
            {
                Oficiales = request.Oficiales
            };
        }
        else
        {
            snapshot = await _dataService.GetSnapshotAsync(
                request.Query,
                request.Oficiales,
                cancellationToken);

            if (request.Module == GraphicModule.Carton)
            {
                var allCartonArtifacts = await _csvWriter.WriteAllCartonFilesAsync(
                    snapshot,
                    request.Query,
                    cancellationToken,
                    request.Module);
                csvPath = allCartonArtifacts.FirstOrDefault()?.FullPath ?? string.Empty;
                generatedCsvPaths = allCartonArtifacts.Select(f => f.FullPath).ToList();
            }
            else
            {
                csvPath = await _csvWriter.WriteAsync(snapshot, request.ExportName, cancellationToken, request.Module);
                generatedCsvPaths = string.IsNullOrWhiteSpace(csvPath) ? [] : [csvPath];
                await _csvWriter.WriteRecuentosJsonAsync(snapshot, cancellationToken, request.Module);
            }

            await WriteSedesCsvIfNeededAsync(request, snapshot, cancellationToken);
        }

        var signal = _signalComposer.Compose(request, snapshot);
        var beforeSendUtc = DateTime.UtcNow.AddMilliseconds(-5);
        var dispatchedTargets = string.IsNullOrWhiteSpace(signal)
            ? []
            : await _graphicsGateway.SendAsync(request.Target, signal, cancellationToken, request.Module);

        _lockService.MarkAction(request.Module, request.OperatorId, request.Action, request.Scene);
        _stateService?.RecordExecution(request, snapshot);

        var recentEntries = _diagnosticsService?
            .GetRecentEntries(request.Module, maxCount: 4)
            .Where(e => e.TimestampUtc >= beforeSendUtc)
            .ToList() ?? [];

        var recentAttempt = dispatchedTargets.Count > 0
            ? recentEntries.FirstOrDefault(e => e.Delivered)
            : (recentEntries.FirstOrDefault(e => e.StatusMessage.StartsWith("Error TCP", StringComparison.OrdinalIgnoreCase))
               ?? recentEntries.FirstOrDefault());

        var hasTcpError = !string.IsNullOrWhiteSpace(signal)
            && dispatchedTargets.Count == 0
            && recentAttempt is not null
            && recentAttempt.StatusMessage.StartsWith("Error TCP", StringComparison.OrdinalIgnoreCase);

        var operationResult = new OperationResult
        {
            Success = !hasTcpError,
            Message = BuildResultMessage(signal, dispatchedTargets, generatedCsvPaths, recentAttempt),
            CsvPath = csvPath,
            GeneratedCsvPaths = generatedCsvPaths,
            Signal = signal,
            DispatchedTargets = dispatchedTargets
        };

        await _auditService.RecordAsync(request, operationResult, cancellationToken);
        return operationResult;
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

        await _csvWriter.WriteSedesAsync(snapshot, request.CommandValue, cancellationToken, request.Module);
    }

    private static string BuildResultMessage(
        string signal,
        IReadOnlyList<string> dispatchedTargets,
        IReadOnlyList<string> generatedCsvPaths,
        SignalDiagnosticEntry? recentAttempt)
    {
        var filesLabel = generatedCsvPaths.Count > 0
            ? $" CSV: {string.Join(", ", generatedCsvPaths.Select(Path.GetFileName))}."
            : string.Empty;

        if (string.IsNullOrWhiteSpace(signal))
        {
            return $"Estado operativo actualizado.{filesLabel}";
        }

        if (dispatchedTargets.Count == 0)
        {
            if (recentAttempt is not null && recentAttempt.StatusMessage.StartsWith("Error TCP", StringComparison.OrdinalIgnoreCase))
            {
                return $"No se pudo conectar con {recentAttempt.EndpointName} ({recentAttempt.Host}:{recentAttempt.Port}): {recentAttempt.StatusMessage}.{filesLabel}";
            }

            if (recentAttempt is not null)
            {
                return $"Acción ejecutada ({recentAttempt.EndpointName} {recentAttempt.Host}:{recentAttempt.Port} desactivado en Configuración Técnica).{filesLabel}";
            }

            return $"Acción ejecutada (sin destinos gráficos activos).{filesLabel}";
        }

        if (recentAttempt is { Delivered: true })
        {
            return $"Acción enviada a {string.Join(", ", dispatchedTargets)} ({recentAttempt.Host}:{recentAttempt.Port} · {recentAttempt.Bd}).{filesLabel}";
        }

        return $"Acción enviada a: {string.Join(", ", dispatchedTargets)}.{filesLabel}";
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
