using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using Elecciones.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elecciones.Tests;

public sealed class CsvMultiModuleRoutingTests : IDisposable
{
    private readonly string _tempRoot;

    public CsvMultiModuleRoutingTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"EleccionesMultiModTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task WriteAsync_WithModuleSpecificFolder_RoutesToModuleFolderAndFallsBackToGlobalWhenUnset()
    {
        var globalCsvFolder = Path.Combine(_tempRoot, "GlobalCSV");
        var cartonCsvFolder = Path.Combine(_tempRoot, "CartonCSV");
        var globalJsonFolder = Path.Combine(_tempRoot, "GlobalJSON");
        var cartonJsonFolder = Path.Combine(_tempRoot, "CartonJSON");

        var storageOptions = Options.Create(new StorageOptions
        {
            CsvFolder = globalCsvFolder,
            JsonFolder = globalJsonFolder,
            BrainStormCsvFormat = "NEW",
            EnableRecuentosJson = true,
            RecuentosJsonFileName = "datos_escrutado",
            ModuleCsvFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Carton"] = cartonCsvFolder,
                ["Faldon"] = string.Empty
            },
            ModuleJsonFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Carton"] = cartonJsonFolder
            }
        });

        var writer = new FileBrainStormCsvWriter(storageOptions, NullLogger<FileBrainStormCsvWriter>.Instance);
        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);

        var cartonPath = await writer.WriteAsync(snapshot, "BrainStorm", CancellationToken.None, GraphicModule.Carton);
        var faldonPath = await writer.WriteAsync(snapshot, "BrainStorm", CancellationToken.None, GraphicModule.Faldon);
        var cartonJsonPath = await writer.WriteRecuentosJsonAsync(snapshot, CancellationToken.None, GraphicModule.Carton);

        Assert.Equal(Path.Combine(cartonCsvFolder, "BrainStorm.csv"), cartonPath);
        Assert.True(File.Exists(cartonPath));

        Assert.Equal(Path.Combine(globalCsvFolder, "BrainStorm.csv"), faldonPath);
        Assert.True(File.Exists(faldonPath));

        Assert.Equal(Path.Combine(cartonJsonFolder, "datos_escrutado.json"), cartonJsonPath);
        Assert.True(File.Exists(cartonJsonPath!));
    }

    [Fact]
    public async Task WriteAsync_ConcurrentWritesToSameTargetPath_SerializesWithoutIoException()
    {
        var sharedFolder = Path.Combine(_tempRoot, "ConcurrentCSV");
        var storageOptions = Options.Create(new StorageOptions
        {
            CsvFolder = sharedFolder,
            JsonFolder = sharedFolder,
            BrainStormCsvFormat = "OLD",
            EnableRecuentosJson = true,
            RecuentosJsonFileName = "datos_escrutado"
        });

        var writer = new FileBrainStormCsvWriter(storageOptions, NullLogger<FileBrainStormCsvWriter>.Instance);
        var snapshotOficial = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);
        var snapshotSondeo = TestSnapshotFactory.CreateSampleSnapshot(oficiales: false);

        var tasks = Enumerable.Range(0, 24).Select(async i =>
        {
            var snap = i % 2 == 0 ? snapshotOficial : snapshotSondeo;
            var mod = i % 2 == 0 ? GraphicModule.Faldon : GraphicModule.Carton;
            await writer.WriteAsync(snap, "BrainStorm", CancellationToken.None, mod);
            await writer.WriteRecuentosJsonAsync(snap, CancellationToken.None, mod);
            await writer.WriteSedesAsync(snap, "00001", CancellationToken.None, mod);
        });

        await Task.WhenAll(tasks);

        Assert.True(File.Exists(Path.Combine(sharedFolder, "BrainStorm.csv")));
        Assert.True(File.Exists(Path.Combine(sharedFolder, "datos_escrutado.json")));
        Assert.True(File.Exists(Path.Combine(sharedFolder, "Sedes.csv")));
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
            // Ignore cleanup errors
        }
    }
}

