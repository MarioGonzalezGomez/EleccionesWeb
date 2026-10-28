using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class SignalParityBaselineTests
{
    [Fact]
    public void Pactos_Oficiales_EntraIzquierdaYDerecha_GeneratesExpectedCommands()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var leftSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.PactosEntraIzquierda,
            CommandValue = "00002"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Pactometro_IzqVALOR','MAP_INT_PAR',121);", leftSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Pactometro/Izq/LogosIzq/Logo01','OBJ_OVERMAT','Logos/Logo02');", leftSignal);
        Assert.Contains("itemset('<{BD}>Pactometro/lanzaPactometroIzq','EVENT_RUN');", leftSignal);

        var rightSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.PactosEntraDerecha,
            CommandValue = "00001"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Pactometro_DerVALOR','MAP_INT_PAR',137);", rightSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Pactometro/Der/LogosDer/Logo01','OBJ_OVERMAT','Logos/Logo01');", rightSignal);
        Assert.Contains("itemset('<{BD}>Pactometro/lanzaPactometroDer','EVENT_RUN');", rightSignal);
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
}

