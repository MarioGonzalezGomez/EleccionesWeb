namespace Elecciones.Application.Models;

public sealed record ProvinciaMapaSnapshot(
    string Codigo,
    string Nombre,
    string SiglaGanador = ""
);

public sealed record VideoSlotSnapshot(
    int Slot,
    bool IsLive,
    string Path = ""
);

