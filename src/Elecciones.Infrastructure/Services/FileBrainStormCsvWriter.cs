using System.Globalization;
using System.Text;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elecciones.Infrastructure.Services;

public sealed class FileBrainStormCsvWriter : IBrainStormCsvWriter
{
    private readonly StorageOptions _options;
    private readonly ILogger<FileBrainStormCsvWriter> _logger;

    public FileBrainStormCsvWriter(IOptions<StorageOptions> options, ILogger<FileBrainStormCsvWriter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        BrainStormSnapshot snapshot,
        string exportName,
        CancellationToken cancellationToken = default)
    {
        var folderPath = ResolveCsvFolder(_options.CsvFolder);
        Directory.CreateDirectory(folderPath);

        var safeName = string.IsNullOrWhiteSpace(exportName) ? "BrainStorm" : exportName.Trim();
        var filePath = Path.Combine(folderPath, $"{safeName}.csv");

        var csv = BuildCsv(snapshot);
        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8, cancellationToken);

        _logger.LogInformation("BrainStorm CSV generated at {Path}", filePath);

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

    private static string BuildCsv(BrainStormSnapshot snapshot)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Codigo;Nombre;Escrutado;Escanios;Mayoria;Avance;Votantes;UltimasElecciones;NumeroPartidos");
        sb.Append(snapshot.Circunscripcion.Codigo).Append(';')
            .Append(snapshot.Circunscripcion.Nombre).Append(';')
            .Append(snapshot.Escrutado.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
            .Append(snapshot.EscaniosTotales).Append(';')
            .Append(snapshot.MayoriaAbsoluta).Append(';')
            .Append(snapshot.Avance).Append(';')
            .Append(snapshot.VotantesTotales).Append(';')
            .Append(snapshot.AnioUltimasElecciones).Append(';')
            .Append(snapshot.Partidos.Count)
            .AppendLine();

        sb.AppendLine("Codigo;Siglas;Escanios;DesdeSondeo;HastaSondeo;Historicos;PorcentajeVoto;Votos");

        foreach (var partido in snapshot.Partidos)
        {
            sb.Append(partido.Codigo).Append(';')
                .Append(partido.Siglas).Append(';')
                .Append(partido.Escanios).Append(';')
                .Append(partido.EscaniosDesdeSondeo).Append(';')
                .Append(partido.EscaniosHastaSondeo).Append(';')
                .Append(partido.EscaniosHistoricos).Append(';')
                .Append(partido.PorcentajeVoto.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
                .Append(partido.Votos)
                .AppendLine();
        }

        return sb.ToString();
    }
}
