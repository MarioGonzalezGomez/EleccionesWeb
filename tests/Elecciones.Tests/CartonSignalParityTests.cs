using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class CartonSignalParityTests
{
    [Fact]
    public void Participacion_Enter_And_Update_AppliesUnder25RuleAndMapProvincias()
    {
        var composer = new DefaultSignalComposer();
        var snapshotUnder25 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Circunscripcion = new CircunscripcionSummary("0100000", "Andalucía"),
            Avance = 1,
            ProvinciasAutonomia =
            [
                new ProvinciaMapaSnapshot("0141000", "Sevilla", "PSOE"),
                new ProvinciaMapaSnapshot("0129000", "Málaga", "PP")
            ],
            HoraAvance1Historico = "14:00 (2023)",
            Avance1Historico = 18.25,
            HoraAvance1 = "14:00",
            Avance1 = 22.40
        };

        var enterSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshotUnder25);

        Assert.Contains("itemset('<{BD}>PARTICIPACION/PREPARA','EVENT_RUN');", enterSignal);
        Assert.Contains("itemgo('<{BD}>Participacion/Mapa/Sevilla','OBJ_CULL',0,0.3,0);", enterSignal);
        Assert.Contains("itemgo('<{BD}>Participacion/Mapa/Málaga','OBJ_CULL',0,0.3,0);", enterSignal);
        Assert.Contains("itemset('<{BD}>Participacion/LugarTxt','TEXT_STRING','Andalucía');", enterSignal);
        Assert.Contains("itemset('<{BD}>Participacion/Hora_Izq','TEXT_STRING','14:00 (2023)');", enterSignal);
        Assert.Contains("itemset('<{BD}>Participacion/Hora_Dch','TEXT_STRING','14:00');", enterSignal);
        Assert.Contains("itemgo('<{BD}>Participacion_Barra_Izq','MAP_FLOAT_PAR',18.25,0.5,0.6);", enterSignal);
        Assert.Contains("itemgo('<{BD}>Participacion_Barra_Dch','MAP_FLOAT_PAR',22.4,0.5,0.6);", enterSignal);
        // Regla < 25%
        Assert.Contains("itemset('<{BD}>Participacion/Barras/Barra_Dch/Txt_Dch','OBJ_OFFSET[2]',322);", enterSignal);
        Assert.Contains("itemset('<{BD}>Participacion/Txt_Dch','TEXT_FONT','Heavy_Naranja');", enterSignal);
        Assert.Contains("itemset('<{BD}>PARTICIPACION/ENTRA','EVENT_RUN');", enterSignal);

        var snapshotOver25 = snapshotUnder25 with { Avance1 = 58.10 };
        var updateSignal = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshotOver25);

        Assert.Contains("itemset('<{BD}>PARTICIPACION/ENCADENA','EVENT_RUN');", updateSignal);
        Assert.Contains("itemgo('<{BD}>Participacion/Mapa/Sevilla','OBJ_CULL',0,0.3,0.3);", updateSignal);
        Assert.Contains("itemgo('<{BD}>Participacion/Barras/Barra_Dch/Txt_Dch','OBJ_OFFSET[2]',190,0.5,0);", updateSignal);
        Assert.Contains("itemset('<{BD}>Participacion/Txt_Dch','TEXT_FONT','Heavy_Negro');", updateSignal);
    }

    [Fact]
    public void CcaaCartones_And_Mayorias_ConfigureMapAndFichasDisplacement()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Circunscripcion = new CircunscripcionSummary("0100000", "Andalucía"),
            ProvinciasAutonomia =
            [
                new ProvinciaMapaSnapshot("0141000", "Sevilla", "PSOE"),
                new ProvinciaMapaSnapshot("0114000", "Córdoba", "PP")
            ]
        };

        var ccaaEnter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "CCAA_CARTONES",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>CCAA_CARTONES/PREPARA','EVENT_RUN');", ccaaEnter);
        Assert.Contains("itemset('<{BD}>CCAA_Carton/LugarTxt','TEXT_STRING','Andalucía');", ccaaEnter);
        Assert.Contains("itemgo('<{BD}>CCAA_Carton/Mapa/Sevilla','OBJ_CULL',0,0.3,0);", ccaaEnter);
        Assert.Contains("itemset('<{BD}>CCAA_Carton/Fichas/PP','OBJ_DISPLACEMENT[2]',0);", ccaaEnter);
        Assert.Contains("itemset('<{BD}>CCAA_Carton/Fichas/PSOE','OBJ_DISPLACEMENT[2]',-110);", ccaaEnter);
        Assert.Contains("itemset('<{BD}>CCAA_CARTONES/ENTRA','EVENT_RUN');", ccaaEnter);

        var mayoriasEnter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "MAYORIAS",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>MAYORIAS/PREPARA','EVENT_RUN');", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>Mayorias/LugarTxt1','TEXT_STRING','Andalucía');", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>Mayorias/Sevilla','MAT_LIST_COLOR','PSOE');", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>Mayorias/Córdoba','MAT_LIST_COLOR','PP');", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>Mapa_Mayorias/Fichas/PP','OBJ_DISPLACEMENT[2]',0);", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>Mapa_Mayorias/Fichas/PSOE','OBJ_DISPLACEMENT[2]',-100);", mayoriasEnter);
        Assert.Contains("itemset('<{BD}>MAYORIAS/ENTRA','EVENT_RUN');", mayoriasEnter);
    }

    [Fact]
    public void Carrusel_Enter_Encadena_And_Update_TransitionsCardsFrom1920To0AndMinus1920()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "CARRUSEL",
            Action = OperationActionType.Enter,
            Oficiales = true,
            CommandValue = "PP"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>CARRUSEL/OFICIAL','EVENT_RUN');", enter);
        Assert.Contains("itemset('<{BD}>Carton_Carrusel/Oficiales/PSOE','OBJ_DISPLACEMENT[0]',1920);", enter);
        Assert.Contains("itemgo('<{BD}>Carton_Carrusel/Oficiales/PP','OBJ_DISPLACEMENT[0]',0,0.5,0);", enter);
        Assert.Contains("itemset('<{BD}>CARRUSEL/ENTRA','EVENT_RUN');", enter);

        var encadena = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "CARRUSEL",
            Oficiales = true,
            Command = GraphicCommand.CarruselEncadena,
            CommandValue = "PSOE"
        }, snapshot);

        Assert.Contains("itemgo('<{BD}>Carton_Carrusel/Oficiales/PP','OBJ_DISPLACEMENT[0]',-1920,0.5,0);", encadena);
        Assert.Contains("itemgo('<{BD}>Carton_Carrusel/Oficiales/PSOE','OBJ_DISPLACEMENT[0]',0,0.5,0);", encadena);

        var update = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "CARRUSEL",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Oficial_Codigo','MAP_LLSTRING_LOAD');", update);
    }

    [Fact]
    public void CartonPartidos_Enter_PositionsTwoColumnsAndFormatsText()
    {
        var composer = new DefaultSignalComposer();
        var parties = Enumerable.Range(1, 9)
            .Select(i => new PartidoSnapshot(i.ToString("D5"), i.ToString("D5"), $"P{i}", $"Partido {i}", $"Cand {i}", 20 - i, 20 - i, 20 - i, 10, 12.5, 11.0, 123456, 100000, 0, 0, 0))
            .ToList();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Partidos = parties,
            CsvTemplatePartidos = parties
        };

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "CARTON_PARTIDOS",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        // Columna 1 (i=0 => x=0, y=0.0, z=0)
        Assert.Contains("itemset('<{BD}>Carton_Partidos/Fichas/P1','OBJ_DISPLACEMENT',(0,0.0,0));", enter);
        // Columna 2 (i=7 => x=918, y=-1.4, z=100)
        Assert.Contains("itemset('<{BD}>Carton_Partidos/Fichas/P8','OBJ_DISPLACEMENT',(918,-1.4,100));", enter);
        Assert.Contains("itemset('<{BD}>Mayorias1/P1/Partido_Escanos1','TEXT_STRING','19');", enter);
        Assert.Contains("itemset('<{BD}>Mayorias1/P1/Partido_Porcentaje1','TEXT_STRING','12.5\\\\f<Light>%');", enter);
        Assert.Contains("itemset('<{BD}>Mayorias1/P1/Partido_Votos1','TEXT_STRING','123.456\\\\f<Light> votos');", enter);
        Assert.Contains("itemset('<{BD}>Oficial_Codigo','MAP_LLSTRING_LOAD');", enter);
        Assert.Contains("itemset('<{BD}>UltimoEscanoNDatos/CSV','MAP_LLSTRING_LOAD');", enter);
        Assert.Contains("itemset('<{BD}>CARTON_PARTIDOS/ENTRA','EVENT_RUN');", enter);
    }

    [Fact]
    public void UltimoEscano_Carton_DistinguishesCambioUltimoAndCambioAbajo()
    {
        var composer = new DefaultSignalComposer();
        var snapshot1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "ULTIMO_ESCANO",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot1);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/ENTRA','EVENT_RUN');", enter);

        // 1. Cambia solo el partido en lucha (LuchaUltimoEscano pasa de PSOE=00002 a VOX=00003)
        var partidosCambioLucha = snapshot1.Partidos
            .Select(p => p.Codigo switch
            {
                "00002" => p with { LuchaUltimoEscano = 0 },
                "00003" => p with { LuchaUltimoEscano = 1 },
                _ => p
            })
            .ToList();
        var snapshotCambioLucha = snapshot1 with { Partidos = partidosCambioLucha };

        var updateAbajo = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "ULTIMO_ESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshotCambioLucha);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/CambioAbajo','EVENT_RUN');", updateAbajo);

        // 2. Cambia el partido con el último escaño (EsUltimoEscano pasa de PP=00001 a SUMAR=00004)
        var partidosCambioUltimo = snapshotCambioLucha.Partidos
            .Select(p => p.Codigo switch
            {
                "00001" => p with { EsUltimoEscano = 0 },
                "00004" => p with { EsUltimoEscano = 1 },
                _ => p
            })
            .ToList();
        var snapshotCambioUltimo = snapshotCambioLucha with { Partidos = partidosCambioUltimo };

        var updateCambio = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Carton,
            Scene = "ULTIMO_ESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshotCambioUltimo);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/Cambio','EVENT_RUN');", updateCambio);
    }
}

