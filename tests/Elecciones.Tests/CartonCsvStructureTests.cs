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
    [InlineData(CartonKind.Participacion, true, 0, new string[0])]
    [InlineData(CartonKind.UltimoEscano, true, 0, new string[0])]
    [InlineData(CartonKind.Mayorias, true, 2, new[] { "datos_partidos.csv", "mayorias_provincias.csv" })]
    [InlineData(CartonKind.Estatal, true, 2, new[] { "datos_partidos.csv", "mayorias_provincias.csv" })]
    [InlineData(CartonKind.Mayorias, false, 2, new[] { "datos_sondeo.csv", "sondeo_provincias.csv" })]
    [InlineData(CartonKind.Estatal, false, 2, new[] { "datos_sondeo.csv", "sondeo_provincias.csv" })]
    public async Task WriteCartonAsync_GeneratesOnlyAuthorizedFilesForKindAndMode(
        CartonKind kind,
        bool oficiales,
        int expectedFileCount,
        string[] expectedFileNames)
    {
        var csvDir = Path.Combine(_tempRoot, $"{kind}_{oficiales}");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: oficiales);
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

        var diskFiles = Directory.GetFiles(csvDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(expectedFileNames.OrderBy(x => x).ToArray(), diskFiles);
    }

    [Fact]
    public async Task DatosPartidosCsv_OrdersByFiveDigitCode_IncludesDifEscanos_AndLeavesNewPartyDiffEmpty()
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
            new("11", "00011", "JUNTS", "Junts", "", 7, 7, 7, 7, 1.64, 1.65, 470652, 460968, 0, 0, 0),
            new("2", "00002", "PSOE", "PSOE", "", 111, 110, 112, 120, 27.11, 28.49, 7798702, 7964974, 0, 0, 0),
            new("6", "00006", "PODEMOS", "Podemos", "", 4, 3, 5, 0, 2.76, 0, 792844, 0, 0, 0, 0),
            new("1", "00001", "PP", "PP", "", 137, 135, 140, 89, 33.1, 20.8, 8160000, 5047040, 0, 0, 0)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            Partidos = allCircParties.Where(p => p.Escanios > 0).ToList(),
            PartidosCircunscripcionCompletos = allCircParties
        };

        var artifacts = await writer.WriteAllCartonFilesAsync(
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "9900000" });

        var partidosFile = artifacts.Single(f => f.FileName == "datos_partidos.csv");
        var lines = await File.ReadAllLinesAsync(partidosFile.FullPath);

        Assert.Equal("partido;cod_partido;siglas;escanos;porcentaje;votos;escanos_ant;porcentaje_ant;votos_ant;dif_escanos;tendencia", lines[0]);
        Assert.Equal(6, lines.Length);

        Assert.Equal("Partido01;00001;PP;137;33.1;8160000;89;20.8;5047040;+48;+", lines[1]);
        Assert.Equal("Partido02;00002;PSOE;111;27.11;7798702;120;28.49;7964974;-9;-", lines[2]);
        // Partido nuevo sin histórico: escanos_ant, porcentaje_ant, votos_ant y dif_escanos vacíos, tendencia *
        Assert.Equal("Partido03;00006;PODEMOS;4;2.76;792844;;;;;*", lines[3]);
        Assert.Equal("Partido04;00011;JUNTS;7;1.64;470652;7;1.65;460968;0;=", lines[4]);
        Assert.Equal("Partido05;99999;OTROS;0;0.54;156040;0;0.56;155272;0;=", lines[5]);
    }

    [Fact]
    public async Task DatosSondeoCsv_ComputesDiferenciaAlwaysFromHastaMinusHistoricos()
    {
        var csvDir = Path.Combine(_tempRoot, "DatosSondeoExact");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var allCircParties = new List<PartidoSnapshot>
        {
            new("1", "00001", "PP", "PP", "", 0, 145, 150, 137, 0, 33.1, 0, 8160000, 0, 0, 0),
            new("2", "00002", "PSOE", "PSOE", "", 0, 110, 115, 121, 0, 31.7, 0, 7820000, 0, 0, 0),
            new("3", "00003", "VOX", "VOX", "", 0, 31, 33, 33, 0, 12.4, 0, 3057000, 0, 0, 0),
            new("99", "00099", "SALF", "SALF", "", 0, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: false) with
        {
            Partidos = allCircParties,
            PartidosCircunscripcionCompletos = allCircParties
        };

        var artifacts = await writer.WriteAllCartonFilesAsync(
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "9900000" });

        var sondeoFile = artifacts.Single(f => f.FileName == "datos_sondeo.csv");
        var lines = await File.ReadAllLinesAsync(sondeoFile.FullPath);

        Assert.Equal("partido;cod_partido;siglas;desde;hasta;diferencia", lines[0]);
        Assert.Equal(5, lines.Length);

        Assert.Equal("Partido01;00001;PP;145;150;+13", lines[1]);
        Assert.Equal("Partido02;00002;PSOE;110;115;-6", lines[2]);
        Assert.Equal("Partido03;00003;VOX;31;33;=", lines[3]);
        Assert.Equal("Partido04;00099;SALF;2;3;*", lines[4]);
    }

    [Fact]
    public async Task DatosPartidosAndDatosSondeo_AlwaysIncludeAllElectionParties_ZeroingNonContestingInSelectedCircunscripcion()
    {
        var csvDir = Path.Combine(_tempRoot, "FullElectionPartiesProvincial");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        // Simulamos 6 partidos maestros en toda la elección (más "00000" IGNORAR que debe excluirse):
        var electionMasterParties = new List<PartidoEleccionSummary>
        {
            new("00000", "IGNORAR", "IGNORAR", "00000"),
            new("00022", "FA", "Frente Amplio", "00022"),
            new("00001", "PP", "Partido Popular", "00001"),
            new("00011", "JUNTS", "Junts per Catalunya", "00011"),
            new("90079", "FIA", "Federación Independientes Aragón", "90079"),
            new("00002", "PSOE", "Partido Socialista", "00002"),
            new("00004", "VOX", "VOX", "00004")
        };

        // En la provincia seleccionada (Teruel) solo concurren 3 partidos (PP, VOX y FA);
        // PSOE, JUNTS y FIA no se presentan aquí pero deben aparecer en su fila absoluta con datos a 0.
        var provincialParties = new List<PartidoSnapshot>
        {
            new("22", "00022", "FA", "Frente Amplio", "", 1, 0, 1, 1, 8.25, 9.10, 6200, 6800, 0, 0, 0),
            new("00001", "00001", "PP", "PP", "", 2, 1, 2, 1, 38.50, 35.20, 28900, 26400, 0, 0, 0),
            new("4", "00004", "VOX", "VOX", "", 0, 0, 1, 0, 11.20, 10.50, 8400, 7900, 0, 0, 0)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true, circunscripcionCodigo: "0244000", circunscripcionNombre: "Teruel") with
        {
            Partidos = provincialParties.Where(p => p.Escanios > 0).ToList(),
            PartidosCircunscripcionCompletos = provincialParties,
            PartidosEleccion = electionMasterParties
        };

        var artifacts = await writer.WriteAllCartonFilesAsync(
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "0244000" });

        var datosPartidosLines = await File.ReadAllLinesAsync(artifacts.Single(f => f.FileName == "datos_partidos.csv").FullPath);
        Assert.Equal("partido;cod_partido;siglas;escanos;porcentaje;votos;escanos_ant;porcentaje_ant;votos_ant;dif_escanos;tendencia", datosPartidosLines[0]);
        Assert.Equal(7, datosPartidosLines.Length);
        Assert.Equal("Partido01;00001;PP;2;38.5;28900;1;35.2;26400;+1;+", datosPartidosLines[1]);
        Assert.Equal("Partido02;00002;PSOE;0;0;0;;;;;*", datosPartidosLines[2]);
        Assert.Equal("Partido03;00004;VOX;0;11.2;8400;0;10.5;7900;0;=", datosPartidosLines[3]);
        Assert.Equal("Partido04;00011;JUNTS;0;0;0;;;;;*", datosPartidosLines[4]);
        Assert.Equal("Partido05;00022;FA;1;8.25;6200;1;9.1;6800;0;=", datosPartidosLines[5]);
        Assert.Equal("Partido06;90079;FIA;0;0;0;;;;;*", datosPartidosLines[6]);

        var datosSondeoLines = await File.ReadAllLinesAsync(artifacts.Single(f => f.FileName == "datos_sondeo.csv").FullPath);
        Assert.Equal("partido;cod_partido;siglas;desde;hasta;diferencia", datosSondeoLines[0]);
        Assert.Equal(7, datosSondeoLines.Length);
        Assert.Equal("Partido01;00001;PP;1;2;+1", datosSondeoLines[1]);
        Assert.Equal("Partido02;00002;PSOE;0;0;*", datosSondeoLines[2]);
        Assert.Equal("Partido03;00004;VOX;0;1;+1", datosSondeoLines[3]);
        Assert.Equal("Partido04;00011;JUNTS;0;0;*", datosSondeoLines[4]);
        Assert.Equal("Partido05;00022;FA;0;1;=", datosSondeoLines[5]);
        Assert.Equal("Partido06;90079;FIA;0;0;*", datosSondeoLines[6]);
    }

    [Fact]
    public async Task MayoriasAndSondeoProvinciasCsv_IncludeProvincesCommunitiesAndSpainWithExactColumns()
    {
        var csvDir = Path.Combine(_tempRoot, "ProvinciasExact");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                EnableRecuentosJson = false
            }),
            NullLogger<FileBrainStormCsvWriter>.Instance);

        var mayorias = new List<CircunscripcionMayoriaSnapshot>
        {
            new("9900000", "España", "1", "PP", 350, 137, 87.45, 68.12, 48),
            new("0141000", "Sevilla", "00002", "PSOE", 12, 6, 91.2, 66.8, -1),
            new("0104000", "Almería", "001", "PP", 6, 3, 100.0, 64.0, 1),
            new("0100000", "Andalucía", "1", "PP", 61, 25, 89.05, 65.4, 4),
            new("1952000", "Melilla", "", "", 1, 0, 0.0, 0.0, 0)
        };

        var sondeoMayorias = new List<CircunscripcionMayoriaSnapshot>
        {
            new("9900000", "España", "1", "PP", 350, 150, 0, 0, 13),
            new("0141000", "Sevilla", "2", "PSOE", 12, 5, 0, 0, -2),
            new("0104000", "Almería", "1", "PP", 6, 3, 0, 0, 0),
            new("0100000", "Andalucía", "1", "PP", 61, 28, 0, 0, 3),
            new("1952000", "Melilla", "", "", 1, 0, 0, 0, 0)
        };

        var snapshot = TestSnapshotFactory.CreateSampleSnapshot(oficiales: true) with
        {
            MayoriasCircunscripciones = mayorias,
            SondeoMayoriasCircunscripciones = sondeoMayorias
        };

        var artifacts = await writer.WriteAllCartonFilesAsync(
            snapshot,
            new SnapshotQuery { CircunscripcionCodigo = "9900000" });

        var mayoriasLines = await File.ReadAllLinesAsync(artifacts.Single(f => f.FileName == "mayorias_provincias.csv").FullPath);
        Assert.Equal("cod_lugar;cod_partido;escanos;escrutado", mayoriasLines[0]);
        Assert.Equal("0100000;00001;61;89.05", mayoriasLines[1]);
        Assert.Equal("0104000;00001;6;100", mayoriasLines[2]);
        Assert.Equal("0141000;00002;12;91.2", mayoriasLines[3]);
        Assert.Equal("1952000;;1;0", mayoriasLines[4]);
        Assert.Equal("9900000;00001;350;87.45", mayoriasLines[5]);

        var sondeoProvLines = await File.ReadAllLinesAsync(artifacts.Single(f => f.FileName == "sondeo_provincias.csv").FullPath);
        Assert.Equal("cod_lugar;cod_partido;escanos;diferencia", sondeoProvLines[0]);
        Assert.Equal("0100000;00001;61;3", sondeoProvLines[1]);
        Assert.Equal("0104000;00001;6;0", sondeoProvLines[2]);
        Assert.Equal("0141000;00002;12;-2", sondeoProvLines[3]);
        Assert.Equal("1952000;;1;", sondeoProvLines[4]);
        Assert.Equal("9900000;00001;350;13", sondeoProvLines[5]);
    }

    [Fact]
    public async Task OperationService_OnCartonModule_WritesStrictlyFourCsvFilesAndNoJsonOrLegacyFiles()
    {
        var csvDir = Path.Combine(_tempRoot, "OpServiceCartonStrictFour");
        var writer = new FileBrainStormCsvWriter(
            Options.Create(new StorageOptions
            {
                CsvFolder = csvDir,
                JsonFolder = csvDir,
                EnableRecuentosJson = true
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
        Assert.Equal(4, result.GeneratedCsvPaths.Count);

        var diskFiles = Directory.GetFiles(csvDir).Select(Path.GetFileName).OrderBy(x => x).ToArray();
        Assert.Equal(
            new string?[] { "datos_partidos.csv", "datos_sondeo.csv", "mayorias_provincias.csv", "sondeo_provincias.csv" },
            diskFiles);
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
