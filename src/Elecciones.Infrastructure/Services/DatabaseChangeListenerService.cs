using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Microsoft.Extensions.Logging;

namespace Elecciones.Infrastructure.Services;

public sealed class DatabaseChangeListenerService
{
    private readonly IOperationStateService _stateService;
    private readonly IEleccionesDataService _dataService;
    private readonly IBrainStormCsvWriter _csvWriter;
    private readonly ISignalComposer _signalComposer;
    private readonly IGraphicsGateway _graphicsGateway;
    private readonly ILogger<DatabaseChangeListenerService> _logger;
    private readonly IGlobalOperationalSettingsService? _globalSettings;
    private readonly Dictionary<GraphicModule, string> _lastHashes = new();

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public DatabaseChangeListenerService(
        IOperationStateService stateService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        ISignalComposer signalComposer,
        IGraphicsGateway graphicsGateway,
        ILogger<DatabaseChangeListenerService> logger,
        IGlobalOperationalSettingsService? globalSettings = null)
    {
        _stateService = stateService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _signalComposer = signalComposer;
        _graphicsGateway = graphicsGateway;
        _logger = logger;
        _globalSettings = globalSettings;
    }

    public async Task RunPollingLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = _globalSettings is not null
                ? Math.Clamp(_globalSettings.GetSettings().AutoRefreshIntervalSeconds, 2, 60)
                : Math.Max(1, (int)PollInterval.TotalSeconds);

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);

            if (_globalSettings is not null && !_globalSettings.GetSettings().AutoRefreshEnabled)
            {
                continue;
            }

            await CheckAndDispatchUpdatesOnceAsync(stoppingToken);
        }
    }

    public async Task<int> CheckAndDispatchUpdatesOnceAsync(CancellationToken cancellationToken = default)
    {
        var dispatchedCount = 0;
        var updatedModules = new List<GraphicModule>();
        var states = _stateService.GetAllStates();
        var onAirStates = states.Where(s => s.IsOnAir && s.ActiveQuery is not null).ToList();
        var checkedAtUtc = DateTimeOffset.UtcNow;

        if (onAirStates.Count == 0)
        {
            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, "Sin módulos en aire.");
            return 0;
        }

        foreach (var state in onAirStates)
        {
            try
            {
                var snapshot = await _dataService.GetSnapshotAsync(state.ActiveQuery!, state.Oficiales, cancellationToken);
                var hash = ComputeSnapshotHash(snapshot);

                if (!_lastHashes.TryGetValue(state.Module, out var previousHash))
                {
                    _lastHashes[state.Module] = hash;
                    continue;
                }

                if (string.Equals(previousHash, hash, StringComparison.Ordinal))
                {
                    continue;
                }

                _lastHashes[state.Module] = hash;

                var exportName = state.Oficiales ? "Oficial_Datos" : "Sondeo_Datos";
                await _csvWriter.WriteAsync(snapshot, exportName, cancellationToken, state.Module);
                await _csvWriter.WriteRecuentosJsonAsync(snapshot, cancellationToken, state.Module);

                var updateRequest = new OperationRequest
                {
                    OperatorId = string.IsNullOrWhiteSpace(state.LastOperatorId) ? "system-listener" : state.LastOperatorId,
                    Module = state.Module,
                    Action = OperationActionType.Update,
                    Scene = state.ActiveScene,
                    Oficiales = state.Oficiales,
                    Target = state.Target,
                    Query = state.ActiveQuery!
                };

                var signal = _signalComposer.Compose(updateRequest, snapshot);
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    await _graphicsGateway.SendAsync(state.Target, signal, cancellationToken, state.Module);
                    dispatchedCount++;
                    updatedModules.Add(state.Module);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error in DatabaseChangeListenerService while polling module {Module}", state.Module);
                _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, $"Error en {state.Module}: {ex.Message}");
            }
        }

        if (dispatchedCount > 0)
        {
            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, DateTimeOffset.UtcNow, $"Autoactualizados: {string.Join(", ", updatedModules)}");
        }
        else
        {
            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, "Sin cambios de datos.");
        }

        return dispatchedCount;
    }

    private static string ComputeSnapshotHash(BrainStormSnapshot snapshot)
    {
        var payload = JsonSerializer.Serialize(new
        {
            snapshot.Circunscripcion.Codigo,
            snapshot.Escrutado,
            snapshot.Avance,
            snapshot.Avance1,
            snapshot.Avance2,
            snapshot.Avance3,
            snapshot.Participacion,
            Partidos = snapshot.Partidos.Select(p => new
            {
                p.Codigo,
                p.Escanios,
                p.EscaniosDesdeSondeo,
                p.EscaniosHastaSondeo,
                p.PorcentajeVoto,
                p.Votos,
                p.EsUltimoEscano,
                p.LuchaUltimoEscano
            })
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
