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
        public Task<string> WriteAsync(BrainStormSnapshot snapshot, string fileNameWithoutExtension, CancellationToken ct = default, GraphicModule? module = null)
            => Task.FromResult($"{fileNameWithoutExtension}.csv");

        public Task<string?> WriteSedesAsync(BrainStormSnapshot snapshot, string partyCodeOrSiglas, CancellationToken ct = default, GraphicModule? module = null)
            => Task.FromResult<string?>("Sedes.csv");

        public Task<string?> WriteRecuentosJsonAsync(BrainStormSnapshot snapshot, CancellationToken ct = default, GraphicModule? module = null)
            => Task.FromResult<string?>("Recuentos.json");
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
