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
    private readonly IModuleLockService? _lockService;
    private readonly Dictionary<GraphicModule, (string ContextKey, string DataHash)> _lastHashes = new();

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public DatabaseChangeListenerService(
        IOperationStateService stateService,
        IEleccionesDataService dataService,
        IBrainStormCsvWriter csvWriter,
        ISignalComposer signalComposer,
        IGraphicsGateway graphicsGateway,
        ILogger<DatabaseChangeListenerService> logger,
        IGlobalOperationalSettingsService? globalSettings = null,
        IModuleLockService? lockService = null)
    {
        _stateService = stateService;
        _dataService = dataService;
        _csvWriter = csvWriter;
        _signalComposer = signalComposer;
        _graphicsGateway = graphicsGateway;
        _logger = logger;
        _globalSettings = globalSettings;
        _lockService = lockService;
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
        var csvUpdatedOffAirModules = new List<GraphicModule>();
        var signalDispatchedModules = new List<GraphicModule>();

        var states = _stateService.GetAllStates();
        var lockStates = _lockService?.GetStates() ?? [];
        var isCartonOperatorActive = lockStates.Any(l => l.Module == GraphicModule.Carton && l.IsLocked);
        var globalSnapshot = _globalSettings?.GetSettings();
        var checkedAtUtc = DateTimeOffset.UtcNow;

        var monitoredModules = states
            .Where(s => ShouldMonitorModule(s, isCartonOperatorActive))
            .ToList();

        if (monitoredModules.Count == 0)
        {
            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, "Sin módulos en aire ni operador de cartones activo.");
            return 0;
        }

        foreach (var state in monitoredModules)
        {
            try
            {
                var query = state.IsOnAir
                    ? (state.ActiveQuery ?? BuildFallbackQuery(state, globalSnapshot))
                    : (state.PreviewQuery ?? state.ActiveQuery ?? BuildFallbackQuery(state, globalSnapshot));
                var contextKey = $"{query.ElectionSlot}|{state.Oficiales}|{state.CartonKind}|{state.ActiveScene}|{query.Kind}|{query.CircunscripcionCodigo}|{query.AutonomiaCodigo}|{query.PartidoCodigo}|{query.MedioCodigo}";
                var snapshot = await _dataService.GetSnapshotAsync(query, state.Oficiales, cancellationToken);
                var hash = ComputeSnapshotHash(snapshot);

                if (!_lastHashes.TryGetValue(state.Module, out var previous)
                    || !string.Equals(previous.ContextKey, contextKey, StringComparison.Ordinal))
                {
                    _lastHashes[state.Module] = (contextKey, hash);

                    // Si hay operador de Cartones activo en un contexto nuevo, garantizamos los 5 ficheros CSV iniciales sincronizados
                    if (state.Module == GraphicModule.Carton && isCartonOperatorActive && !state.IsOnAir)
                    {
                        await _csvWriter.WriteAllCartonFilesAsync(snapshot, query, cancellationToken, state.Module);
                    }

                    continue;
                }

                if (string.Equals(previous.DataHash, hash, StringComparison.Ordinal))
                {
                    continue;
                }

                _lastHashes[state.Module] = (contextKey, hash);

                var exportName = !string.IsNullOrWhiteSpace(state.ExportName)
                    ? state.ExportName
                    : (state.Oficiales ? "Oficial_Datos" : "Sondeo_Datos");

                var resolvedCartonKind = state.Module == GraphicModule.Carton
                    ? (state.CartonKind ?? CartonKindResolver.ResolveFromScene(state.ActiveScene))
                    : (CartonKind?)null;

                if (state.Module == GraphicModule.Carton)
                {
                    await _csvWriter.WriteAllCartonFilesAsync(snapshot, query, cancellationToken, state.Module);
                }
                else
                {
                    await _csvWriter.WriteAsync(snapshot, exportName, cancellationToken, state.Module);
                    await _csvWriter.WriteRecuentosJsonAsync(snapshot, cancellationToken, state.Module);
                }

                if (!state.IsOnAir)
                {
                    csvUpdatedOffAirModules.Add(state.Module);
                    continue;
                }

                var updateRequest = new OperationRequest
                {
                    OperatorId = string.IsNullOrWhiteSpace(state.LastOperatorId) ? "system-listener" : state.LastOperatorId,
                    Module = state.Module,
                    CartonKind = resolvedCartonKind,
                    Action = OperationActionType.Update,
                    Scene = state.ActiveScene,
                    Oficiales = state.Oficiales,
                    ExportName = exportName,
                    Target = state.Target,
                    Query = query
                };

                var signal = _signalComposer.Compose(updateRequest, snapshot);
                if (!string.IsNullOrWhiteSpace(signal))
                {
                    await _graphicsGateway.SendAsync(state.Target, signal, cancellationToken, state.Module);
                    dispatchedCount++;
                    signalDispatchedModules.Add(state.Module);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error in DatabaseChangeListenerService while polling module {Module}", state.Module);
                _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, $"Error en {state.Module}: {ex.Message}");
            }
        }

        if (dispatchedCount > 0 || csvUpdatedOffAirModules.Count > 0)
        {
            var statusParts = new List<string>();
            if (signalDispatchedModules.Count > 0)
            {
                statusParts.Add($"Señal + CSV en aire: {string.Join(", ", signalDispatchedModules)}");
            }

            if (csvUpdatedOffAirModules.Count > 0)
            {
                statusParts.Add($"CSV sincronizados: {string.Join(", ", csvUpdatedOffAirModules)}");
            }

            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, DateTimeOffset.UtcNow, string.Join(" · ", statusParts));
        }
        else
        {
            _globalSettings?.RecordAutoRefreshCheck(checkedAtUtc, null, "Sin cambios de datos.");
        }

        return dispatchedCount;
    }

    private static bool ShouldMonitorModule(ModuleOperationalState state, bool isCartonOperatorActive)
    {
        if (state.IsOnAir && state.ActiveQuery is not null)
        {
            return true;
        }

        if (state.Module == GraphicModule.Carton && isCartonOperatorActive)
        {
            return true;
        }

        return false;
    }

    private static SnapshotQuery BuildFallbackQuery(ModuleOperationalState state, GlobalOperationalSettings? global)
    {
        return new SnapshotQuery
        {
            ElectionSlot = state.ElectionSlot,
            ConnectionType = global?.ConnectionType ?? 0,
            Kind = SnapshotQueryKind.Circunscripcion,
            CircunscripcionCodigo = "9900000",
            Avance = global?.Avance is > 0 ? global.Avance : null,
            IncludeZeroSeats = global?.IncludeZeroSeats ?? false,
            MedioCodigo = "RTVE"
        };
    }

    private static string ComputeSnapshotHash(BrainStormSnapshot snapshot)
    {
        var sourceParties = snapshot.PartidosCircunscripcionCompletos is { Count: > 0 }
            ? snapshot.PartidosCircunscripcionCompletos
            : snapshot.Partidos;

        var payload = JsonSerializer.Serialize(new
        {
            snapshot.Circunscripcion.Codigo,
            snapshot.Escrutado,
            snapshot.EscrutadoGeneral,
            snapshot.Avance,
            snapshot.Avance1,
            snapshot.Avance2,
            snapshot.Avance3,
            snapshot.Participacion,
            snapshot.ParticipacionHistorica,
            snapshot.ParticipacionMedia,
            snapshot.VotantesFaltan,
            PartidosEleccion = snapshot.PartidosEleccion.Select(p => new { p.Codigo, p.Siglas }),
            Partidos = sourceParties.Select(p => new
            {
                p.Codigo,
                p.Escanios,
                p.EscaniosDesdeSondeo,
                p.EscaniosHastaSondeo,
                p.PorcentajeVoto,
                p.Votos,
                p.EsUltimoEscano,
                p.LuchaUltimoEscano,
                p.RestoVotos
            }),
            Mayorias = snapshot.MayoriasCircunscripciones.Select(m => new
            {
                m.CodigoLugar,
                m.CodigoPartidoGanador,
                m.EscaniosCircunscripcion,
                m.Escrutado,
                m.DiferenciaEscaniosGanador
            }),
            SondeoMayorias = snapshot.SondeoMayoriasCircunscripciones.Select(m => new
            {
                m.CodigoLugar,
                m.CodigoPartidoGanador,
                m.EscaniosCircunscripcion,
                m.DiferenciaEscaniosGanador
            })
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
