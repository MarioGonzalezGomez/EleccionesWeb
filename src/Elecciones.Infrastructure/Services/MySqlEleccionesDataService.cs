using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Csv.Cartones;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Entities;
using Elecciones.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elecciones.Infrastructure.Services;

public sealed class MySqlEleccionesDataService : IEleccionesDataService
{
    private readonly DatabaseOptions _staticDatabaseOptions;
    private readonly IOptionsMonitor<DatabaseOptions>? _databaseMonitor;
    private readonly ILogger<MySqlEleccionesDataService> _logger;
    private readonly IOptionsMonitor<OperationalOptions>? _operationalMonitor;

    public MySqlEleccionesDataService(
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<MySqlEleccionesDataService> logger,
        IOptionsMonitor<OperationalOptions>? operationalMonitor = null,
        IOptionsMonitor<DatabaseOptions>? databaseMonitor = null)
    {
        _staticDatabaseOptions = databaseOptions.Value;
        _databaseMonitor = databaseMonitor;
        _logger = logger;
        _operationalMonitor = operationalMonitor;
    }

    private DatabaseOptions DbOptions => _databaseMonitor?.CurrentValue ?? _staticDatabaseOptions;

    public async Task<IReadOnlyList<CircunscripcionSummary>> GetCircunscripcionesAsync(int electionSlot = 1, int connectionType = 0, CancellationToken cancellationToken = default)
    {
        await using var db = CreateDbContextForElection(electionSlot, connectionType);

        var query = db.Circunscripciones
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.Nombre));

        var regionalCode = ResolveRegionalCode(electionSlot);
        var mustFilterByRegional =
            (!string.IsNullOrWhiteSpace(regionalCode) && DbOptions.TipoElecciones == 2)
            || (!string.IsNullOrWhiteSpace(regionalCode) && DbOptions.Regional);

        if (mustFilterByRegional)
        {
            query = query.Where(x => x.Codigo.StartsWith(regionalCode));
        }

        var rawData = await query
            .Select(x => new CircunscripcionSummary(x.Codigo, x.Nombre))
            .ToListAsync(cancellationToken);

        var data = rawData
            .OrderBy(x => string.Equals(x.Codigo, "9900000", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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
        var partidosCircunscripcionCompletos = await ResolveFullCircunscripcionPartidosAsync(
            db,
            contextCirc.Codigo,
            query.MedioCodigo,
            oficiales,
            cancellationToken);
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
        var (todasLasProvincias, provinciasAutonomia, mayoriasCircunscripciones, sondeoMayoriasCircunscripciones, mapaEscanos, partidosEleccion) = await ResolveMapaYMayoriasAsync(
            db,
            contextCirc,
            query.MedioCodigo,
            oficiales,
            cancellationToken);
        var operational = _operationalMonitor?.CurrentValue ?? new OperationalOptions();

        return new BrainStormSnapshot
        {
            Circunscripcion = new CircunscripcionSummary(contextCirc.Codigo, contextCirc.Nombre),
            ComunidadCircunscripcion = contextCirc.Comunidad?.Trim() ?? string.Empty,
            Oficiales = oficiales,
            Avance = avance,
            Escrutado = contextCirc.Escrutado,
            EscrutadoGeneral = participacion.EscrutadoGeneral,
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
            PartidosCircunscripcionCompletos = partidosCircunscripcionCompletos,
            PartidosEleccion = partidosEleccion,
            CsvTemplatePartidos = csvTemplatePartidos,
            ProvinciasAutonomia = provinciasAutonomia,
            MayoriasCircunscripciones = mayoriasCircunscripciones,
            SondeoMayoriasCircunscripciones = sondeoMayoriasCircunscripciones,
            MapaEscanos = mapaEscanos,
            TodasLasProvinciasMapa = todasLasProvincias,
            VideoSlots = operational.VideoSources
                .Select(v => new VideoSlotSnapshot(v.Slot, v.IsLive, v.Path ?? string.Empty))
                .ToList()
        };
    }

    private static async Task<(
        IReadOnlyList<string> TodasLasProvincias,
        IReadOnlyList<ProvinciaMapaSnapshot> ProvinciasAutonomia,
        IReadOnlyList<CircunscripcionMayoriaSnapshot> MayoriasCircunscripciones,
        IReadOnlyList<CircunscripcionMayoriaSnapshot> SondeoMayoriasCircunscripciones,
        IReadOnlyList<MapaEscanosSnapshot> MapaEscanos,
        IReadOnlyList<PartidoEleccionSummary> PartidosEleccion)> ResolveMapaYMayoriasAsync(
        EleccionesDbContext db,
        CircunscripcionEntity contextCirc,
        string? medioCodigo,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        var allCircs = await db.Circunscripciones
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.Nombre))
            .OrderBy(x => x.Codigo)
            .ToListAsync(cancellationToken);

        var allProvincialCircs = ResolveProvincialCircunscripciones(allCircs);

        var todasLasProvincias = allProvincialCircs
            .Select(x => x.Nombre.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allCps = await db.CircunscripcionPartidos
            .AsNoTracking()
            .Include(x => x.Partido)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(medioCodigo) && !IsRtve(medioCodigo))
        {
            await ApplyMedioSondeoOverlayAsync(db, allCps, medioCodigo.Trim(), cancellationToken);
        }

        var groupedByCirc = allCps
            .GroupBy(x => x.CodCircunscripcion, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var mapaEscanos = allProvincialCircs
            .SelectMany(c =>
            {
                var partidosProvincia = groupedByCirc.GetValueOrDefault(c.Codigo) ?? [];
                var ultimoEscano = partidosProvincia.FirstOrDefault(x => x.EsUltimoEscano != 0);
                var disputaCon = ResolveMapaEscanosDisputaCon(c, partidosProvincia);

                return partidosProvincia
                    .Where(x => x.EscaniosHasta > 0
                        && !string.IsNullOrWhiteSpace(x.CodPartido)
                        && !string.Equals(DatosPartidosCsvBuilder.NormalizePartyCode(x.CodPartido), "99999", StringComparison.Ordinal))
                    .Select(x => new MapaEscanosSnapshot(
                        c.Codigo.Trim(),
                        x.CodPartido.Trim(),
                        x.EscaniosHasta,
                        ultimoEscano is not null
                            && string.Equals(
                                DatosPartidosCsvBuilder.NormalizePartyCode(x.CodPartido),
                                DatosPartidosCsvBuilder.NormalizePartyCode(ultimoEscano.CodPartido),
                                StringComparison.Ordinal)
                                ? disputaCon
                                : string.Empty));
            })
            .OrderBy(x => x.CodigoLugar, StringComparer.Ordinal)
            .ThenBy(x => x.CodigoPartido, StringComparer.Ordinal)
            .ToList();

        var officialWinnerByCirc = groupedByCirc.ToDictionary(
            kvp => kvp.Key,
            kvp => ResolveValidMajorityWinner(kvp.Value, oficiales: true),
            StringComparer.OrdinalIgnoreCase);

        var sondeoWinnerByCirc = groupedByCirc.ToDictionary(
            kvp => kvp.Key,
            kvp => ResolveValidMajorityWinner(kvp.Value, oficiales: false),
            StringComparer.OrdinalIgnoreCase);

        var mayoriasCircunscripciones = allCircs
            .Select(c =>
            {
                var winner = officialWinnerByCirc.GetValueOrDefault(c.Codigo);
                var winnerSeats = winner?.EscaniosHasta ?? 0;
                var diff = winner is null ? 0 : winner.EscaniosHasta - winner.EscaniosHistoricos;
                return new CircunscripcionMayoriaSnapshot(
                    c.Codigo.Trim(),
                    c.Nombre.Trim(),
                    winner?.CodPartido ?? string.Empty,
                    winner?.Partido?.Siglas ?? string.Empty,
                    c.Escanios,
                    winnerSeats,
                    c.Escrutado,
                    c.Participacion,
                    diff);
            })
            .ToList();

        var sondeoMayoriasCircunscripciones = allCircs
            .Select(c =>
            {
                var winner = sondeoWinnerByCirc.GetValueOrDefault(c.Codigo);
                var winnerSeats = winner?.EscaniosHastaSondeo ?? 0;
                var diff = winner is null ? 0 : winner.EscaniosHastaSondeo - winner.EscaniosHistoricos;
                return new CircunscripcionMayoriaSnapshot(
                    c.Codigo.Trim(),
                    c.Nombre.Trim(),
                    winner?.CodPartido ?? string.Empty,
                    winner?.Partido?.Siglas ?? string.Empty,
                    c.Escanios,
                    winnerSeats,
                    c.Escrutado,
                    c.Participacion,
                    diff);
            })
            .ToList();

        var activeWinnerByCirc = oficiales ? officialWinnerByCirc : sondeoWinnerByCirc;

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

        var provinciasAutonomia = targetProvincias
            .Select(p =>
            {
                var winner = activeWinnerByCirc.GetValueOrDefault(p.Codigo);
                var winnerSeats = winner is null ? 0 : (oficiales ? winner.EscaniosHasta : winner.EscaniosHastaSondeo);
                var winnerPct = winner is null ? 0.0 : (oficiales ? winner.Votos : winner.VotosSondeo);
                return new ProvinciaMapaSnapshot(
                    p.Codigo,
                    p.Nombre.Trim(),
                    winner?.Partido?.Siglas ?? string.Empty,
                    winner?.CodPartido ?? string.Empty,
                    p.Escrutado,
                    p.Escanios,
                    p.Participacion,
                    p.Votantes,
                    winnerSeats,
                    winnerPct);
            })
            .ToList();

        var partidosEleccion = allCps
            .Where(x => !string.IsNullOrWhiteSpace(x.CodPartido)
                && !string.Equals(DatosPartidosCsvBuilder.NormalizePartyCode(x.CodPartido), "0", StringComparison.Ordinal))
            .GroupBy(x => DatosPartidosCsvBuilder.NormalizePartyCode(x.CodPartido), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var representative = g.FirstOrDefault(cp => !string.IsNullOrWhiteSpace(cp.Partido?.Siglas)) ?? g.First();
                var formattedCode = DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(representative.CodPartido);
                var siglas = !string.IsNullOrWhiteSpace(representative.Partido?.Siglas)
                    ? representative.Partido.Siglas.Trim()
                    : formattedCode;
                var nombre = representative.Partido?.Nombre?.Trim() ?? string.Empty;
                var padre = !string.IsNullOrWhiteSpace(representative.Partido?.Padre)
                    ? DatosPartidosCsvBuilder.FormatFiveDigitPartyCode(representative.Partido.Padre)
                    : formattedCode;
                return new PartidoEleccionSummary(formattedCode, siglas, nombre, padre);
            })
            .OrderBy(p => DatosPartidosCsvBuilder.ParseSortablePartyCode(p.Codigo))
            .ThenBy(p => DatosPartidosCsvBuilder.NormalizePartyCode(p.Codigo), StringComparer.Ordinal)
            .ToList();

        return (todasLasProvincias, provinciasAutonomia, mayoriasCircunscripciones, sondeoMayoriasCircunscripciones, mapaEscanos, partidosEleccion);
    }

    internal static List<CircunscripcionEntity> ResolveProvincialCircunscripciones(
        IReadOnlyList<CircunscripcionEntity> allCircs)
    {
        var provincialCircs = allCircs
            .Where(x => x.Codigo.EndsWith("000", StringComparison.Ordinal)
                && !x.Codigo.EndsWith("00000", StringComparison.Ordinal))
            .ToList();

        var singleProvinceCommunities = allCircs
            .Where(x => x.Codigo.EndsWith("00000", StringComparison.Ordinal)
                && !string.Equals(x.Codigo, "9900000", StringComparison.Ordinal))
            .Where(community =>
            {
                var prefix = !string.IsNullOrWhiteSpace(community.Comunidad)
                    ? community.Comunidad.Trim()
                    : community.Codigo[..Math.Min(2, community.Codigo.Length)];

                return !provincialCircs.Any(province =>
                    string.Equals(province.Comunidad, prefix, StringComparison.OrdinalIgnoreCase)
                    || province.Codigo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            });

        return provincialCircs
            .Concat(singleProvinceCommunities)
            .DistinctBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Codigo, StringComparer.Ordinal)
            .ToList();
    }

    internal static string ResolveMapaEscanosDisputaCon(
        CircunscripcionEntity circunscripcion,
        IReadOnlyList<CircunscripcionPartidoEntity> partidos)
    {
        var ultimoEscano = partidos.FirstOrDefault(x => x.EsUltimoEscano != 0);
        var partidoEnDisputa = partidos.FirstOrDefault(x => x.LuchaUltimoEscano != 0);
        if (ultimoEscano is null
            || partidoEnDisputa is null
            || string.IsNullOrWhiteSpace(partidoEnDisputa.CodPartido)
            || partidoEnDisputa.Restos >= circunscripcion.VotantesFaltan * 0.025)
        {
            return string.Empty;
        }

        return partidoEnDisputa.CodPartido.Trim();
    }

    private static CircunscripcionPartidoEntity? ResolveValidMajorityWinner(
        List<CircunscripcionPartidoEntity> list,
        bool oficiales)
    {
        var candidate = OrderCpList(list, oficiales)
            .FirstOrDefault(x => !string.Equals(x.CodPartido, "99999", StringComparison.Ordinal));

        if (candidate is null)
        {
            return null;
        }

        var hasActivity = oficiales
            ? (candidate.EscaniosHasta > 0 || candidate.Votos > 0 || candidate.Votantes > 0)
            : (candidate.EscaniosHastaSondeo > 0 || candidate.EscaniosDesdeSondeo > 0 || candidate.VotosSondeo > 0);

        return hasActivity ? candidate : null;
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
        var opts = DbOptions;
        var connectionType = ResolveConnectionType(opts, electionSlot, requestedConnectionType);
        var selectedHost = ResolveHost(opts, connectionType);
        var selectedDatabase = ResolveDatabaseName(opts, electionSlot);
        var password = !string.IsNullOrWhiteSpace(opts.Password)
            ? opts.Password
            : (Environment.GetEnvironmentVariable("ELECCIONES_DB_PASSWORD") ?? string.Empty);

        var connectionString =
            $"server={selectedHost};port={opts.Port};uid={opts.User};pwd={password};database={selectedDatabase};";

        var builder = new DbContextOptionsBuilder<EleccionesDbContext>();
        builder.UseMySQL(connectionString, mysql =>
        {
            mysql.CommandTimeout(Math.Max(2, opts.CommandTimeoutSeconds));
        });

        return new EleccionesDbContext(builder.Options);
    }

    private static int ResolveConnectionType(DatabaseOptions opts, int electionSlot, int requestedConnectionType)
    {
        if (requestedConnectionType is >= 1 and <= 3)
        {
            return requestedConnectionType;
        }

        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;
        var configured = slot switch
        {
            2 => opts.ConnectionDefault2,
            3 => opts.ConnectionDefault3,
            _ => opts.ConnectionDefault1
        };

        return configured is >= 1 and <= 3 ? configured : 1;
    }

    private static string ResolveHost(DatabaseOptions opts, int connectionType)
    {
        return connectionType switch
        {
            2 when !string.IsNullOrWhiteSpace(opts.HostBackup) => opts.HostBackup.Trim(),
            3 when !string.IsNullOrWhiteSpace(opts.HostLocal) => opts.HostLocal.Trim(),
            3 => "127.0.0.1",
            _ => opts.Host.Trim()
        };
    }

    private string ResolveRegionalCode(int electionSlot)
    {
        var opts = DbOptions;
        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;
        var raw = slot switch
        {
            2 => opts.CodigoRegionalBD2,
            3 => opts.CodigoRegionalBD3,
            _ => opts.CodigoRegionalBD1
        };

        return raw?.Trim() ?? string.Empty;
    }

    private static async Task<IReadOnlyList<PartidoSnapshot>> ResolveFullCircunscripcionPartidosAsync(
        EleccionesDbContext db,
        string circunscripcionCodigo,
        string? medioCodigo,
        bool oficiales,
        CancellationToken cancellationToken)
    {
        var targetCode = string.IsNullOrWhiteSpace(circunscripcionCodigo) ? "9900000" : circunscripcionCodigo.Trim();
        var allCps = await db.CircunscripcionPartidos
            .AsNoTracking()
            .Include(x => x.Partido)
            .Include(x => x.Circunscripcion)
            .Where(x => x.CodCircunscripcion == targetCode)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(medioCodigo) && !IsRtve(medioCodigo))
        {
            await ApplyMedioSondeoOverlayAsync(db, allCps, medioCodigo.Trim(), cancellationToken);
        }

        return BuildPartidoSnapshots(
            OrderCpList(allCps, oficiales: true),
            new SnapshotQuery { Kind = SnapshotQueryKind.Circunscripcion, IncludeZeroSeats = true },
            oficiales: true,
            includeZeroSeats: true);
    }

    private async Task<(double Actual, double Historica, double Media, double EscrutadoGeneral)> ResolveParticipacionDataAsync(
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
        var escrutadoGral = mediaFallback.Escrutado;

        return avance switch
        {
            1 => (contextCirc.Avance1, contextCirc.Avance1Hist, mediaFallback.Avance1, escrutadoGral),
            2 => (contextCirc.Avance2, contextCirc.Avance2Hist, mediaFallback.Avance2, escrutadoGral),
            3 => (contextCirc.Avance3, contextCirc.Avance3Hist, mediaFallback.Avance3, escrutadoGral),
            _ => (contextCirc.Participacion, contextCirc.ParticipacionHist, mediaFallback.Participacion, escrutadoGral)
        };
    }

    private string ResolveParentCircunscripcionCode(CircunscripcionEntity contextCirc, int electionSlot)
    {
        if (DbOptions.TipoElecciones == 1 && !DbOptions.Regional)
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

    private static string ResolveDatabaseName(DatabaseOptions opts, int electionSlot)
    {
        var slot = electionSlot is >= 1 and <= 3 ? electionSlot : 1;

        var db1 = opts.Name?.Trim() ?? string.Empty;
        var db2 = opts.Name2?.Trim() ?? string.Empty;
        var db3 = opts.Name3?.Trim() ?? string.Empty;

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


