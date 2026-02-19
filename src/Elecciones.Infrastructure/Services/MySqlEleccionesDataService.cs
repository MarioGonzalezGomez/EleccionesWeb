using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elecciones.Infrastructure.Services;

public sealed class MySqlEleccionesDataService : IEleccionesDataService
{
    private readonly IDbContextFactory<EleccionesDbContext> _dbContextFactory;
    private readonly ILogger<MySqlEleccionesDataService> _logger;

    public MySqlEleccionesDataService(
        IDbContextFactory<EleccionesDbContext> dbContextFactory,
        ILogger<MySqlEleccionesDataService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var data = await db.Circunscripciones
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.Nombre))
            .OrderBy(x => x.Nombre)
            .Select(x => new CircunscripcionSummary(x.Codigo, x.Nombre))
            .ToListAsync(cancellationToken);

        return data;
    }

    public async Task<BrainStormSnapshot> GetSnapshotAsync(
        string circunscripcionCodigo,
        bool oficiales,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var circ = await db.Circunscripciones
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo == circunscripcionCodigo, cancellationToken);

        if (circ is null)
        {
            circ = await db.Circunscripciones
                .AsNoTracking()
                .OrderBy(x => x.Nombre)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("No circunscripciones available in database.");
        }

        var partidos = await db.CircunscripcionPartidos
            .AsNoTracking()
            .Where(x => x.CodCircunscripcion == circ.Codigo)
            .Include(x => x.Partido)
            .Select(x => new PartidoSnapshot(
                x.CodPartido,
                x.Partido != null ? x.Partido.Siglas : x.CodPartido,
                x.EscaniosHasta,
                x.EscaniosDesdeSondeo,
                x.EscaniosHastaSondeo,
                x.EscaniosHistoricos,
                x.Votos,
                x.Votantes))
            .OrderByDescending(x => oficiales ? x.Escanios : x.EscaniosHastaSondeo)
            .ThenBy(x => x.Codigo)
            .ToListAsync(cancellationToken);

        var avance = ResolveAvance(circ);
        var mayoria = circ.Escanios <= 0 ? 0 : (circ.Escanios / 2) + 1;

        _logger.LogDebug(
            "Snapshot loaded from MySQL for {Circ} ({Count} partidos)",
            circ.Codigo,
            partidos.Count);

        return new BrainStormSnapshot
        {
            Circunscripcion = new CircunscripcionSummary(circ.Codigo, circ.Nombre),
            Oficiales = oficiales,
            Avance = avance,
            Escrutado = circ.Escrutado,
            EscaniosTotales = circ.Escanios,
            MayoriaAbsoluta = mayoria,
            VotantesTotales = circ.Votantes,
            AnioUltimasElecciones = DateTime.UtcNow.Year - 3,
            GeneratedAtUtc = DateTime.UtcNow,
            Partidos = partidos
        };
    }

    private static int ResolveAvance(Entities.CircunscripcionEntity circ)
    {
        if (circ.Avance3 > 0) return 3;
        if (circ.Avance2 > 0) return 2;
        if (circ.Avance1 > 0) return 1;
        return 0;
    }
}
