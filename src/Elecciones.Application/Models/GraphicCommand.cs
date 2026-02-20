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
    UltimoEntraPartidoDerecha = 42
}
