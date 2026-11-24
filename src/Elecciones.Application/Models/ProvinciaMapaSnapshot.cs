namespace Elecciones.Application.Models;

public sealed record ProvinciaMapaSnapshot(
    string Codigo,
    string Nombre,
    string SiglaGanador = "",
    string CodigoGanador = "",
    double Escrutado = 0,
    int Escanios = 0,
    double Participacion = 0,
    int Votantes = 0,
    int EscaniosGanador = 0,
    double PorcentajeGanador = 0
);

public sealed record CircunscripcionMayoriaSnapshot(
    string CodigoLugar,
    string NombreLugar,
    string CodigoPartidoGanador,
    string SiglasPartidoGanador,
    int EscaniosCircunscripcion,
    int EscaniosGanador,
    double Escrutado,
    double Participacion
);

public sealed record VideoSlotSnapshot(
    int Slot,
    bool IsLive,
    string Path = ""
);

