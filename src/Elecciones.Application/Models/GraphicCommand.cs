namespace Elecciones.Application.Models;

public enum GraphicCommand
{
    None = 0,

    TickerVotosEntra = 1,
    TickerVotosSale = 2,
    TickerHistoricosEntra = 3,
    TickerHistoricosSale = 4,
    TickerFotosEntra = 5,
    TickerFotosSale = 6,
    TickerTDEntra = 7,
    TickerTDActualiza = 8,
    TickerTDSale = 9,

    VideoIn = 10,
    VideoOut = 11,
    VideoInTodos = 12,
    VideoOutTodos = 13,

    SedesEntra = 20,
    SedesEncadena = 21,
    SedesSale = 22,

    PactosEntra = 30,
    PactosSale = 31,
    PactosReinicio = 32,
    PactosEntraIzquierda = 33,
    PactosEntraDerecha = 34,
    PactosSaleIzquierda = 35,
    PactosSaleDerecha = 36,

    UltimoLimpiaPartidos = 40,
    UltimoEntraPartidoIzquierda = 41,
    UltimoEntraPartidoDerecha = 42,

    PrimerosResultadosActiva = 50,
    PrimerosResultadosDesactiva = 51,
    AnimacionSondeoActiva = 52,
    AnimacionSondeoDesactiva = 53,
    DeSondeoAOficiales = 54,
    CambioEleccionesEuropa = 55,
    CambioEleccionesNacional = 56,

    PrimeSubirRotulosTD = 60,
    PrimeBajarRotulosTD = 61,
    PrimeSubirRotulosEspeciales = 62,
    PrimeBajarRotulosEspeciales = 63
}
