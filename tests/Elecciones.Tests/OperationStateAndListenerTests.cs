using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Infrastructure.Services;
using Elecciones.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elecciones.Tests;

public sealed class OperationStateAndListenerTests
{
    [Fact]
    public void OperationStateService_TracksOnAirSceneVideosSedesAndNotifiesSubscribers()
    {
        var stateService = new OperationStateService();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);
        ModuleOperationalState? notifiedState = null;
        stateService.OnStateChanged += (_, s) => notifiedState = s;

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        var current = stateService.GetState(GraphicModule.Faldon);
        Assert.True(current.IsOnAir);
        Assert.Equal("TICKER", current.ActiveScene);
        Assert.NotNull(notifiedState);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.VideoIn,
            CommandValue = "00001",
            Oficiales = true
        }, snapshot);

        Assert.Contains("00001", stateService.GetState(GraphicModule.Faldon).ExpandedVideos);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, snapshot);

        var afterExit = stateService.GetState(GraphicModule.Faldon);
        Assert.False(afterExit.IsOnAir);
        Assert.Empty(afterExit.ExpandedVideos);
    }

    [Fact]
    public void ValencianoLanguageCommand_UpdatesTheModuleLanguageState()
    {
        var stateService = new OperationStateService();

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.IdiomaValenciano,
            Oficiales = true
        }, TestSnapshotFactory.CreateSampleSnapshot(oficiales: true));

        Assert.Equal(GraphicLanguage.Valenciano, stateService.GetState(GraphicModule.Faldon).GraphicLanguage);
    }

    [Fact]
    public void FaldonPactometroState_ReplacesListsInSelectionOrderAndClearsOnSideSale()
    {
        var stateService = new OperationStateService();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot();

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosEntraIzquierda,
            PartyCodes = ["00002", "00001"],
            Oficiales = true
        }, snapshot);

        Assert.Equal(new[] { "00002", "00001" }, stateService.GetState(GraphicModule.Faldon).PactosIzquierda);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosSaleIzquierda,
            Oficiales = true
        }, snapshot);

        Assert.Empty(stateService.GetState(GraphicModule.Faldon).PactosIzquierda);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosEntraDerecha,
            PartyCodes = ["00003"],
            Oficiales = true
        }, snapshot);
        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Scene = "Pactometro",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Scene = "Pactometro",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, snapshot);

        var afterChartExit = stateService.GetState(GraphicModule.Faldon);
        Assert.Empty(afterChartExit.PactosIzquierda);
        Assert.Empty(afterChartExit.PactosDerecha);
    }

    [Fact]
    public void FaldonPactometroCombinedEntry_StoresBothPartyLists()
    {
        var stateService = new OperationStateService();
        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op-faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosEntraAmbos,
            LeftPartyCodes = ["00002", "00001"],
            RightPartyCodes = ["00003"],
            Oficiales = true
        }, TestSnapshotFactory.CreateSampleSnapshot());

        var state = stateService.GetState(GraphicModule.Faldon);
        Assert.Equal(new[] { "00002", "00001" }, state.PactosIzquierda);
        Assert.Equal(new[] { "00003" }, state.PactosDerecha);
    }

    [Fact]
    public async Task DatabaseChangeListenerService_DetectsDataChangeAndDispatchesAutoUpdateWhenOnAir()
    {
        var stateService = new OperationStateService();
        var snapshot1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);
        var fakeData = new MutableFakeDataService(snapshot1);
        var fakeCsv = new FakeCsvWriter();
        var fakeGateway = new FakeGraphicsGateway();
        var composer = new DefaultSignalComposer();

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Enter,
            Oficiales = true,
            Query = new SnapshotQuery
            {
                Kind = SnapshotQueryKind.Circunscripcion,
                CircunscripcionCodigo = "9900000",
                Avance = 1
            }
        }, snapshot1);

        var listener = new DatabaseChangeListenerService(
            stateService,
            fakeData,
            fakeCsv,
            composer,
            fakeGateway,
            NullLogger<DatabaseChangeListenerService>.Instance);

        // 1er poll: registra hash base sin disparar actualización
        var firstCount = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(0, firstCount);

        // 2º poll sin cambios: no dispara actualización
        var secondCount = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(0, secondCount);

        // 3er poll con cambio de escrutado: regenera CSV y envía señal Update
        fakeData.CurrentSnapshot = snapshot1 with { Escrutado = 91.20 };
        var thirdCount = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(1, thirdCount);
        Assert.Single(fakeGateway.SentSignals);
        Assert.Contains("Escrutinio/Actualiza", fakeGateway.SentSignals[0]);
    }

    [Fact]
    public async Task DatabaseChangeListener_PactometroSendsItsRefreshEventWhenOnAir()
    {
        var stateService = new OperationStateService();
        var initialSnapshot = TestSnapshotFactory.CreateSampleSnapshot();
        var fakeData = new MutableFakeDataService(initialSnapshot);
        var fakeGateway = new FakeGraphicsGateway();

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "Pactometro",
            Action = OperationActionType.Enter,
            Oficiales = true,
            Query = new SnapshotQuery
            {
                Kind = SnapshotQueryKind.Circunscripcion,
                CircunscripcionCodigo = "9900000",
                Avance = 1
            }
        }, initialSnapshot);

        var listener = new DatabaseChangeListenerService(
            stateService,
            fakeData,
            new FakeCsvWriter(),
            new DefaultSignalComposer(),
            fakeGateway,
            NullLogger<DatabaseChangeListenerService>.Instance);

        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());
        fakeData.CurrentSnapshot = initialSnapshot with { Escrutado = 91.20 };

        Assert.Equal(1, await listener.CheckAndDispatchUpdatesOnceAsync());
        Assert.Contains("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", Assert.Single(fakeGateway.SentSignals));
    }

    [Fact]
    public async Task DatabaseChangeListener_UltimoEscanoDebouncesSeveralChangesIntoOneRefresh()
    {
        var stateService = new OperationStateService();
        var initialSnapshot = TestSnapshotFactory.CreateSampleSnapshot();
        var fakeData = new MutableFakeDataService(initialSnapshot);
        var fakeGateway = new FakeGraphicsGateway();

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "SUPERFALDON_ULTIMOESCANO",
            Action = OperationActionType.Enter,
            Oficiales = true,
            Query = new SnapshotQuery
            {
                Kind = SnapshotQueryKind.Circunscripcion,
                CircunscripcionCodigo = "9900000",
                Avance = 1
            }
        }, initialSnapshot);

        var listener = new DatabaseChangeListenerService(
            stateService,
            fakeData,
            new FakeCsvWriter(),
            new DefaultSignalComposer(),
            fakeGateway,
            NullLogger<DatabaseChangeListenerService>.Instance)
        {
            UltimoEscanoUpdateDebounceInterval = TimeSpan.FromMilliseconds(30)
        };

        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());

        fakeData.CurrentSnapshot = initialSnapshot with { Escrutado = 91.20 };
        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());
        Assert.Empty(fakeGateway.SentSignals);

        fakeData.CurrentSnapshot = initialSnapshot with { Escrutado = 92.40 };
        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());
        Assert.Empty(fakeGateway.SentSignals);

        await Task.Delay(listener.UltimoEscanoUpdateDebounceInterval + TimeSpan.FromMilliseconds(20));
        Assert.Equal(1, await listener.CheckAndDispatchUpdatesOnceAsync());

        Assert.Contains(
            "itemset('<{BD}>SUPERFALDON_ULTIMOESCANO/ACTUALIZA','EVENT_RUN');",
            Assert.Single(fakeGateway.SentSignals));
        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());
    }

    [Fact]
    public async Task SimultaneousModules_WithDifferentElectionSlotsAndModes_AreTrackedAndPolledIndependently()
    {
        var stateService = new OperationStateService(defaultElectionSlot: 1, defaultOficiales: false);

        // Configurar Faldón en Sondeo + Elección 1 y Cartón en Oficiales + Elección 2
        var faldonCfg = stateService.ConfigureModule(GraphicModule.Faldon, electionSlot: 1, oficiales: false);
        var cartonCfg = stateService.ConfigureModule(GraphicModule.Carton, electionSlot: 2, oficiales: true);

        Assert.Equal(1, faldonCfg.ElectionSlot);
        Assert.False(faldonCfg.Oficiales);
        Assert.Equal(2, cartonCfg.ElectionSlot);
        Assert.True(cartonCfg.Oficiales);

        // Toggles de estado en Faldón
        var snapshotSondeoE1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: false) with { Escrutado = 0.0 };
        var snapshotOficialE2 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with { Escrutado = 52.4 };

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PrimerosResultadosDesactiva,
            Oficiales = false,
            Query = new SnapshotQuery { ElectionSlot = 1, CircunscripcionCodigo = "9900000" }
        }, snapshotSondeoE1);
        Assert.False(stateService.GetState(GraphicModule.Faldon).PrimerosResultadosEnabled);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.AnimacionSondeoDesactiva,
            Oficiales = false,
            Query = new SnapshotQuery { ElectionSlot = 1, CircunscripcionCodigo = "9900000" }
        }, snapshotSondeoE1);
        Assert.False(stateService.GetState(GraphicModule.Faldon).AnimacionSondeoEnabled);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_faldon",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.CambioEleccionesEuropa,
            Oficiales = false,
            Query = new SnapshotQuery { ElectionSlot = 1, CircunscripcionCodigo = "9900000" }
        }, snapshotSondeoE1);
        Assert.True(stateService.GetState(GraphicModule.Faldon).AmbitoEuropaEnabled);

        // Poner ambos en aire simultáneamente con modos y elecciones distintos
        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_faldon",
            Module = GraphicModule.Faldon,
            Scene = "Sondeo",
            Action = OperationActionType.Enter,
            Oficiales = false,
            ExportName = "BrainStorm_Faldon",
            Query = new SnapshotQuery { ElectionSlot = 1, CircunscripcionCodigo = "9900000" }
        }, snapshotSondeoE1);

        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Action = OperationActionType.Enter,
            Oficiales = true,
            ExportName = "BrainStorm_Carton",
            Query = new SnapshotQuery { ElectionSlot = 2, CircunscripcionCodigo = "9900000" }
        }, snapshotOficialE2);

        var multiData = new SlotAwareFakeDataService(snapshotSondeoE1, snapshotOficialE2);
        var fakeCsv = new FakeCsvWriter();
        var fakeGateway = new FakeGraphicsGateway();
        var composer = new DefaultSignalComposer();

        var listener = new DatabaseChangeListenerService(
            stateService,
            multiData,
            fakeCsv,
            composer,
            fakeGateway,
            NullLogger<DatabaseChangeListenerService>.Instance);

        // Poll inicial establece hashes base de ambos módulos
        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());

        // Solo cambia Elección 2 (Oficiales en Cartón); Elección 1 (Sondeo en Faldón) no cambia
        multiData.SnapshotSlot2Oficial = snapshotOficialE2 with { Escrutado = 64.8 };
        var updatedCount = await listener.CheckAndDispatchUpdatesOnceAsync();

        Assert.Equal(1, updatedCount);
        Assert.Single(fakeGateway.SentSignals);
        Assert.Equal("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", fakeGateway.SentSignals[0]);
    }

    [Fact]
    public async Task CartonOperator_UpdatesAllCartonCsvFilesOffAirWithoutIpfSignal_AndSendsIpfSignalWhenOnAir()
    {
        var stateService = new OperationStateService(defaultElectionSlot: 1, defaultOficiales: true);
        var lockService = new ModuleLockService(new FakeLockStore());
        Assert.True(lockService.TryAcquire(GraphicModule.Carton, "op_carton"));

        var query = new SnapshotQuery
        {
            ElectionSlot = 1,
            Kind = SnapshotQueryKind.Circunscripcion,
            CircunscripcionCodigo = "9900000"
        };
        stateService.SyncModuleSelection(
            GraphicModule.Carton,
            "op_carton",
            CartonKind.Mayorias,
            query,
            oficiales: true,
            GraphicsTarget.Ipf,
            "BrainStorm");

        var snapshot1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);
        var fakeData = new MutableFakeDataService(snapshot1);
        var fakeCsv = new FakeCsvWriter();
        var fakeGateway = new FakeGraphicsGateway();
        var composer = new DefaultSignalComposer();

        var listener = new DatabaseChangeListenerService(
            stateService,
            fakeData,
            fakeCsv,
            composer,
            fakeGateway,
            NullLogger<DatabaseChangeListenerService>.Instance,
            lockService: lockService);

        // 1er poll con operador activo y FUERA DE AIRE: sincroniza CSV iniciales sin enviar señal IPF
        var initialSignals = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(0, initialSignals);
        Assert.Equal(1, fakeCsv.AllCartonSyncCalls);
        Assert.Empty(fakeGateway.SentSignals);

        // 2º poll con cambio en BD y FUERA DE AIRE: vuelve a escribir todos los CSV pero NO envía señal IPF
        fakeData.CurrentSnapshot = snapshot1 with { Escrutado = 90.15 };
        var offAirSignals = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(0, offAirSignals);
        Assert.Equal(2, fakeCsv.AllCartonSyncCalls);
        Assert.Empty(fakeGateway.SentSignals);

        // Ponemos un Cartón EN AIRE
        stateService.RecordExecution(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Action = OperationActionType.Enter,
            Oficiales = true,
            Target = GraphicsTarget.Ipf,
            Query = query
        }, fakeData.CurrentSnapshot);

        // 1er poll tras cambio de escena a ESTATAL en aire fija hash base de ese contexto
        Assert.Equal(0, await listener.CheckAndDispatchUpdatesOnceAsync());

        // Nuevo cambio en BD con Cartón EN AIRE: actualiza todos los CSV Y envía señal ACTUALIZAR al IPF (sin escribir JSON)
        fakeData.CurrentSnapshot = fakeData.CurrentSnapshot with { Escrutado = 94.80 };
        var onAirSignals = await listener.CheckAndDispatchUpdatesOnceAsync();
        Assert.Equal(1, onAirSignals);
        Assert.Equal(3, fakeCsv.AllCartonSyncCalls);
        Assert.Equal(0, fakeCsv.RecuentosJsonCalls);
        Assert.Single(fakeGateway.SentSignals);
        Assert.Equal("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", fakeGateway.SentSignals[0]);

        // Si mientras está EN AIRE (9900000) el operador previsualiza otra circunscripción (2800000) sin pulsar ENTRA/ACTUALIZA,
        // un nuevo cambio en BD mantiene la query de emisión (9900000) sin mutar a 2800000.
        stateService.SyncModuleSelection(
            GraphicModule.Carton,
            "op_carton",
            CartonKind.Participacion,
            new SnapshotQuery { ElectionSlot = 1, CircunscripcionCodigo = "2800000" },
            oficiales: false,
            GraphicsTarget.Ipf,
            "BrainStorm");

        Assert.Equal("9900000", stateService.GetState(GraphicModule.Carton).ActiveQuery!.CircunscripcionCodigo);
        Assert.Equal("2800000", stateService.GetState(GraphicModule.Carton).PreviewQuery!.CircunscripcionCodigo);
        Assert.True(stateService.GetState(GraphicModule.Carton).Oficiales);
    }

    private sealed class FakeLockStore : IModuleLockStateStore
    {
        private IReadOnlyList<ModuleLockInfo> _states = [];
        public IReadOnlyList<ModuleLockInfo> Load() => _states;
        public void Save(IReadOnlyList<ModuleLockInfo> states) => _states = states;
    }

    private sealed class SlotAwareFakeDataService(BrainStormSnapshot slot1Sondeo, BrainStormSnapshot slot2Oficial) : IEleccionesDataService
    {
        public BrainStormSnapshot SnapshotSlot1Sondeo { get; set; } = slot1Sondeo;
        public BrainStormSnapshot SnapshotSlot2Oficial { get; set; } = slot2Oficial;

        public Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(int electionSlot = 1, int connectionType = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CircunscripcionSummary>>([new("9900000", "España")]);

        public Task<IReadOnlyList<MedioSummary>> GetMediosAsync(int electionSlot = 1, int connectionType = 0, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MedioSummary>>([]);

        public Task<BrainStormSnapshot> GetSnapshotAsync(SnapshotQuery query, bool oficiales, CancellationToken cancellationToken = default)
        {
            if (query.ElectionSlot == 2 && oficiales)
            {
                return Task.FromResult(SnapshotSlot2Oficial);
            }

            return Task.FromResult(SnapshotSlot1Sondeo);
        }
    }

    private sealed class MutableFakeDataService(BrainStormSnapshot initial) : IEleccionesDataService
    {
        public BrainStormSnapshot CurrentSnapshot { get; set; } = initial;

        public Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(
            int electionSlot = 1,
            int connectionType = 0,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CircunscripcionSummary>>([CurrentSnapshot.Circunscripcion]);

        public Task<IReadOnlyList<MedioSummary>> GetMediosAsync(
            int electionSlot = 1,
            int connectionType = 0,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MedioSummary>>([]);

        public Task<BrainStormSnapshot> GetSnapshotAsync(
            SnapshotQuery query,
            bool oficiales,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentSnapshot);
    }

    private sealed class FakeCsvWriter : IBrainStormCsvWriter
    {
        public List<CartonKind> WrittenCartonKinds { get; } = [];
        public int AllCartonSyncCalls { get; private set; }
        public int RecuentosJsonCalls { get; private set; }

        public Task<string> WriteAsync(BrainStormSnapshot snapshot, string fileNameWithoutExtension, CancellationToken ct = default, GraphicModule? module = null)
            => Task.FromResult($"{fileNameWithoutExtension}.csv");

        public Task<CartonCsvExportResult> WriteCartonAsync(CartonKind cartonKind, BrainStormSnapshot snapshot, SnapshotQuery query, CancellationToken ct = default, GraphicModule? module = GraphicModule.Carton)
        {
            WrittenCartonKinds.Add(cartonKind);
            var files = cartonKind is CartonKind.Mayorias or CartonKind.Estatal
                ? new List<CartonCsvFileArtifact>
                {
                    new("Partidos", $"{cartonKind}_Partidos.csv", $"{cartonKind}_Partidos.csv"),
                    new("Provincia", $"{cartonKind}_Provincia.csv", $"{cartonKind}_Provincia.csv")
                }
                : new List<CartonCsvFileArtifact>
                {
                    new("Unico", $"{cartonKind}.csv", $"{cartonKind}.csv")
                };

            return Task.FromResult(new CartonCsvExportResult(cartonKind, files));
        }

        public Task<IReadOnlyList<CartonCsvFileArtifact>> WriteAllCartonFilesAsync(BrainStormSnapshot snapshot, SnapshotQuery query, CancellationToken ct = default, GraphicModule? module = GraphicModule.Carton)
        {
            AllCartonSyncCalls++;
            return Task.FromResult<IReadOnlyList<CartonCsvFileArtifact>>(
            [
                new("Partidos", "datos_partidos.csv", "datos_partidos.csv"),
                new("Provincia", "mayorias_provincias.csv", "mayorias_provincias.csv"),
                new("Mapa", "mapa_escanos.csv", "mapa_escanos.csv")
            ]);
        }

        public Task<string?> WriteSedesAsync(BrainStormSnapshot snapshot, string partyCodeOrSiglas, CancellationToken ct = default, GraphicModule? module = null)
            => Task.FromResult<string?>("Sedes.csv");

        public Task<string?> WriteRecuentosJsonAsync(BrainStormSnapshot snapshot, CancellationToken ct = default, GraphicModule? module = null)
        {
            if (module == GraphicModule.Carton)
            {
                RecuentosJsonCalls++;
            }
            return Task.FromResult<string?>("Recuentos.json");
        }
    }

    private sealed class FakeGraphicsGateway : IGraphicsGateway
    {
        public List<string> SentSignals { get; } = [];

        public Task<IReadOnlyList<string>> SendAsync(GraphicsTarget target, string signal, CancellationToken ct = default, GraphicModule? module = null)
        {
            SentSignals.Add(signal);
            return Task.FromResult<IReadOnlyList<string>>(["IPF:Faldon"]);
        }
    }
}
