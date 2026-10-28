using Elecciones.Application.Models;

namespace Elecciones.Tests.Helpers;

public static class TestSnapshotFactory
{
    public static BrainStormSnapshot CreateSampleSnapshot(
        bool oficiales = true,
        string circunscripcionCodigo = "9900000",
        string circunscripcionNombre = "ESPANA")
    {
        return new BrainStormSnapshot
        {
            Circunscripcion = new CircunscripcionSummary(circunscripcionCodigo, circunscripcionNombre),
            Oficiales = oficiales,
            Avance = 3,
            Escrutado = 87.45,
            Participacion = 68.12,
            ParticipacionHistorica = 66.20,
            ParticipacionMedia = 67.16,
            EscaniosTotales = 350,
            MayoriaAbsoluta = 176,
            VotantesTotales = 24500120,
            VotantesFaltan = 3100200,
            AnioUltimasElecciones = 2023,
            NumPartidos = 4,
            NombreSondeo = oficiales ? string.Empty : "GAD3",
            Partidos =
            [
                new PartidoSnapshot(
                    Codigo: "00001",
                    Padre: "00001",
                    Siglas: "PP",
                    Nombre: "PARTIDO POPULAR",
                    Candidato: "Alberto Nunez Feijoo",
                    Escanios: 137,
                    EscaniosDesdeSondeo: 135,
                    EscaniosHastaSondeo: 140,
                    EscaniosHistoricos: 89,
                    PorcentajeVoto: 33.05,
                    PorcentajeVotoHistorico: 20.80,
                    Votos: 8160000,
                    VotantesHistoricos: 5047040,
                    RestoVotos: 0,
                    EsUltimoEscano: 1,
                    LuchaUltimoEscano: 0),
                new PartidoSnapshot(
                    Codigo: "00002",
                    Padre: "00002",
                    Siglas: "PSOE",
                    Nombre: "PARTIDO SOCIALISTA OBRERO ESPANOL",
                    Candidato: "Pedro Sanchez",
                    Escanios: 121,
                    EscaniosDesdeSondeo: 118,
                    EscaniosHastaSondeo: 123,
                    EscaniosHistoricos: 120,
                    PorcentajeVoto: 31.70,
                    PorcentajeVotoHistorico: 28.00,
                    Votos: 7820000,
                    VotantesHistoricos: 6792199,
                    RestoVotos: 1420,
                    EsUltimoEscano: 0,
                    LuchaUltimoEscano: 1),
                new PartidoSnapshot(
                    Codigo: "00003",
                    Padre: "00003",
                    Siglas: "VOX",
                    Nombre: "VOX",
                    Candidato: "Santiago Abascal",
                    Escanios: 33,
                    EscaniosDesdeSondeo: 30,
                    EscaniosHastaSondeo: 35,
                    EscaniosHistoricos: 52,
                    PorcentajeVoto: 12.39,
                    PorcentajeVotoHistorico: 15.08,
                    Votos: 3057000,
                    VotantesHistoricos: 3656979,
                    RestoVotos: 0,
                    EsUltimoEscano: 0,
                    LuchaUltimoEscano: 0),
                new PartidoSnapshot(
                    Codigo: "00004",
                    Padre: "00004",
                    Siglas: "SUMAR",
                    Nombre: "SUMAR",
                    Candidato: "Yolanda Diaz",
                    Escanios: 31,
                    EscaniosDesdeSondeo: 28,
                    EscaniosHastaSondeo: 32,
                    EscaniosHistoricos: 35,
                    PorcentajeVoto: 12.31,
                    PorcentajeVotoHistorico: 12.86,
                    Votos: 3038000,
                    VotantesHistoricos: 3119364,
                    RestoVotos: 0,
                    EsUltimoEscano: 0,
                    LuchaUltimoEscano: 0),
                new PartidoSnapshot(
                    Codigo: "00005",
                    Padre: "00005",
                    Siglas: "PACMA",
                    Nombre: "PACMA",
                    Candidato: "Candidato PACMA",
                    Escanios: 0,
                    EscaniosDesdeSondeo: 0,
                    EscaniosHastaSondeo: 0,
                    EscaniosHistoricos: 0,
                    PorcentajeVoto: 0.68,
                    PorcentajeVotoHistorico: 0.94,
                    Votos: 165000,
                    VotantesHistoricos: 228856,
                    RestoVotos: 0,
                    EsUltimoEscano: 0,
                    LuchaUltimoEscano: 0)
            ]
        };
    }
}

