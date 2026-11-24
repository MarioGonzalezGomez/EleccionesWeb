using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using Elecciones.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elecciones.Tests;

public sealed class CartonCsvStructureTests : IDisposable
{
    private readonly string _tempRoot;

    public CartonCsvStructureTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"EleccionesCartonCsvTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Theory]
    [InlineData(CartonKind.Participacion, 1, new[] { "Carton_Participacion.csv" })]
    [InlineData(CartonKind.Mayorias, 2, new[] { "datos_partidos.csv", "mayorias_provincias.csv" })]
    [InlineData(CartonKind.Estatal, 2, new[] { "datos_partidos.csv", "Carton_Estatal_Provincia.csv" })]
    [InlineData(CartonKind.Cuadraditos, 1, new[] { "datos_partidos.csv" })]
    [InlineData(CartonKind.UltimoEscano, 1, new[] { "Carton_UltimoEscano.csv" })]
    public async Task WriteCartonAsync_GeneratesExpectedNumberAndNamesOfCsvFilesPerCartonKind(
        CartonKind kind,
        int expectedFileCount,
        string[] expectedFileNames)
    {
        var csvDir = Path.Combine(_tempRoot, kind.ToString());
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true);
        var query = new SnapshotQuery
        {
            ElectionSlot = 1,
            Kind = SnapshotQueryKind.Circunscripcion,
            CircunscripcionCodigo = "9900000"
        };

        var result = await writer.WriteCartonAsync(kind, snapshot, query, CancellationToken.None, GraphicModule.Carton);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(expectedFileCount, result.Files.Count);
        Assert.Equal(expectedFileNames, result.Files.Select(f => f.FileName).ToArray());

        foreach (var artifact in result.Files)
        {
            Assert.True(File.Exists(artifact.FullPath));
            var lines = await File.ReadAllLinesAsync(artifact.FullPath);
            Assert.True(lines.Length >= 2, $"Expected header and at least one data row in {artifact.FileName}");
        }

