using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Entities;
using Elecciones.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elecciones.Infrastructure.Services;

public sealed class MySqlEleccionesDataService : IEleccionesDataService
{
    private readonly DatabaseOptions _databaseOptions;
    private readonly ILogger<MySqlEleccionesDataService> _logger;
    private readonly IOptionsMonitor<OperationalOptions>? _operationalMonitor;

    public MySqlEleccionesDataService(
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<MySqlEleccionesDataService> logger,
        IOptionsMonitor<OperationalOptions>? operationalMonitor = null)
    {
        _databaseOptions = databaseOptions.Value;
        _logger = logger;
        _operationalMonitor = operationalMonitor;
    }

    public async Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(int electionSlot = 1, int connectionType = 0, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContextForElection(electionSlot, connectionType);

        var query = db.Circunscripciones
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.Nombre));

        var regionalCode = ResolveRegionalCode(electionSlot);
        var mustFilterByRegional =
            (!string.IsNullOrWhiteSpace(regionalCode) && _databaseOptions.TipoElecciones == 2)
            || (!string.IsNullOrWhiteSpace(regionalCode) && _databaseOptions.Regional);

        if (mustFilterByRegional)
        {
            query = query.Where(x => x.Codigo.StartsWith(regionalCode));
        }

        var data = await query
            .OrderBy(x => x.Nombre)
            .Select(x => new CircunscripcionSummary(x.Codigo, x.Nombre))
            .ToListAsync(cancellationToken);

        return data;
    }

    public async Task<IReadOnlyList<MedioSummary>> GetMediosAsync(int electionSlot = 1, int connectionType = 0, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContextForElection(electionSlot, connectionType);

        var data = await db.Medios
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.Codigo))
            .OrderBy(x => x.Descripcion)
            .ThenBy(x => x.Codigo)
            .Select(x => new MedioSummary(
                x.Codigo,
                string.IsNullOrWhiteSpace(x.Descripcion) ? x.Codigo : x.Descripcion))
            .ToListAsync(cancellationToken);

        return data;
    }

    public async Task<BrainStormSnapshot> GetSnapshotAsync(
        SnapshotQuery query,
        bool oficiales,
        CancellationToken cancellationToken = default)
    {
        query ??= new SnapshotQuery();

        await using var db = CreateDbContextForElection(query.ElectionSlot, query.ConnectionType);

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

        if (!oficiales && !string.IsNullOrWhiteSpace(query.MedioCodigo) && !IsRtve(query.MedioCodigo))
        {
            await ApplyMedioSondeoOverlayAsync(db, cps, query.MedioCodigo.Trim(), cancellationToken);
        }

        var contextCirc = await ResolveContextCircunscripcionAsync(db, query, cps, cancellationToken);
        var partidos = BuildPartidoSnapshots(cps, query, oficiales, query.IncludeZeroSeats);
        var csvTemplatePartidos = await ResolveCsvTemplatePartidosAsync(
            db,
            query,
            contextCirc,
            oficiales,
            cancellationToken);

        _logger.LogDebug(
            "Snapshot loaded from MySQL. Kind={Kind}, Circ={Circ}, Count={Count}",
            query.Kind,
            contextCirc.Codigo,
            partidos.Count);

        var avance = ResolveAvance(contextCirc);
        if (query.Avance is >= 1 and <= 4)
        {
            avance = query.Avance.Value;
        }
        var participacion = await ResolveParticipacionDataAsync(db, contextCirc, avance, query.ElectionSlot, cancellationToken);
        var (todasLasProvincias, provinciasAutonomia) = await ResolveMapaProvinciasAsync(db, contextCirc, oficiales, cancellationToken);
        var operational = _operationalMonitor?.CurrentValue ?? new OperationalOptions();

        return new BrainStormSnapshot
        {
            Circunscripcion = new CircunscripcionSummary(contextCirc.Codigo, contextCirc.Nombre),
            Oficiales = oficiales,
            Avance = avance,
            Escrutado = contextCirc.Escrutado,
            Participacion = participacion.Actual,
            ParticipacionHistorica = participacion.Historica,
            ParticipacionMedia = participacion.Media,
            Avance1 = contextCirc.Avance1,
            Avance2 = contextCirc.Avance2,
            Avance3 = contextCirc.Avance3,
            Avance1Historico = contextCirc.Avance1Hist,
            Avance2Historico = contextCirc.Avance2Hist,
            Avance3Historico = contextCirc.Avance3Hist,
            HoraAvance1 = operational.Hours.HoraAvance1,
            HoraAvance2 = operational.Hours.HoraAvance2,
            HoraAvance3 = operational.Hours.HoraAvance3,
            HoraParticipacion = operational.Hours.HoraParticipacion,
            HoraAvance1Historico = operational.Hours.HoraAvance1Historico,
            HoraAvance2Historico = operational.Hours.HoraAvance2Historico,
            HoraAvance3Historico = operational.Hours.HoraAvance3Historico,
            HoraParticipacionHistorico = operational.Hours.HoraParticipacionHistorico,
            EscaniosTotales = contextCirc.Escanios,
            MayoriaAbsoluta = contextCirc.Escanios <= 0 ? 0 : (contextCirc.Escanios / 2) + 1,
            VotantesTotales = contextCirc.Votantes,
            VotantesFaltan = contextCirc.VotantesFaltan,
            AnioUltimasElecciones = DateTime.UtcNow.Year - 3,
            NumPartidos = partidos.Count(p => oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0),
            NombreSondeo = query.MedioCodigo ?? string.Empty,
            GeneratedAtUtc = DateTime.UtcNow,
            Partidos = partidos,
            CsvTemplatePartidos = csvTemplatePartidos,
            ProvinciasAutonomia = provinciasAutonomia,
            TodasLasProvinciasMapa = todasLasProvincias,
            VideoSlots = operational.VideoSources
                .Select(v => new VideoSlotSnapshot(v.Slot, v.IsLive, v.Path ?? string.Empty))
                .ToList()
        };
    }

    private static async Task<(IReadOnlyList<string> TodasLasProvincias, IReadOnlyList<ProvinciaMapaSnapshot> ProvinciasAutonomia)> ResolveMapaProvinciasAsync(
        EleccionesDbContext db,
        CircunscripcionEntity contextCirc,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        var allProvincialCircs = await db.Circunscripciones
            .AsNoTracking()
            .Where(x => x.Codigo.EndsWith("000") && !x.Codigo.EndsWith("00000") && !string.IsNullOrWhiteSpace(x.Nombre))
            .OrderBy(x => x.Codigo)
            .ToListAsync(cancellationToken);

        var todasLasProvincias = allProvincialCircs
            .Select(x => x.Nombre.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<CircunscripcionEntity> targetProvincias;
        if (contextCirc.Codigo.EndsWith("00000", StringComparison.Ordinal))
        {
            if (contextCirc.Codigo.StartsWith("99", StringComparison.Ordinal))
            {
                targetProvincias = allProvincialCircs;
            }
            else
            {
                var prefix = !string.IsNullOrWhiteSpace(contextCirc.Comunidad)
                    ? contextCirc.Comunidad.Trim()
                    : contextCirc.Codigo[..Math.Min(2, contextCirc.Codigo.Length)];
                targetProvincias = allProvincialCircs
                    .Where(x => string.Equals(x.Comunidad, prefix, StringComparison.OrdinalIgnoreCase)
                        || x.Codigo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        else
        {
            targetProvincias = [contextCirc];
        }

        if (targetProvincias.Count == 0)
        {
            return (todasLasProvincias, []);
        }

        var targetCodes = targetProvincias.Select(x => x.Codigo).ToList();
        var cpsProvincias = await db.CircunscripcionPartidos
            .AsNoTracking()
            .Include(x => x.Partido)
            .Where(x => targetCodes.Contains(x.CodCircunscripcion))
            .ToListAsync(cancellationToken);

        var winnerByCirc = cpsProvincias
            .GroupBy(x => x.CodCircunscripcion, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => OrderCpList(g.ToList(), oficiales).FirstOrDefault()?.Partido?.Siglas ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var provinciasAutonomia = targetProvincias
            .Select(p => new ProvinciaMapaSnapshot(
                p.Codigo,
                p.Nombre.Trim(),
                winnerByCirc.GetValueOrDefault(p.Codigo, string.Empty)))
            .ToList();

        return (todasLasProvincias, provinciasAutonomia);
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
                x.Partido?.Padre ?? string.Empty,
                x.Partido?.Siglas ?? x.CodPartido,
                x.Partido?.Nombre ?? string.Empty,
                x.Partido?.Candidato ?? string.Empty,
                x.EscaniosHasta,
                x.EscaniosDesdeSondeo,
                x.EscaniosHastaSondeo,
                x.EscaniosHistoricos,
                oficiales ? x.Votos : x.VotosSondeo,
                x.VotosHist,
                x.Votantes,
                x.VotantesHist,
                x.Restos,
                x.EsUltimoEscano,
                x.LuchaUltimoEscano))
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

    private static async Task<IReadOnlyList<PartidoSnapshot>> ResolveCsvTemplatePartidosAsync(
        EleccionesDbContext db,
        SnapshotQuery query,
        CircunscripcionEntity contextCirc,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        if (query.Kind != SnapshotQueryKind.Circunscripcion)
        {
            return [];
        }

        var contextCode = contextCirc.Codigo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(contextCode))
        {
            return [];
        }

        // Si ya estamos en la circunscripcion general, no hace falta plantilla adicional.
        if (contextCode.EndsWith("00000", StringComparison.Ordinal))
        {
            return [];
        }

        var candidateTemplateCodes = new List<string>();
        var comunidad = contextCirc.Comunidad?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(comunidad))
        {
            candidateTemplateCodes.Add($"{comunidad}00000");
        }

        candidateTemplateCodes.Add("9900000");

        foreach (var templateCode in candidateTemplateCodes.Distinct(StringComparer.Ordinal))
        {
            var cpsPlantilla = await db.CircunscripcionPartidos
                .AsNoTracking()
                .Include(x => x.Partido)
                .Where(x => x.CodCircunscripcion == templateCode)
                .ToListAsync(cancellationToken);

            if (cpsPlantilla.Count == 0)
            {
                continue;
            }

            var partidosPlantilla = BuildPartidoSnapshots(
                    OrderCpList(cpsPlantilla, oficiales),
                    new SnapshotQuery { Kind = SnapshotQueryKind.Circunscripcion, IncludeZeroSeats = true },
                    oficiales,
                    includeZeroSeats: true)
                .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
                .OrderBy(p => p.Codigo, StringComparer.Ordinal)
                .ToList();

            if (partidosPlantilla.Count > 0)
            {
                return partidosPlantilla;
            }
        }

        return [];
    }

    private EleccionesDbContext CreateDbContextForElection(int electionSlot, int requestedConnectionType)
    {
        var connectionType = ResolveConnectionType(electionSlot, requestedConnectionType);
        var selectedHost = ResolveHost(connectionType);
        var selectedDatabase = ResolveDatabaseName(electionSlot);

        var connectionString =
            $"server={selectedHost};port={_databaseOptions.Port};uid={_databaseOptions.User};pwd={_databaseOptions.Password};database={selectedDatabase};";

        var builder = new DbContextOptionsBuilder<EleccionesDbContext>();
        builder.UseMySQL(connectionString, mysql =>
        {
            mysql.CommandTimeout(_databaseOptions.CommandTimeoutSeconds);
        });

        return new EleccionesDbContext(builder.Options);
    }

    private int ResolveConnectionType(int electionSlot, int requestedConnectionType)
    {
        if (requestedConnectionType is >= 1 and <= 3)
        {
            return requestedConnectionType;
        }

        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;
        var configured = slot switch
        {
            2 => _databaseOptions.ConnectionDefault2,
            3 => _databaseOptions.ConnectionDefault3,
            _ => _databaseOptions.ConnectionDefault1
        };

        return configured is >= 1 and <= 3 ? configured : 1;
    }

    private string ResolveHost(int connectionType)
    {
        return connectionType switch
        {
            2 when !string.IsNullOrWhiteSpace(_databaseOptions.HostBackup) => _databaseOptions.HostBackup.Trim(),
            3 when !string.IsNullOrWhiteSpace(_databaseOptions.HostLocal) => _databaseOptions.HostLocal.Trim(),
            3 => "127.0.0.1",
            _ => _databaseOptions.Host.Trim()
        };
    }

    private string ResolveRegionalCode(int electionSlot)
    {
        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;
        var raw = slot switch
        {
            2 => _databaseOptions.CodigoRegionalBD2,
            3 => _databaseOptions.CodigoRegionalBD3,
            _ => _databaseOptions.CodigoRegionalBD1
        };

        return raw?.Trim() ?? string.Empty;
    }

    private async Task<(double Actual, double Historica, double Media)> ResolveParticipacionDataAsync(
        EleccionesDbContext db,
        CircunscripcionEntity contextCirc,
        int avance,
        int electionSlot,
        CancellationToken cancellationToken)
    {
        var parentCode = ResolveParentCircunscripcionCode(contextCirc, electionSlot);
        CircunscripcionEntity? parent = null;
        if (!string.IsNullOrWhiteSpace(parentCode))
        {
            parent = await db.Circunscripciones
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codigo == parentCode, cancellationToken);
        }

        var mediaFallback = parent ?? contextCirc;

        return avance switch
        {
            1 => (contextCirc.Avance1, contextCirc.Avance1Hist, mediaFallback.Avance1),
            2 => (contextCirc.Avance2, contextCirc.Avance2Hist, mediaFallback.Avance2),
            3 => (contextCirc.Avance3, contextCirc.Avance3Hist, mediaFallback.Avance3),
            _ => (contextCirc.Participacion, contextCirc.ParticipacionHist, mediaFallback.Participacion)
        };
    }

    private string ResolveParentCircunscripcionCode(CircunscripcionEntity contextCirc, int electionSlot)
    {
        if (_databaseOptions.TipoElecciones == 1)
        {
            return "9900000";
        }

        if (!string.IsNullOrWhiteSpace(contextCirc.Comunidad))
        {
            return $"{contextCirc.Comunidad.Trim()}00000";
        }

        var regionalCode = ResolveRegionalCode(electionSlot);
        return string.IsNullOrWhiteSpace(regionalCode) ? "9900000" : $"{regionalCode}00000";
    }

    private string ResolveDatabaseName(int electionSlot)
    {
        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;

        var db1 = _databaseOptions.Name?.Trim() ?? string.Empty;
        var db2 = _databaseOptions.Name2?.Trim() ?? string.Empty;
        var db3 = _databaseOptions.Name3?.Trim() ?? string.Empty;

        return slot switch
        {
            2 when !string.IsNullOrWhiteSpace(db2) => db2,
            3 when !string.IsNullOrWhiteSpace(db3) => db3,
            3 when !string.IsNullOrWhiteSpace(db2) => db2,
            _ => db1
        };
    }

    private static int ResolveAvance(CircunscripcionEntity circ)
    {
        if (circ.Avance3 > 0) return 3;
        if (circ.Avance2 > 0) return 2;
        if (circ.Avance1 > 0) return 1;
        return 0;
    }

    private static bool IsRtve(string medioCodigo)
    {
        return string.Equals(medioCodigo.Trim(), "RTVE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ApplyMedioSondeoOverlayAsync(
        EleccionesDbContext db,
        List<CircunscripcionPartidoEntity> cps,
        string medioCodigo,
        CancellationToken cancellationToken)
    {
        if (cps.Count == 0)
        {
            return;
        }

        var circCodes = cps
            .Select(x => x.CodCircunscripcion)
            .Distinct()
            .ToList();

        var partidoCodes = cps
            .Select(x => x.CodPartido)
            .Distinct()
            .ToList();

        var overlays = await db.MedioPartidos
            .AsNoTracking()
            .Where(x => x.CodMedio == medioCodigo)
            .Where(x => circCodes.Contains(x.CodCircunscripcion))
            .Where(x => partidoCodes.Contains(x.CodPartido))
            .ToListAsync(cancellationToken);

        var overlayMap = overlays.ToDictionary(
            x => (x.CodCircunscripcion, x.CodPartido),
            x => x);

        foreach (var cp in cps)
        {
            if (overlayMap.TryGetValue((cp.CodCircunscripcion, cp.CodPartido), out var overlay))
            {
                cp.EscaniosDesdeSondeo = overlay.EscaniosDesde;
                cp.EscaniosHastaSondeo = overlay.EscaniosHasta;
                cp.VotosSondeo = overlay.Votos;
                continue;
            }

            cp.EscaniosDesdeSondeo = 0;
            cp.EscaniosHastaSondeo = 0;
            cp.VotosSondeo = 0;
        }
    }
}


