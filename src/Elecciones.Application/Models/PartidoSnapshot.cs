namespace Elecciones.Application.Models;

public sealed record PartidoSnapshot(
    string Codigo,
    string Siglas,
    int Escanios,
    int EscaniosDesdeSondeo,
    int EscaniosHastaSondeo,
    int EscaniosHistoricos,
    double PorcentajeVoto,
    int Votos
);