        if (kind is CartonKind.Mayorias or CartonKind.Estatal)
        {
            Assert.Contains(result.Files, f => f.LogicalRole == "Partidos");
            Assert.Contains(result.Files, f => f.LogicalRole == "Provincia");
        }
    }

    [Fact]
    public async Task DatosPartidosCsv_OrdersNumericallyByCode_IncludesZeroSeats_AndFormatsTendenciaSymbols()
    {
        var csvDir = Path.Combine(_tempRoot, "DatosPartidosExact");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var allCircParties = new List<PartidoSnapshot>
        {
            new("99999", "99999", "OTROS", "Otros", "", 0, 0, 0, 0, 0.54, 0.56, 156040, 155272, 0, 0, 0),
            new("00011", "00011", "JUNTS", "Junts", "", 7, 7, 7, 7, 1.64, 1.65, 470652, 460968, 0, 0, 0),
            new("00002", "00002", "PSOE", "PSOE", "", 111, 110, 112, 120, 27.11, 28.49, 7798702, 7964974, 0, 0, 0),
            new("00006", "00006", "PODEMOS", "Podemos", "", 0, 0, 0, 0, 2.76, 0, 792844, 0, 0, 0, 0),
            new("00001", "00001", "PP", "PP", "", 137, 135, 140, 89, 33.1, 20.8, 8160000, 5047040, 0, 0, 0)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Partidos = allCircParties.Where(p => p.Escanios > 0).ToList(),
            PartidosCircunscripcionCompletos = allCircParties
        };

        var export = await writer.WriteCartonAsync(
            CartonKind.Mayorias,
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "9900000" });

        var partidosFile = export.Files.Single(f => f.LogicalRole == "Partidos");
        Assert.Equal("datos_partidos.csv", partidosFile.FileName);

        var lines = await File.ReadAllLinesAsync(partidosFile.FullPath);
        Assert.Equal("partido;cod_partido;siglas;escanos;porcentaje;votos;escanos_ant;porcentaje_ant;votos_ant;tendencia", lines[0]);
        Assert.Equal(6, lines.Length);

        Assert.Equal("Partido01;1;PP;137;33.1;8160000;89;20.8;5047040;+", lines[1]);
        Assert.Equal("Partido02;2;PSOE;111;27.11;7798702;120;28.49;7964974;-", lines[2]);
        Assert.Equal("Partido03;6;PODEMOS;0;2.76;792844;;;;*", lines[3]);
        Assert.Equal("Partido04;11;JUNTS;7;1.64;470652;7;1.65;460968;=", lines[4]);
        Assert.Equal("Partido05;99999;OTROS;0;0.54;156040;0;0.56;155272;=", lines[5]);
    }

    [Fact]
    public async Task MayoriasProvinciasCsv_IncludesProvincesCommunitiesAndSpain_WithFiveDigitWinnerTotalSeatsAndEscrutado()
    {
        var csvDir = Path.Combine(_tempRoot, "MayoriasProvinciasExact");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var mayorias = new List<CircunscripcionMayoriaSnapshot>
        {
            new("9900000", "España", "1", "PP", 350, 137, 87.45, 68.12),
            new("0141000", "Sevilla", "00002", "PSOE", 12, 6, 91.2, 66.8),
            new("0104000", "Almería", "001", "PP", 6, 3, 100.0, 64.0),
            new("0100000", "Andalucía", "1", "PP", 61, 25, 89.05, 65.4),
            new("1952000", "Melilla", "", "", 1, 0, 0.0, 0.0) // 0% sin ganador aún
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            MayoriasCircunscripciones = mayorias
        };

        var export = await writer.WriteCartonAsync(
            CartonKind.Mayorias,
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "9900000" });

        var mayoriasFile = export.Files.Single(f => f.LogicalRole == "Provincia");
        Assert.Equal("mayorias_provincias.csv", mayoriasFile.FileName);

        var lines = await File.ReadAllLinesAsync(mayoriasFile.FullPath);
        Assert.Equal("cod_lugar;cod_partido;escanos;escrutado", lines[0]);
        Assert.Equal(6, lines.Length);

        // Ordenados por cod_lugar incluyendo CCAA (0100000), provincias (0104000, 0141000, 1952000) y general España (9900000)
        Assert.Equal("0100000;00001;61;89.05", lines[1]);
        Assert.Equal("0104000;00001;6;100", lines[2]);
        Assert.Equal("0141000;00002;12;91.2", lines[3]);
        Assert.Equal("1952000;;1;0", lines[4]);
        Assert.Equal("9900000;00001;350;87.45", lines[5]);
    }

    [Fact]
    public async Task OperationService_OnCartonModule_SynchronizesAllUniqueCartonCsvFilesAtOnce()
    {
        var csvDir = Path.Combine(_tempRoot, "OpServiceCartonAll");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var lockService = new ModuleLockService(new InMemoryLockStore());
        lockService.TryAcquire(GraphicModule.Carton, "op_carton");

        var stateService = new OperationStateService();
        var opService = new OperationService(
            lockService,
            new InMemoryEleccionesDataService(),
            writer,
            new NoOpGateway(),
            new DefaultSignalComposer(),
            new AllowAllOperatorAuthorizationService(),
            new NoOpAudit(),
            stateService);

        var result = await opService.ExecuteAsync(new OperationRequest
        {
            OperatorId = "op_carton",
            Module = GraphicModule.Carton,
            CartonKind = CartonKind.Mayorias,
            Scene = "MAYORIAS",
            Action = OperationActionType.Enter,
            Oficiales = true,
            Target = GraphicsTarget.Ipf,
            Query = new SnapshotQuery
            {
                ElectionSlot = 1,
                Kind = SnapshotQueryKind.Circunscripcion,
                CircunscripcionCodigo = "2800000"
            }
        });

        Assert.True(result.Success);
        // 5 archivos únicos: Carton_Participacion.csv, datos_partidos.csv, mayorias_provincias.csv, Carton_Estatal_Provincia.csv, Carton_UltimoEscano.csv
        Assert.Equal(5, result.GeneratedCsvPaths.Count);
        Assert.All(result.GeneratedCsvPaths, p => Assert.True(File.Exists(p)));

        var mayoriasLines = await File.ReadAllLinesAsync(Path.Combine(csvDir, "mayorias_provincias.csv"));
        Assert.Equal("cod_lugar;cod_partido;escanos;escrutado", mayoriasLines[0]);
        Assert.Contains(mayoriasLines, l => l.StartsWith("9900000;00001;350;"));
        Assert.Contains(mayoriasLines, l => l.StartsWith("0100000;00001;61;"));
        Assert.Contains(mayoriasLines, l => l.StartsWith("0141000;00002;12;"));
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

    private sealed class InMemoryLockStore : IModuleLockStateStore
    {
        private IReadOnlyList<ModuleLockInfo> _states = [];
        public IReadOnlyList<ModuleLockInfo> Load() => _states;
        public void Save(IReadOnlyList<ModuleLockInfo> states) => _states = states;
    }

    private sealed class NoOpGateway : IGraphicsGateway
    {
        public Task<IReadOnlyList<string>> SendAsync(GraphicsTarget target, string message, CancellationToken cancellationToken = default, GraphicModule? module = null)
            => Task.FromResult<IReadOnlyList<string>>(["IPF"]);
    }

    private sealed class NoOpAudit : IOperationAuditService
    {
        public Task RecordAsync(OperationRequest request, OperationResult result, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
