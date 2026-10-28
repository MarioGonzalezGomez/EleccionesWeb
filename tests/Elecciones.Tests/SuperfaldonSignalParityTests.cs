using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Tests.Helpers;
using Xunit;

namespace Elecciones.Tests;

public sealed class SuperfaldonSignalParityTests
{
    [Fact]
    public void Superfaldon_Ultimo_Entra_Cambio_CambioAbajo_Sale()
    {
        var composer = new DefaultSignalComposer();
        var snapshot1 = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot1);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/Entra','EVENT_RUN');", enter);

        // Sin cambios en último ni en lucha -> emite ReloadPartidos
        var updateSinCambio = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshot1);

        Assert.Contains("itemset('<{BD}>ReloadPartidos','EVENT_RUN');", updateSinCambio);

        // Cambia solo quien lucha (PSOE=00002 -> VOX=00003) -> UltimoEscanoN/CambioAbajo
        var partidosCambioLucha = snapshot1.Partidos
            .Select(p => p.Codigo switch
            {
                "00002" => p with { LuchaUltimoEscano = 0 },
                "00003" => p with { LuchaUltimoEscano = 1 },
                _ => p
            })
            .ToList();
        var snapshotCambioLucha = snapshot1 with { Partidos = partidosCambioLucha };

        var updateCambioAbajo = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshotCambioLucha);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/CambioAbajo','EVENT_RUN');", updateCambioAbajo);

        // Cambia quien retiene (PP=00001 -> SUMAR=00004) -> UltimoEscanoN/Cambio
        var partidosCambioUltimo = snapshotCambioLucha.Partidos
            .Select(p => p.Codigo switch
            {
                "00001" => p with { EsUltimoEscano = 0 },
                "00004" => p with { EsUltimoEscano = 1 },
                _ => p
            })
            .ToList();
        var snapshotCambioUltimo = snapshotCambioLucha with { Partidos = partidosCambioUltimo };

        var updateCambioArriba = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Update,
            Oficiales = true
        }, snapshotCambioUltimo);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/Cambio','EVENT_RUN');", updateCambioArriba);

        var exit = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "ULTIMOESCANO",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, snapshotCambioUltimo);

        Assert.Contains("itemset('<{BD}>UltimoEscanoN/Sale','EVENT_RUN');", exit);
    }

    [Fact]
    public void Superfaldon_Carrusel_Sale_RepliegaSedeSiEstabaDesplegada()
    {
        var composer = new DefaultSignalComposer();
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        // Entra carrusel Superfaldón
        var enter = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "SUPERFALDON",
            Action = OperationActionType.Enter,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Superfaldon/Oficial/Entra','EVENT_RUN');", enter);

        // Despliega sede de PP
        var sedeEntra = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Oficiales = true,
            Command = GraphicCommand.SedesEntra,
            CommandValue = "00001"
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Superfaldon/Sedes/DespliegaSede','EVENT_RUN');", sedeEntra);

        // Al salir de SUPERFALDON con sede desplegada, debe replegar sede y salir del carrusel
        var exit = composer.Compose(new OperationRequest
        {
            OperatorId = "op1",
            Module = GraphicModule.Superfaldon,
            Scene = "SUPERFALDON",
            Action = OperationActionType.Exit,
            Oficiales = true
        }, snapshot);

        Assert.Contains("itemset('<{BD}>Superfaldon/Sedes/RepliegaSede','EVENT_RUN');", exit);
        Assert.Contains("itemset('<{BD}>Superfaldon/Oficial/Sale','EVENT_RUN');", exit);
    }
}

