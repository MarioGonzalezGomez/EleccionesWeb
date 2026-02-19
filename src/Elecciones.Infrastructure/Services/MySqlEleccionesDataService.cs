using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Entities;
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
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default)
    {
        query ??= new SnapshotQuery();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var baseQuery = db.CircunscripcionPartidos
            .AsNoTracking()
            .Include(x => x.Partido)
            .Include(x => x.Circunscripcion)
            .AsQueryable();

        List<CircunscripcionPartidoEntity> cps = query.Kind switch
        {
            SnapshotQueryKind.MasVotadosAutonomias => await GetMasVotadosAutonomiasAsync(baseQuery, oficiales, cancellationToken),
            SnapshotQueryKind.MasVotadosProvincias => await GetMasVotadosProvinciasAsync(baseQuery, query.AutonomiaCodigo, oficiales, cancellationToken),
            SnapshotQueryKind.PartidoAutonomias => await GetPartidoAutonomiasAsync(baseQuery, query.PartidoCodigo, oficiales, cancellationToken),
            SnapshotQueryKind.PartidoProvincias => await GetPartidoProvinciasAsync(baseQuery, query.AutonomiaCodigo, query.PartidoCodigo, oficiales, cancellationToken),
            _ => await GetCircunscripcionAsync(baseQuery, query.CircunscripcionCodigo, oficiales, query.IncludeZeroSeats, cancellationToken)
        };

        var contextCirc = await ResolveContextCircunscripcionAsync(db, query, cps, cancellationToken);
        var partidos = BuildPartidoSnapshots(cps, query, oficiales, query.IncludeZeroSeats);

        _logger.LogDebug(
            "Snapshot loaded from MySQL. Kind={Kind}, Circ={Circ}, Count={Count}",
            query.Kind,
            contextCirc.Codigo,
            partidos.Count);

        return new BrainStormSnapshot
        {
            Circunscripcion = new CircunscripcionSummary(contextCirc.Codigo, contextCirc.Nombre),
            Oficiales = oficiales,
            Avance = ResolveAvance(contextCirc),
            Escrutado = contextCirc.Escrutado,
            EscaniosTotales = contextCirc.Escanios,
            MayoriaAbsoluta = contextCirc.Escanios <= 0 ? 0 : (contextCirc.Escanios / 2) + 1,
            VotantesTotales = contextCirc.Votantes,
            AnioUltimasElecciones = DateTime.UtcNow.Year - 3,
            GeneratedAtUtc = DateTime.UtcNow,
            Partidos = partidos
        };
    }

    private static async Task<List<CircunscripcionPartidoEntity>> GetCircunscripcionAsync(
        IQueryable<CircunscripcionPartidoEntity> query,
        string circunscripcionCodigo,
        bool oficiales,
        bool includeZeroSeats,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(circunscripcionCodigo))
        {
            circunscripcionCodigo = "9900000";
        }

        var filtered = query.Where(x => x.CodCircunscripcion == circunscripcionCodigo);

        if (!includeZeroSeats)
        {
            filtered = oficiales
                ? filtered.Where(x => x.EscaniosHasta > 0)
                : filtered.Where(x => x.EscaniosHastaSondeo > 0);
        }

        var data = await filtered.ToListAsync(cancellationToken);
        return OrderCpList(data, oficiales);
    }

    private static async Task<List<CircunscripcionPartidoEntity>> GetMasVotadosAutonomiasAsync(
        IQueryable<CircunscripcionPartidoEntity> query,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        var data = await query
            .Where(x => x.CodCircunscripcion.EndsWith("00000") && !x.CodCircunscripcion.StartsWith("99"))
            .ToListAsync(cancellationToken);

        var grouped = data
            .GroupBy(x => x.CodCircunscripcion)
            .Select(g => OrderCpList(g.ToList(), oficiales).FirstOrDefault())
            .Where(x => x is not null)
            .Cast<CircunscripcionPartidoEntity>()
            .ToList();

        return OrderCpList(grouped, oficiales);
    }

    private static async Task<List<CircunscripcionPartidoEntity>> GetMasVotadosProvinciasAsync(
        IQueryable<CircunscripcionPartidoEntity> query,
        string autonomiaCodigo,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        autonomiaCodigo = autonomiaCodigo?.Trim() ?? string.Empty;

        var data = await query
            .Where(x => x.CodCircunscripcion.StartsWith(autonomiaCodigo))
            .Where(x => x.CodCircunscripcion.EndsWith("000") && !x.CodCircunscripcion.EndsWith("00000"))
            .ToListAsync(cancellationToken);

        var grouped = data
            .GroupBy(x => x.CodCircunscripcion)
            .Select(g => OrderCpList(g.ToList(), oficiales).FirstOrDefault())
            .Where(x => x is not null)
            .Cast<CircunscripcionPartidoEntity>()
            .ToList();

        return OrderCpList(grouped, oficiales);
    }

    private static async Task<List<CircunscripcionPartidoEntity>> GetPartidoAutonomiasAsync(
        IQueryable<CircunscripcionPartidoEntity> query,
        string partidoCodigo,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        partidoCodigo = partidoCodigo?.Trim() ?? string.Empty;

        var data = await query
            .Where(x => x.CodPartido == partidoCodigo)
            .Where(x => x.CodCircunscripcion.EndsWith("00000") && !x.CodCircunscripcion.StartsWith("99"))
            .ToListAsync(cancellationToken);

        return OrderCpList(data, oficiales);
    }

    private static async Task<List<CircunscripcionPartidoEntity>> GetPartidoProvinciasAsync(
        IQueryable<CircunscripcionPartidoEntity> query,
        string autonomiaCodigo,
        string partidoCodigo,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        autonomiaCodigo = autonomiaCodigo?.Trim() ?? string.Empty;
        partidoCodigo = partidoCodigo?.Trim() ?? string.Empty;

        var data = await query
            .Where(x => x.CodPartido == partidoCodigo)
            .Where(x => x.CodCircunscripcion.StartsWith(autonomiaCodigo))
            .Where(x => x.CodCircunscripcion.EndsWith("000") && !x.CodCircunscripcion.EndsWith("00000"))
            .ToListAsync(cancellationToken);

        return OrderCpList(data, oficiales);
    }

    private static List<CircunscripcionPartidoEntity> OrderCpList(
        List<CircunscripcionPartidoEntity> data,
        bool oficiales)
    {
        if (oficiales)
        {
            return data
                .OrderBy(x => x.CodPartido == "99999")
                .ThenByDescending(x => x.EscaniosHasta)
                .ThenByDescending(x => x.Votos)
                .ThenByDescending(x => x.Votantes)
                .ToList();
        }

        return data
            .OrderBy(x => x.CodPartido == "99999")
            .ThenByDescending(x => x.EscaniosHastaSondeo)
            .ThenByDescending(x => x.EscaniosDesdeSondeo)
            .ThenByDescending(x => x.VotosSondeo)
            .ToList();
    }

    private static List<PartidoSnapshot> BuildPartidoSnapshots(
        List<CircunscripcionPartidoEntity> cps,
        SnapshotQuery query,
        bool oficiales,
        bool includeZeroSeats)
    {
        var mapped = cps.Select(x => new PartidoSnapshot(
                x.CodPartido,
                x.Partido != null ? x.Partido.Siglas : x.CodPartido,
                x.EscaniosHasta,
                x.EscaniosDesdeSondeo,
                x.EscaniosHastaSondeo,
                x.EscaniosHistoricos,
                oficiales ? x.Votos : x.VotosSondeo,
                x.Votantes))
            .ToList();

        if (!includeZeroSeats && query.Kind == SnapshotQueryKind.Circunscripcion)
        {
            mapped = mapped
                .Where(x => oficiales ? x.Escanios > 0 : x.EscaniosHastaSondeo > 0)
                .ToList();
        }

        return mapped;
    }

    private static async Task<CircunscripcionEntity> ResolveContextCircunscripcionAsync(
        EleccionesDbContext db,
        SnapshotQuery query,
        List<CircunscripcionPartidoEntity> cps,
        CancellationToken cancellationToken)
    {
        async Task<CircunscripcionEntity?> FindByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return await db.Circunscripciones.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == code, cancellationToken);
        }

        CircunscripcionEntity? circ = query.Kind switch
        {
            SnapshotQueryKind.Circunscripcion => await FindByCodeAsync(query.CircunscripcionCodigo),
            SnapshotQueryKind.MasVotadosAutonomias or SnapshotQueryKind.PartidoAutonomias => await FindByCodeAsync("9900000"),
            SnapshotQueryKind.MasVotadosProvincias or SnapshotQueryKind.PartidoProvincias => await FindByCodeAsync($"{query.AutonomiaCodigo}00000"),
            _ => null
        };

        if (circ is not null)
        {
            return circ;
        }

        var firstCpCirc = cps.FirstOrDefault()?.Circunscripcion;
        if (firstCpCirc is not null)
        {
            return firstCpCirc;
        }

        return await db.Circunscripciones.AsNoTracking().OrderBy(x => x.Nombre).FirstAsync(cancellationToken);
    }

    private static int ResolveAvance(CircunscripcionEntity circ)
    {
        if (circ.Avance3 > 0) return 3;
        if (circ.Avance2 > 0) return 2;
        if (circ.Avance1 > 0) return 1;
        return 0;
    }
}
