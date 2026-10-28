using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elecciones.Infrastructure.Services;

public sealed class FileBrainStormCsvWriter : IBrainStormCsvWriter
{
    private const string CsvFormatOld = "OLD";
    private const string BrainStormCodigoFileName = "Brainstorm_Codigo.csv";
    private const string BrainStormSondeoCodigoFileName = "Brainstorm_Sondeo_Codigo.csv";

    private readonly StorageOptions _options;
    private readonly ILogger<FileBrainStormCsvWriter> _logger;
    private static readonly JsonSerializerOptions RecuentosJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultBufferSize = 1024
    };

    public FileBrainStormCsvWriter(IOptions<StorageOptions> options, ILogger<FileBrainStormCsvWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        BrainStormSnapshot snapshot,
        string exportName,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null)
    {
        var folderPath = ResolveCsvFolder(_options.ResolveCsvFolderForModule(module));
        Directory.CreateDirectory(folderPath);

        var safeName = string.IsNullOrWhiteSpace(exportName) ? "BrainStorm" : exportName.Trim();
        var filePath = Path.Combine(folderPath, $"{safeName}.csv");
        var isOldFormat = IsOldFormat(_options.BrainStormCsvFormat);
        var rows = isOldFormat
            ? BuildLegacyRows(snapshot)
            : BuildCodeAlignedRows(snapshot);
        var csv = BuildCsv(snapshot, rows, includeEscaniosColumn: !isOldFormat);

        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8, cancellationToken);

        _logger.LogInformation("BrainStorm CSV generated at {Path}", filePath);

        if (!isOldFormat)
        {
            return filePath;
        }

        var csvCodigoPath = Path.Combine(folderPath, BrainStormCodigoFileName);
        var csvCodigo = BuildCsv(snapshot, BuildCodeAlignedRows(snapshot), includeEscaniosColumn: true);
        await File.WriteAllTextAsync(csvCodigoPath, csvCodigo, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("BrainStorm codigo CSV generated at {Path}", csvCodigoPath);

        if (!snapshot.Oficiales)
        {
            var csvSondeoPath = Path.Combine(folderPath, BrainStormSondeoCodigoFileName);
            await File.WriteAllTextAsync(csvSondeoPath, csv, Encoding.UTF8, cancellationToken);
            _logger.LogInformation("BrainStorm sondeo codigo CSV generated at {Path}", csvSondeoPath);
        }

        return filePath;
    }

    public async Task<string?> WriteSedesAsync(
        BrainStormSnapshot snapshot,
        string partyToken,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null)
    {
        if (snapshot.Partidos.Count == 0 || string.IsNullOrWhiteSpace(partyToken))
        {
            return null;
        }

        var partido = ResolveParty(snapshot.Partidos, partyToken);
        if (partido is null)
        {
            return null;
        }

        var folderPath = ResolveCsvFolder(_options.ResolveCsvFolderForModule(module));
        Directory.CreateDirectory(folderPath);
        var filePath = Path.Combine(folderPath, "Sedes.csv");

        var difEscRaw = partido.EscaniosHistoricos == 0
            ? 0
            : partido.Escanios - partido.EscaniosHistoricos;
        var tendenciaEsc = partido.EscaniosHistoricos == 0
            ? "*"
            : difEscRaw > 0 ? "+" : difEscRaw == 0 ? "=" : "-";
        var difEsc = Math.Abs(difEscRaw);

        var difVotosRaw = partido.Votos - partido.VotantesHistoricos;
        var tendenciaVotos = partido.VotantesHistoricos == 0
            ? "*"
            : difVotosRaw > 0 ? "+" : difVotosRaw == 0 ? "=" : "-";
        var difVotos = Math.Abs(difVotosRaw);

        var porcentajeTruncado = Math.Truncate(partido.PorcentajeVoto * 10d) / 10d;
        var csv = new StringBuilder();
        csv.AppendLine("Codigo;Padre;Siglas;Candidato;Escanios;Historicos;% Voto;Votantes;Diferencia de escanios;Tendencia;Diferencia de votos;Tendencia");
        csv.Append(partido.Codigo).Append(';')
            .Append(partido.Padre).Append(';')
            .Append(partido.Siglas).Append(';')
            .Append(partido.Candidato).Append(';')
            .Append(partido.Escanios).Append(';')
            .Append(partido.EscaniosHistoricos).Append(';')
            .Append(porcentajeTruncado.ToString(CultureInfo.InvariantCulture)).Append(';')
            .Append(partido.Votos).Append(';')
            .Append(difEsc).Append(';')
            .Append(tendenciaEsc).Append(';')
            .Append(difVotos).Append(';')
            .Append(tendenciaVotos)
            .AppendLine();

        await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Sedes CSV generated at {Path}", filePath);
        return filePath;
    }

    public async Task<string?> WriteRecuentosJsonAsync(
        BrainStormSnapshot snapshot,
        CancellationToken cancellationToken = default,
        GraphicModule? module = null)
    {
        if (!_options.EnableRecuentosJson)
        {
            return null;
        }

        var folderPath = ResolveCsvFolder(_options.ResolveJsonFolderForModule(module));
        Directory.CreateDirectory(folderPath);

        var safeName = string.IsNullOrWhiteSpace(_options.RecuentosJsonFileName)
            ? "datos_escrutado"
            : _options.RecuentosJsonFileName.Trim();
        var filePath = Path.Combine(folderPath, $"{safeName}.json");

        var payload = BuildRecuentosPayload(snapshot);
        var json = JsonSerializer.Serialize(payload, RecuentosJsonOptions);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);

        _logger.LogInformation("Recuentos JSON generated at {Path}", filePath);
        return filePath;
    }

    private static string ResolveCsvFolder(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "Data/CSV";
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    private static string BuildCsv(
        BrainStormSnapshot snapshot,
        IReadOnlyList<PartidoSnapshot?> filasCsv,
        bool includeEscaniosColumn)
    {
        var partidosActuales = snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => p.Codigo, StringComparer.Ordinal)
            .ToList();

        var ultimo = partidosActuales.FirstOrDefault(p => p.EsUltimoEscano != 0);
        var siguiente = partidosActuales.FirstOrDefault(p => p.LuchaUltimoEscano != 0);
        var resto = siguiente?.RestoVotos ?? 0;

        var numeroPartidos = snapshot.NumPartidos > 0
            ? snapshot.NumPartidos
            : partidosActuales.Count(p => snapshot.Oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0);

        var sb = new StringBuilder();

        sb.AppendLine("Codigo;Nombre;Escrutado;Escanios;Mayoria;Avance;Participacion;Participacion Historica;Media de Participacion;Votantes;Ultimas Elecciones;Numero de partidos;Ultimo;Siguiente;Resto;NombreSondeo;Vot.Faltan");
        sb.Append(snapshot.Circunscripcion.Codigo).Append(';')
            .Append(snapshot.Circunscripcion.Nombre).Append(';')
            .Append(snapshot.Escrutado.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
            .Append(snapshot.EscaniosTotales).Append(';')
            .Append(snapshot.MayoriaAbsoluta).Append(';')
            .Append(snapshot.Avance).Append(';')
            .Append(snapshot.Participacion.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
            .Append(snapshot.ParticipacionHistorica.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
            .Append(snapshot.ParticipacionMedia.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
            .Append(snapshot.VotantesTotales).Append(';')
            .Append(snapshot.AnioUltimasElecciones).Append(';')
            .Append(numeroPartidos).Append(';')
            .Append(ultimo?.Siglas ?? string.Empty).Append(';')
            .Append(siguiente?.Siglas ?? string.Empty).Append(';')
            .Append(resto).Append(';')
            .Append(snapshot.NombreSondeo ?? string.Empty).Append(';')
            .Append(snapshot.VotantesFaltan)
            .AppendLine();

        var cabeceraPartidos = includeEscaniosColumn
            ? "Codigo;Padre;Siglas;Candidato;Escanios;Escanios Desde Sondeo;Hasta Sondeo;Historicos;% Voto;Votantes;Diferencia de escanios;Tendencia;Diferencia de votos;Tendencia;Votantes Historico;Nombre"
            : "Codigo;Padre;Siglas;Candidato;Escanios Desde Sondeo;Hasta Sondeo;Historicos;% Voto;Votantes;Diferencia de escanios;Tendencia;Diferencia de votos;Tendencia;Votantes Historico;Nombre";
        sb.AppendLine(cabeceraPartidos);

        foreach (var partido in filasCsv)
        {
            if (partido is null)
            {
                var columnas = includeEscaniosColumn ? 16 : 15;
                sb.AppendLine(string.Join(';', Enumerable.Repeat(string.Empty, columnas)));
                continue;
            }

            var escaniosReferencia = snapshot.Oficiales ? partido.Escanios : partido.EscaniosHastaSondeo;
            var difEscRaw = partido.EscaniosHistoricos == 0
                ? escaniosReferencia
                : escaniosReferencia - partido.EscaniosHistoricos;
            var tendenciaEsc = partido.EscaniosHistoricos == 0
                ? "*"
                : difEscRaw > 0 ? "+" : difEscRaw == 0 ? "=" : "-";
            var difEsc = Math.Abs(difEscRaw);

            var difVotosRaw = partido.Votos - partido.VotantesHistoricos;
            var tendenciaVotos = partido.VotantesHistoricos == 0
                ? "*"
                : difVotosRaw > 0 ? "+" : difVotosRaw == 0 ? "=" : "-";
            var difVotos = Math.Abs(difVotosRaw);

            var porcentajeTruncado = Math.Truncate(partido.PorcentajeVoto * 10d) / 10d;
            var escaniosDesdeCsv = snapshot.Oficiales ? partido.Escanios : partido.EscaniosDesdeSondeo;
            var escaniosHastaCsv = snapshot.Oficiales ? partido.Escanios : partido.EscaniosHastaSondeo;

            sb.Append(partido.Codigo).Append(';')
                .Append(partido.Padre).Append(';')
                .Append(partido.Siglas).Append(';')
                .Append(partido.Candidato).Append(';');

            if (includeEscaniosColumn)
            {
                sb.Append(partido.Escanios).Append(';')
                    .Append(partido.EscaniosDesdeSondeo).Append(';')
                    .Append(partido.EscaniosHastaSondeo).Append(';');
            }
            else
            {
                sb.Append(escaniosDesdeCsv).Append(';')
                    .Append(escaniosHastaCsv).Append(';');
            }

            sb.Append(partido.EscaniosHistoricos).Append(';')
                .Append(porcentajeTruncado.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(partido.Votos).Append(';')
                .Append(difEsc).Append(';')
                .Append(tendenciaEsc).Append(';')
                .Append(difVotos).Append(';')
                .Append(tendenciaVotos).Append(';')
                .Append(partido.VotantesHistoricos).Append(';')
                .Append(partido.Nombre)
                .AppendLine();
        }

        return sb.ToString();
    }

    private static bool IsOldFormat(string? configuredFormat)
    {
        return string.Equals(configuredFormat?.Trim(), CsvFormatOld, StringComparison.OrdinalIgnoreCase);
    }

    private static List<PartidoSnapshot?> BuildCodeAlignedRows(BrainStormSnapshot snapshot)
    {
        var partidosActuales = snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => p.Codigo, StringComparer.Ordinal)
            .ToList();

        var plantilla = snapshot.CsvTemplatePartidos.Count > 0
            ? snapshot.CsvTemplatePartidos
                .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
                .OrderBy(p => p.Codigo, StringComparer.Ordinal)
                .ToList()
            : partidosActuales;

        var actualesPorCodigo = partidosActuales
            .GroupBy(p => NormalizeCode(p.Codigo), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var codigosPlantilla = new HashSet<string>(StringComparer.Ordinal);
        var filasCsv = new List<PartidoSnapshot?>(plantilla.Count);

        foreach (var partidoPlantilla in plantilla)
        {
            var codigoPlantilla = NormalizeCode(partidoPlantilla.Codigo);
            codigosPlantilla.Add(codigoPlantilla);
            filasCsv.Add(actualesPorCodigo.TryGetValue(codigoPlantilla, out var actual) ? actual : null);
        }

        foreach (var partidoActual in partidosActuales)
        {
            if (!codigosPlantilla.Contains(NormalizeCode(partidoActual.Codigo)))
            {
                filasCsv.Add(partidoActual);
            }
        }

        return filasCsv;
    }

    private static List<PartidoSnapshot?> BuildLegacyRows(BrainStormSnapshot snapshot)
    {
        var partidos = snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .Where(p => snapshot.Oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0)
            .ToList();

        partidos.Sort((x, y) => CompareLegacyAscending(x, y, snapshot.Oficiales));
        partidos.Reverse();

        return partidos.Cast<PartidoSnapshot?>().ToList();
    }

    private static int CompareLegacyAscending(PartidoSnapshot x, PartidoSnapshot y, bool oficiales)
    {
        var xEsOtros = string.Equals(x.Codigo, "99999", StringComparison.Ordinal);
        var yEsOtros = string.Equals(y.Codigo, "99999", StringComparison.Ordinal);
        if (xEsOtros && yEsOtros)
        {
            return 0;
        }

        var xEscanios = oficiales ? x.Escanios : x.EscaniosHastaSondeo;
        var yEscanios = oficiales ? y.Escanios : y.EscaniosHastaSondeo;
        var xTieneRepresentacion = xEscanios > 0;
        var yTieneRepresentacion = yEscanios > 0;

        if (xEsOtros)
        {
            if (xTieneRepresentacion)
            {
                return yTieneRepresentacion ? -1 : 1;
            }

            return -1;
        }

        if (yEsOtros)
        {
            if (yTieneRepresentacion)
            {
                return xTieneRepresentacion ? 1 : -1;
            }

            return 1;
        }

        int comp;
        if (oficiales)
        {
            comp = x.Escanios.CompareTo(y.Escanios);
            if (comp == 0)
            {
                comp = x.PorcentajeVoto.CompareTo(y.PorcentajeVoto);
                if (comp == 0)
                {
                    comp = x.Votos.CompareTo(y.Votos);
                }
            }
        }
        else
        {
            comp = x.EscaniosHastaSondeo.CompareTo(y.EscaniosHastaSondeo);
            if (comp == 0)
            {
                comp = x.EscaniosDesdeSondeo.CompareTo(y.EscaniosDesdeSondeo);
                if (comp == 0)
                {
                    comp = x.PorcentajeVoto.CompareTo(y.PorcentajeVoto);
                    if (comp == 0)
                    {
                        comp = x.EscaniosHistoricos.CompareTo(y.EscaniosHistoricos);
                    }
                }
            }
        }

        return comp;
    }

    private static PartidoSnapshot? ResolveParty(IReadOnlyList<PartidoSnapshot> partidos, string partyToken)
    {
        var token = partyToken.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return partidos.FirstOrDefault(p =>
                   string.Equals(p.Codigo, token, StringComparison.OrdinalIgnoreCase))
            ?? partidos.FirstOrDefault(p =>
                   string.Equals(NormalizeCode(p.Codigo), NormalizeCode(token), StringComparison.OrdinalIgnoreCase))
            ?? partidos.FirstOrDefault(p =>
                   string.Equals(p.Siglas, token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<object> BuildRecuentosPayload(BrainStormSnapshot snapshot)
    {
        var partidos = snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .ToList();

        var partidosConEscano = partidos.Count(p =>
            snapshot.Oficiales ? p.Escanios > 0 : p.EscaniosHastaSondeo > 0);

        var recuentos = partidos
            .OrderBy(p => p.Codigo, StringComparer.Ordinal)
            .Select((p, index) =>
            {
                var escanosHasta = snapshot.Oficiales ? p.Escanios : p.EscaniosHastaSondeo;
                var difEscanos = escanosHasta - p.EscaniosHistoricos;
                var flechaEscanos = ToArrow(difEscanos);

                var difVotantes = p.Votos - p.VotantesHistoricos;
                var flechaVotantes = p.VotantesHistoricos == 0 ? 1 : ToArrow(difVotantes);

                return new
                {
                    BLOQUE = $"Partido_{index + 1}",
                    PARTIDO = p.Nombre,
                    SIGLA_OFICIAL = p.Siglas,
                    CODIGO = p.Codigo,
                    ORDEN = index + 1,
                    ESCANOS_DESDE = p.EscaniosDesdeSondeo,
                    ESCANOS_HASTA = escanosHasta,
                    DIF_ESCANOS = Math.Abs(difEscanos),
                    FLECHA_ESCANOS = flechaEscanos,
                    VOTOS = p.PorcentajeVoto,
                    VOTANTES = p.Votos,
                    DIF_VOTANTES = Math.Abs(difVotantes),
                    FLECHA_VOTANTES = flechaVotantes
                };
            })
            .Cast<object>()
            .ToList();

        return
        [
            new
            {
                CIRCUNSCRIPCION = snapshot.Circunscripcion.Nombre,
                CODIGO_CIRCUNSCRIPCION = snapshot.Circunscripcion.Codigo,
                ESCANOS = snapshot.EscaniosTotales,
                ESCRUTADO = snapshot.Escrutado,
                PARTICIPACION = snapshot.Participacion,
                PARTICIPACION_HISTORICO = snapshot.ParticipacionHistorica,
                PARTIDOS_TOTALES = partidos.Count,
                PARTIDOS_CON_ESCANO = partidosConEscano,
                RECUENTOS = recuentos
            }
        ];
    }

    private static int ToArrow(int delta)
    {
        if (delta > 0)
        {
            return 1;
        }

        if (delta < 0)
        {
            return -1;
        }

        return 0;
    }

    private static string NormalizeCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0";
        }

        var trimmed = raw.Trim().TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }
}
