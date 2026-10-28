using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using Elecciones.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elecciones.Tests;

public sealed class CsvExportBaselineTests : IDisposable
{
    private readonly string _tempRoot;

    public CsvExportBaselineTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"EleccionesWebTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task WriteAsync_NewFormat_GeneratesExpectedBrainStormCsvHeaderAndRows()
    {
        var storageOptions = Options.Create(new StorageOptions
        {
            CsvFolder = Path.Combine(_tempRoot, "CSV"),
            JsonFolder = Path.Combine(_tempRoot, "JSON"),
            BrainStormCsvFormat = "NEW",
            EnableRecuentosJson = true,
            RecuentosJsonFileName = "datos_escrutado"
        });

        var writer = new FileBrainStormCsvWriter(storageOptions, NullLogger<FileBrainStormCsvWriter>.Instance);
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var filePath = await writer.WriteAsync(snapshot, "BrainStorm");

        Assert.True(File.Exists(filePath));
        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.True(lines.Length >= 5);
        Assert.Equal(
            "Codigo;Nombre;Escrutado;Escanios;Mayoria;Avance;Participacion;Participacion Historica;Media de Participacion;Votantes;Ultimas Elecciones;Numero de partidos;Ultimo;Siguiente;Resto;NombreSondeo;Vot.Faltan",
            lines[0]);
        Assert.Contains("9900000;ESPANA;87.45;350;176;3;68.12;66.20;67.16;24500120;2023;4;PP;PSOE;1420;;3100200", lines[1]);
        Assert.Equal(
            "Codigo;Padre;Siglas;Candidato;Escanios;Escanios Desde Sondeo;Hasta Sondeo;Historicos;% Voto;Votantes;Diferencia de escanios;Tendencia;Diferencia de votos;Tendencia;Votantes Historico;Nombre",
            lines[2]);
        Assert.StartsWith("00001;00001;PP;Alberto Nunez Feijoo;137;135;140;89;33;8160000;48;+;", lines[3]);
    }

    [Fact]
    public async Task WriteSedesAndRecuentosJson_GeneratesValidFiles()
    {
        var storageOptions = Options.Create(new StorageOptions
        {
            CsvFolder = Path.Combine(_tempRoot, "CSV"),
            JsonFolder = Path.Combine(_tempRoot, "JSON"),
            BrainStormCsvFormat = "NEW",
            EnableRecuentosJson = true,
            RecuentosJsonFileName = "datos_escrutado"
        });

        var writer = new FileBrainStormCsvWriter(storageOptions, NullLogger<FileBrainStormCsvWriter>.Instance);
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var sedesPath = await writer.WriteSedesAsync(snapshot, "00001");
        var jsonPath = await writer.WriteRecuentosJsonAsync(snapshot);

        Assert.NotNull(sedesPath);
        Assert.True(File.Exists(sedesPath));
        var sedesContent = await File.ReadAllTextAsync(sedesPath!);
        Assert.Contains("00001;00001;PP;Alberto Nunez Feijoo;137;89;33;8160000;48;+;", sedesContent);

        Assert.NotNull(jsonPath);
        Assert.True(File.Exists(jsonPath));
        var jsonContent = await File.ReadAllTextAsync(jsonPath!);
        Assert.Contains("\"CIRCUNSCRIPCION\": \"ESPANA\"", jsonContent);
        Assert.Contains("\"SIGLA_OFICIAL\": \"PP\"", jsonContent);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in temp directory
        }
    }
}

