namespace Elecciones.Application.Models;

public sealed record PartidoSnapshot(
    string Codigo,
    string Padre,
    string Siglas,
    string Nombre,
    string Candidato,
    int Escanios,
    int EscaniosDesdeSondeo,
    int EscaniosHastaSondeo,
    int EscaniosHistoricos,
    double PorcentajeVoto,
    double PorcentajeVotoHistorico,
    int Votos,
    int VotantesHistoricos,
    int RestoVotos,
    int EsUltimoEscano,
    int LuchaUltimoEscano
);
