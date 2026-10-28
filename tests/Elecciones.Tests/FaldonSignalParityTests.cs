using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class FaldonSignalParityTests
{
    [Fact]
    public void Ticker_Entra_Update_And_Exit_GeneratesDynamicLayoutAndRecolocaNoActivos()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        // 4 partidos activos (PP=01, PSOE=02, VOX=03, SUMAR=04) y 1 no activo (PODEMOS=05)
        // tamanoFicha = (1748 - 10 * 3) / 4 = 429.5
        var enterSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>PrimerosResultados/Entra','EVENT_RUN');", enterSignal);
        Assert.Contains("itemset('<{BD}>fichaPartido','PRIM_RECGLO_LEN[0]',429.5);", enterSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Escrutinio/partidos/partido01','OBJ_DISPLACEMENT[0]',0);", enterSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Escrutinio/partidos/partido02','OBJ_DISPLACEMENT[0]',439.5);", enterSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Escrutinio/partidos/partido03','OBJ_DISPLACEMENT[0]',879);", enterSignal);
        Assert.Contains("itemset('<{BD}>Graficos/Escrutinio/partidos/partido04','OBJ_DISPLACEMENT[0]',1318.5);", enterSignal);
        // No activo (05) recolocado al final: 1318.5 + 429.5 = 1748
        Assert.Contains("itemset('<{BD}>Graficos/Escrutinio/partidos/partido05','OBJ_DISPLACEMENT[0]',1748);", enterSignal);
        Assert.Contains("itemset('<{BD}>Escrutinio/Entra','EVENT_RUN');", enterSignal);

        var updateSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemgo('<{BD}>fichaPartido','PRIM_RECGLO_LEN[0]',429.5,0.3,0);", updateSignal);
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01','OBJ_DISPLACEMENT[0]',0,0.3,0);", updateSignal);
        Assert.Contains("itemset('<{BD}>Escrutinio/Actualiza','EVENT_RUN');", updateSignal);
    }

    [Fact]
    public void Ticker_Entra_WithMoreThan6ActiveParties_EmitsSaleFoto()
    {
        var composer = new DefaultSignalComposer();
        var parties = Enumerable.Range(1, 7)
            .Select(i => new PartidoSnapshot(i.ToString("D5"), i.ToString("D5"), $"P{i}", $"Partido {i}", $"Cand {i}", 10 - i, 10 - i, 10 - i, 5, 10.0, 9.0, 1000, 900, 0, 0, 0))
            .ToList();

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Partidos = parties,
            CsvTemplatePartidos = parties
        };

        var enterSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>SaleFoto','EVENT_RUN');", enterSignal);
        Assert.Contains("itemset('<{BD}>Escrutinio/Entra','EVENT_RUN');", enterSignal);
    }

    [Fact]
    public void VideoIn_And_VideoOut_CalculatesDynamicWidthsScalesAndShortCircuitsTickerUpdate()
    {
        var composer = new DefaultSignalComposer();
        var parties = Enumerable.Range(1, 5)
            .Select(i => new PartidoSnapshot(i.ToString("D5"), i.ToString("D5"), $"P{i}", $"Partido {i}", $"Cand {i}", 20 - i, 20 - i, 20 - i, 10, 15.0, 14.0, 5000, 4500, 0, 0, 0))
            .ToList();

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Partidos = parties,
            CsvTemplatePartidos = parties
        };

        var videoInSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.VideoIn,
            CommandValue = "00001"
        }, snapshot);

        // Con 5 partidos activos: minWidth = 349.6, widthOthers = (1708 - 349.6) / 4 = 339.6
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01/fichaPartido','OBJ_SCALE[2]',1.4,0.5,0);", videoInSignal);
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01/Vid','OBJ_DISPLACEMENT[0]',-656,0.5,0);", videoInSignal);
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01/DirectoMascara/Directo','OBJ_DISPLACEMENT[2]',40,0.5,0);", videoInSignal);
        Assert.Contains("itemset('<{BD}>VIDEOS/01/ENTRA','EVENT_RUN');", videoInSignal);
        Assert.Contains("itemgo('<{BD}>fichaPartido','PRIM_RECGLO_LEN[0]',339.6,0.5,0);", videoInSignal);
        Assert.Contains("itemset('<{BD}>Titular/Ocultar','EVENT_RUN');", videoInSignal);

        // Mientras hay un vídeo expandido, Ticker Update solo emite Actualiza sin recalcular PRIM_RECGLO_LEN
        var updateWhileExpanded = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Scene = "TICKER",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot);

        Assert.DoesNotContain("PRIM_RECGLO_LEN[0]", updateWhileExpanded);
        Assert.Contains("itemset('<{BD}>Escrutinio/Actualiza','EVENT_RUN');", updateWhileExpanded);

        // VideoOut restaura escala a 1, recupera titular y saca VIDEOS/01
        var videoOutSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.VideoOut,
            CommandValue = "00001"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Titular/Recuperar','EVENT_RUN');", videoOutSignal);
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01/fichaPartido','OBJ_SCALE[0]',1,0.5,0.3);", videoOutSignal);
        Assert.Contains("itemgo('<{BD}>Graficos/Escrutinio/partidos/partido01/fichaPartido','OBJ_SCALE[2]',1,0.5,0.3);", videoOutSignal);
        Assert.Contains("itemset('<{BD}>VIDEOS/01/SALE','EVENT_RUN');", videoOutSignal);
    }

    [Fact]
    public void TickerTD_Entra_And_Actualiza_PositionsPartiesAndAnimatesRankImprovement()
    {
        var composer = new DefaultSignalComposer();
        var snapshot1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var tdEntra = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.TickerTDEntra
        }, snapshot1);

        // 4 partidos activos => layout[4]: Size=320, Positions=[0,341,683,1024], LogoPos=-621, EscanosPos=-441
        Assert.Contains("itemgo('<{BD}>NumeroEscrutado','TEXT_STRING','87.45%',0.5,0);", tdEntra);
        Assert.Contains("itemgo('<{BD}>Pastilla','PRIM_BAR_LEN[0]',320,0.5,0);", tdEntra);
        Assert.Contains("itemgo('<{BD}>Partidos/PP','OBJ_DISPLACEMENT[0]',0,0.5,0);", tdEntra);
        Assert.Contains("itemgo('<{BD}>Partidos/PSOE','OBJ_DISPLACEMENT[0]',341,0.5,0);", tdEntra);
        Assert.Contains("itemset('<{BD}>Partidos/PP/Logo','OBJ_DISPLACEMENT[0]',-621);", tdEntra);
        Assert.Contains("itemset('<{BD}>Partidos/PP/Escaños','OBJ_DISPLACEMENT[0]',-441);", tdEntra);
        Assert.Contains("itemgo('<{BD}>DatoTD/PP','MAP_INT_PAR',137,0.5,0);", tdEntra);
        Assert.Contains("itemset('<{BD}>FALDON_TD/ENTRA','EVENT_RUN');", tdEntra);

        // Ahora PSOE supera a PP en escaños (PSOE sube de índice 1 a 0)
        var partidosActualizados = snapshot1.Partidos
            .Select(p => p.Siglas == "PSOE" ? p with { Escanios = 145 } : p)
            .ToList();
        var snapshot2 = snapshot1 with { Escrutado = 91.2, Partidos = partidosActualizados };

        var tdActualiza = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.TickerTDActualiza
        }, snapshot2);

        Assert.Contains("itemgo('<{BD}>NumeroEscrutado','TEXT_STRING','91.2%',0.5,0);", tdActualiza);
        // PSOE sube => animación en eje Y (-100 -> 0)
        Assert.Contains("itemgo('<{BD}>Partidos/PSOE','OBJ_DISPLACEMENT[1]',-100,0.25,0);", tdActualiza);
        Assert.Contains("itemgo('<{BD}>Partidos/PSOE','OBJ_DISPLACEMENT[0]',0,0.5,0.25);", tdActualiza);
        Assert.Contains("itemgo('<{BD}>Partidos/PSOE','OBJ_DISPLACEMENT[1]',0,0.25,0.75);", tdActualiza);
        // PP baja a posición 341 sin salto Y
        Assert.Contains("itemgo('<{BD}>Partidos/PP','OBJ_DISPLACEMENT[0]',341,0.5,0);", tdActualiza);
    }

    [Fact]
    public void Sedes_Faldon_AlternatesSlots01And02WithSede04AndSede03()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var entra = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.SedesEntra,
            CommandValue = "00001"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>datosSede01','MAP_STRING_PAR','01');", entra);
        Assert.Contains("itemset('<{BD}>Sede01/Entra','EVENT_RUN');", entra);

        var encadena1 = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.SedesEncadena,
            CommandValue = "00002"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>datosSede02','MAP_STRING_PAR','02');", encadena1);
        Assert.Contains("itemset('<{BD}>Sede04','EVENT_RUN');", encadena1);

        var encadena2 = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.SedesEncadena,
            CommandValue = "00003"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>datosSede01','MAP_STRING_PAR','03');", encadena2);
        Assert.Contains("itemset('<{BD}>Sede03','EVENT_RUN');", encadena2);

        var sale = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Faldon,
            Oficiales = true,
            Command = GraphicCommand.SedesSale
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Sede01/Sale','EVENT_RUN');", sale);
    }
}
