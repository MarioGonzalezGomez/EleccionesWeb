using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class CartonSignalParityTests
{
    [Fact]
    public void Participacion_Enter_SamePlaceUpdate_PlaceChangeSiguiente_AndExit_MatchOrdenesTxt()
    {
        var composer = new DefaultSignalComposer();
        var andalucia = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Circunscripcion = new CircunscripcionSummary("0100000", "Andalucia"),
            ParticipacionHistorica = 87.12,
            Participacion = 49.0,
            ParticipacionMedia = 3.01
        };

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Participacion,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, andalucia);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>Participacion/Historico','MAP_FLOAT_PAR',87.12);",
                "itemset('<{BD}>Participacion/Actual','MAP_FLOAT_PAR',49);",
                "itemset('<{BD}>Participacion/Media','MAP_FLOAT_PAR',3.01);",
                "itemset('<{BD}>Participacion/CodigoLugar','MAP_STRING_PAR','0100000');",
                "itemset('<{BD}>Participacion/ProvinciaNombre','MAP_STRING_PAR','Andalucia');",
                "itemset('<{BD}>PARTICIPACION/ENTRA','EVENT_RUN');"),
            enter);

        // Actualización en la misma circunscripción -> variables + ACTUALIZAR
        var samePlaceUpdate = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Participacion,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Update,
            Oficiales = true
        }, andalucia with { Participacion = 51.45 });

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>Participacion/Historico','MAP_FLOAT_PAR',87.12);",
                "itemset('<{BD}>Participacion/Actual','MAP_FLOAT_PAR',51.45);",
                "itemset('<{BD}>Participacion/Media','MAP_FLOAT_PAR',3.01);",
                "itemset('<{BD}>Participacion/CodigoLugar','MAP_STRING_PAR','0100000');",
                "itemset('<{BD}>Participacion/ProvinciaNombre','MAP_STRING_PAR','Andalucia');",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');"),
            samePlaceUpdate);

        // Cambio de circunscripción en aire -> variables + PARTICIPACION/SIGUIENTE
        var castillaLeon = andalucia with
        {
            Circunscripcion = new CircunscripcionSummary("0800000", "Castilla y león"),
            ParticipacionHistorica = 71.30,
            Participacion = 64.20,
            ParticipacionMedia = 63.10
        };

        var placeChange = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Participacion,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Update,
            Oficiales = true
        }, castillaLeon);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>Participacion/Historico','MAP_FLOAT_PAR',71.3);",
                "itemset('<{BD}>Participacion/Actual','MAP_FLOAT_PAR',64.2);",
                "itemset('<{BD}>Participacion/Media','MAP_FLOAT_PAR',63.1);",
                "itemset('<{BD}>Participacion/CodigoLugar','MAP_STRING_PAR','0800000');",
                "itemset('<{BD}>Participacion/ProvinciaNombre','MAP_STRING_PAR','Castilla y león');",
                "itemset('<{BD}>PARTICIPACION/SIGUIENTE','EVENT_RUN');"),
            placeChange);

        var exit = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Participacion,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, castillaLeon);

        Assert.Equal("itemset('<{BD}>PARTICIPACION/SALE','EVENT_RUN');", exit);

        // Permitido también en modo Sondeo (ej. avances de participación mientras se emiten sondeos)
        var enterDuringSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Participacion,
            Scene = "PARTICIPACION",
            Action = OperationActionType.Enter,
            Oficiales = false
        }, andalucia);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>Participacion/Historico','MAP_FLOAT_PAR',87.12);",
                "itemset('<{BD}>Participacion/Actual','MAP_FLOAT_PAR',49);",
                "itemset('<{BD}>Participacion/Media','MAP_FLOAT_PAR',3.01);",
                "itemset('<{BD}>Participacion/CodigoLugar','MAP_STRING_PAR','0100000');",
                "itemset('<{BD}>Participacion/ProvinciaNombre','MAP_STRING_PAR','Andalucia');",
                "itemset('<{BD}>PARTICIPACION/ENTRA','EVENT_RUN');"),
            enterDuringSondeo);
    }

    [Fact]
    public void Mayorias_OficialesAndSondeo_Enter_Cambio_Actualizar_Pagina_AndSale_MatchOrdenesTxt()
    {
        var composer = new DefaultSignalComposer();
        var cyl = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Circunscripcion = new CircunscripcionSummary("0800000", "Castilla y león")
        };

        // OFICIALES
        var enterOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, cyl);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>Mayorias/CodigoLugar','MAP_STRING_PAR','0800000');",
                "itemset('<{BD}>Mayorias/Lugar','MAP_STRING_PAR','Castilla y león');",
                "itemset('<{BD}>MAYORIAS/ENTRA','EVENT_RUN');"),
            enterOficial);

        // Misma circunscripción -> solo ACTUALIZAR
        var sameUpdateOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Action = OperationActionType.Update,
            Oficiales = true
        }, cyl);

        Assert.Equal("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", sameUpdateOficial);

        // Cambio de circunscripción -> ACTUALIZAR + variables + MAYORIAS/CAMBIO
        var andalucia = cyl with { Circunscripcion = new CircunscripcionSummary("0100000", "Andalucía") };
        var cambioOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Action = OperationActionType.Update,
            Oficiales = true
        }, andalucia);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>Mayorias/CodigoLugar','MAP_STRING_PAR','0100000');",
                "itemset('<{BD}>Mayorias/Lugar','MAP_STRING_PAR','Andalucía');",
                "itemset('<{BD}>MAYORIAS/CAMBIO','EVENT_RUN');"),
            cambioOficial);

        var paginaOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Command = GraphicCommand.CartonPagina,
            Oficiales = true
        }, andalucia);

        Assert.Equal("itemset('<{BD}>MAYORIAS/PAGINA','EVENT_RUN');", paginaOficial);

        var saleOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, andalucia);

        Assert.Equal("itemset('<{BD}>MAYORIAS/SALE','EVENT_RUN');", saleOficial);

        // SONDEO
        var enterSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "SONDEO_MAYORIAS",
            Action = OperationActionType.Enter,
            Oficiales = false
        }, cyl);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>SondeoMayorias/CodigoLugar','MAP_STRING_PAR','0800000');",
                "itemset('<{BD}>SondeoMayorias/Lugar','MAP_STRING_PAR','Castilla y león');",
                "itemset('<{BD}>SONDEO_MAYORIAS/ENTRA','EVENT_RUN');"),
            enterSondeo);

        var cambioSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "SONDEO_MAYORIAS",
            Action = OperationActionType.Update,
            Oficiales = false
        }, andalucia);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>SondeoMayorias/CodigoLugar','MAP_STRING_PAR','0100000');",
                "itemset('<{BD}>SondeoMayorias/Lugar','MAP_STRING_PAR','Andalucía');",
                "itemset('<{BD}>SONDEO_MAYORIAS/CAMBIO','EVENT_RUN');"),
            cambioSondeo);

        var paginaSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "SONDEO_MAYORIAS",
            Command = GraphicCommand.CartonPagina,
            Oficiales = false
        }, andalucia);

        Assert.Equal("itemset('<{BD}>SONDEO_MAYORIAS/PAGINA','EVENT_RUN');", paginaSondeo);

        var saleSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "SONDEO_MAYORIAS",
            Action = OperationActionType.Exit,
            Oficiales = false
        }, andalucia);

        Assert.Equal("itemset('<{BD}>SONDEO_MAYORIAS/SALE','EVENT_RUN');", saleSondeo);
    }

    [Fact]
    public void Estatal_OficialesAndSondeo_Enter_Actualizar_Pagina_AndSale_MatchOrdenesTxt()
    {
        var composer = new DefaultSignalComposer();
        var espana = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true, "9900000", "España");

        var enterOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, espana);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>Estatal/Lugar','MAP_STRING_PAR','España');",
                "itemset('<{BD}>ESTATAL/ENTRA','EVENT_RUN');"),
            enterOficial);

        var updateOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Action = OperationActionType.Update,
            Oficiales = true
        }, espana);

        Assert.Equal("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", updateOficial);

        var paginaOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Command = GraphicCommand.CartonPagina,
            Oficiales = true
        }, espana);

        Assert.Equal("itemset('<{BD}>ESTATAL/PAGINA','EVENT_RUN');", paginaOficial);

        var saleOficial = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "ESTATAL",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, espana);

        Assert.Equal("itemset('<{BD}>ESTATAL/SALE','EVENT_RUN');", saleOficial);

        var enterSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "SONDEO_ESTATAL",
            Action = OperationActionType.Enter,
            Oficiales = false
        }, espana);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>SondeoEstatal/Lugar','MAP_STRING_PAR','España');",
                "itemset('<{BD}>SONDEO_ESTATAL/ENTRA','EVENT_RUN');"),
            enterSondeo);

        var paginaSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "SONDEO_ESTATAL",
            Command = GraphicCommand.CartonPagina,
            Oficiales = false
        }, espana);

        Assert.Equal("itemset('<{BD}>SONDEO_ESTATAL/PAGINA','EVENT_RUN');", paginaSondeo);

        var saleSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Estatal,
            Scene = "SONDEO_ESTATAL",
            Action = OperationActionType.Exit,
            Oficiales = false
        }, espana);

        Assert.Equal("itemset('<{BD}>SONDEO_ESTATAL/SALE','EVENT_RUN');", saleSondeo);
    }

    [Fact]
    public void UltimoEscano_Carton_SendsFiveAutomaticVariablesFromFullParties_AndBlocksSondeo()
    {
        var composer = new DefaultSignalComposer();
        var fullParties = new List<PartidoSnapshot>
        {
            new("4", "00004", "SUMAR", "SUMAR", "", 1, 1, 1, 1, 9.4, 11.2, 22450, 24500, 0, EsUltimoEscano: 1, LuchaUltimoEscano: 0),
            // Segundo tiene 0 escaños actuales pero lucha por el último escaño a 224 votos
            new("2", "00002", "PSOE", "PSOE", "", 0, 0, 1, 1, 8.9, 10.1, 22226, 23100, RestoVotos: 224, EsUltimoEscano: 0, LuchaUltimoEscano: 1)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Circunscripcion = new CircunscripcionSummary("0700000", "Castilla y la mancha"),
            VotantesFaltan = 158,
            Partidos = fullParties.Where(p => p.Escanios > 0).ToList(),
            PartidosCircunscripcionCompletos = fullParties
        };

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.UltimoEscano,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>UltimoEscano/Ganador','MAP_STRING_PAR','00004');",
                "itemset('<{BD}>UltimoEscano/Segundo','MAP_STRING_PAR','00002');",
                "itemset('<{BD}>UltimoEscano/Lugar','MAP_STRING_PAR','Castilla y la mancha');",
                "itemset('<{BD}>UltimoEscano/NewPendientes','MAP_INT_PAR',158);",
                "itemset('<{BD}>UltimoEscano/NewVotos','MAP_INT_PAR',224);",
                "itemset('<{BD}>ULTIMOESCANO/ENTRA','EVENT_RUN');"),
            enter);

        var update = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.UltimoEscano,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot with { VotantesFaltan = 92 });

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>UltimoEscano/Ganador','MAP_STRING_PAR','00004');",
                "itemset('<{BD}>UltimoEscano/Segundo','MAP_STRING_PAR','00002');",
                "itemset('<{BD}>UltimoEscano/Lugar','MAP_STRING_PAR','Castilla y la mancha');",
                "itemset('<{BD}>UltimoEscano/NewPendientes','MAP_INT_PAR',92);",
                "itemset('<{BD}>UltimoEscano/NewVotos','MAP_INT_PAR',224);",
                "itemset('<{BD}>ULTIMOESCANO/ACTUALIZA','EVENT_RUN');"),
            update);

        var exit = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.UltimoEscano,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, snapshot);

        Assert.Equal("itemset('<{BD}>ULTIMOESCANO/SALE','EVENT_RUN');", exit);

        var blockedSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.UltimoEscano,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Enter,
            Oficiales = false
        }, snapshot);

        Assert.Equal(string.Empty, blockedSondeo);
    }

    [Fact]
    public void Mapa_UsesLifecycleEventsAndOfficialOnlyRefresh()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mapa,
            Scene = "MAPA_ESCANOS",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Equal(
            string.Join("\n",
                "itemset('<{BD}>ACTUALIZAR','EVENT_RUN');",
                "itemset('<{BD}>MAPA_ESCANOS/ENTRA','EVENT_RUN');"),
            enter);

        var update = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mapa,
            Scene = "MAPA_ESCANOS",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot);
        Assert.Equal("itemset('<{BD}>ACTUALIZAR','EVENT_RUN');", update);

        var blockedSondeo = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mapa,
            Scene = "MAPA_ESCANOS",
            Action = OperationActionType.Enter,
            Oficiales = false
        }, snapshot);
        Assert.Equal(string.Empty, blockedSondeo);

        var exit = composer.Compose(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mapa,
            Scene = "MAPA_ESCANOS",
            Action = OperationActionType.Exit,
            Oficiales = false
        }, snapshot);
        Assert.Equal("itemset('<{BD}>MAPA_ESCANOS/SALE','EVENT_RUN');", exit);
    }
}
