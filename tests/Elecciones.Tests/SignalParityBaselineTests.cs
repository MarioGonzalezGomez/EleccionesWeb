using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class SignalParityBaselineTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pactos_Faldon_EntraIzquierdaYDerecha_UsesOrderedFullPartyLists(bool oficiales)
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var leftSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = oficiales,
            Command = GraphicCommand.PactosEntraIzquierda,
            PartyCodes = ["00002", "00001"]
        }, snapshot);

        Assert.Contains("itemset('<{BD}>SuperfaldonPactometro/Izquierda','MAP_LSTRING_PAR',[\"00002\", \"00001\"]);", leftSignal);
        Assert.Contains("itemset('<{BD}>PACTOMETRO/ENTRA_IZQUIERDA','EVENT_RUN');", leftSignal);

        var rightSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = oficiales,
            Command = GraphicCommand.PactosEntraDerecha,
            PartyCodes = ["00001", "00002"]
        }, snapshot);

        Assert.Contains("itemset('<{BD}>SuperfaldonPactometro/Derecha','MAP_LSTRING_PAR',[\"00001\", \"00002\"]);", rightSignal);
        Assert.Contains("itemset('<{BD}>PACTOMETRO/ENTRA_DERECHA','EVENT_RUN');", rightSignal);
    }

    [Fact]
    public void Pactos_Faldon_EntraAmbosActualizaLosDosBloquesAntesDeLanzarlos()
    {
        var composer = new DefaultSignalComposer();
        var signal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosEntraAmbos,
            LeftPartyCodes = ["00002", "00001"],
            RightPartyCodes = ["00003"]
        }, TestSnapshotFactory.CreateSampleSnapshot());

        var leftListIndex = signal.IndexOf("SuperfaldonPactometro/Izquierda", StringComparison.Ordinal);
        var rightListIndex = signal.IndexOf("SuperfaldonPactometro/Derecha", StringComparison.Ordinal);
        var leftEnterIndex = signal.IndexOf("PACTOMETRO/ENTRA_IZQUIERDA", StringComparison.Ordinal);
        var rightEnterIndex = signal.IndexOf("PACTOMETRO/ENTRA_DERECHA", StringComparison.Ordinal);

        Assert.True(leftListIndex >= 0);
        Assert.True(rightListIndex > leftListIndex);
        Assert.True(leftEnterIndex > rightListIndex);
        Assert.True(rightEnterIndex > leftEnterIndex);
        Assert.Contains("[\"00002\", \"00001\"]", signal);
        Assert.Contains("[\"00003\"]", signal);
    }

    [Theory]
    [InlineData(OperationActionType.Enter, "PACTOMETRO/ENTRA")]
    [InlineData(OperationActionType.Update, "ACTUALIZAR")]
    [InlineData(OperationActionType.Exit, "PACTOMETRO/SALE")]
    public void Pactometro_Faldon_UsesExpectedLifecycleEvents(OperationActionType action, string eventName)
    {
        var composer = new DefaultSignalComposer();
        var signal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "Pactometro",
            Action = action,
            Oficiales = true
        }, TestSnapshotFactory.CreateSampleSnapshot());

        Assert.Contains($"itemset('<{{BD}}>{eventName}','EVENT_RUN');", signal);
        Assert.DoesNotContain("META/", signal);
    }

    [Fact]
    public void Pactometro_Faldon_SaleLateralClearsThatSide()
    {
        var composer = new DefaultSignalComposer();
        var signal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PactosSaleIzquierda,
            Oficiales = true
        }, TestSnapshotFactory.CreateSampleSnapshot());

        Assert.Contains("itemset('<{BD}>PACTOMETRO/SALE_IZQUIERDA','EVENT_RUN');", signal);
        Assert.DoesNotContain("reinicioPactometro", signal);
    }

    [Fact]
    public void UltimoEscano_Faldon_MapsOfficialValuesAndLifecycle()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot();
        var request = new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "SUPERFALDON_ULTIMOESCANO",
            Action = OperationActionType.Enter,
            Oficiales = true
        };

        var enterSignal = composer.Compose(request, snapshot);

        Assert.Contains("itemset('<{BD}>SuperfaldonUltimoEscano/Ganador','MAP_STRING_PAR','00001');", enterSignal);
        Assert.Contains("itemset('<{BD}>SuperfaldonUltimoEscano/Segundo','MAP_STRING_PAR','00002');", enterSignal);
        Assert.Contains("itemset('<{BD}>SuperfaldonUltimoEscano/Provincia','MAP_STRING_PAR','ESPANA');", enterSignal);
        Assert.Contains("itemset('<{BD}>SuperfaldonUltimoEscano/NewPendientes','MAP_INT_PAR',3100200);", enterSignal);
        Assert.Contains("itemset('<{BD}>SuperfaldonUltimoEscano/NewVotos','MAP_INT_PAR',1420);", enterSignal);
        Assert.Contains("itemset('<{BD}>SUPERFALDON_ULTIMOESCANO/ENTRA','EVENT_RUN');", enterSignal);

        var updateSignal = composer.Compose(new OperationRequest
        {
            OperatorId = request.OperatorId,
            Module = request.Module,
            Scene = request.Scene,
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot);
        Assert.Contains("itemset('<{BD}>SUPERFALDON_ULTIMOESCANO/ACTUALIZA','EVENT_RUN');", updateSignal);

        var exitSignal = composer.Compose(new OperationRequest
        {
            OperatorId = request.OperatorId,
            Module = request.Module,
            Scene = request.Scene,
            Action = OperationActionType.Exit,
            Oficiales = false
        }, snapshot);
        Assert.Contains("itemset('<{BD}>SUPERFALDON_ULTIMOESCANO/SALE','EVENT_RUN');", exitSignal);

        var sondeoSignal = composer.Compose(new OperationRequest
        {
            OperatorId = request.OperatorId,
            Module = request.Module,
            Scene = request.Scene,
            Action = OperationActionType.Enter,
            Oficiales = false
        }, snapshot);
        Assert.Empty(sondeoSignal);
    }

    [Fact]
    public void PrimeDirectCommands_SubirYBajarRotulos_FormatMatchesPrimeProtocol()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var subirTd = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PrimeSubirRotulosTD,
            Target = GraphicsTarget.Prime
        }, snapshot);

        Assert.Equal("P\\CHANGE_PROJECT\\01_TD_2026\\\\\r\nP\\PROJECT_PARAMETER\\TickerElec\\True\\\\\r\n", subirTd);

        var subirEsp = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Command = GraphicCommand.PrimeSubirRotulosEspeciales,
            Target = GraphicsTarget.Prime
        }, snapshot);

        Assert.Equal("P\\CHANGE_PROJECT\\Especiales_ConPicto_2026\\\\\r\nP\\PROJECT_PARAMETER\\Par_Posicion_GrupoXY\\2\\\\\r\n", subirEsp);
    }

    [Theory]
    [InlineData(GraphicModule.Faldon, GraphicCommand.IdiomaCastellano, "itemset('<{BD}>IDIOMA/CASTELLANO','EVENT_RUN');")]
    [InlineData(GraphicModule.Faldon, GraphicCommand.IdiomaEuskera, "itemset('<{BD}>IDIOMA/EUSKERA','EVENT_RUN');")]
    [InlineData(GraphicModule.Faldon, GraphicCommand.IdiomaCatalan, "itemset('<{BD}>IDIOMA/CATALAN','EVENT_RUN');")]
    [InlineData(GraphicModule.Faldon, GraphicCommand.IdiomaGallego, "itemset('<{BD}>IDIOMA/GALEGO','EVENT_RUN');")]
    [InlineData(GraphicModule.Faldon, GraphicCommand.IdiomaValenciano, "itemset('<{BD}>IDIOMA/VALENCIANO','EVENT_RUN');")]
    [InlineData(GraphicModule.Carton, GraphicCommand.IdiomaCastellano, "itemset('<{BD}>IDIOMA/CASTELLANO','EVENT_RUN');")]
    [InlineData(GraphicModule.Carton, GraphicCommand.IdiomaEuskera, "itemset('<{BD}>IDIOMA/EUSKERA','EVENT_RUN');")]
    [InlineData(GraphicModule.Carton, GraphicCommand.IdiomaCatalan, "itemset('<{BD}>IDIOMA/CATALAN','EVENT_RUN');")]
    [InlineData(GraphicModule.Carton, GraphicCommand.IdiomaGallego, "itemset('<{BD}>IDIOMA/GALEGO','EVENT_RUN');")]
    [InlineData(GraphicModule.Carton, GraphicCommand.IdiomaValenciano, "itemset('<{BD}>IDIOMA/VALENCIANO','EVENT_RUN');")]
    public void GraphicLanguageCommands_EmitExactUppercaseUnaccentedEventRunOnBothModules(
        GraphicModule module,
        GraphicCommand command,
        string expectedSignal)
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var signal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = module,
            Command = command,
            Oficiales = true
        }, snapshot);

        Assert.Equal(expectedSignal, signal);
    }
}

