using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var composer = new DefaultSignalComposer();

RunPactometroSondeoChecks(composer);
RunUltimoEscanoCapacityChecks(composer);
await RunCsvTemplateAlignmentChecksAsync();

Console.WriteLine("Signal parity checks: OK");

static void RunPactometroSondeoChecks(DefaultSignalComposer composer)
{
    var snapshot = new BrainStormSnapshot
    {
        Circunscripcion = new CircunscripcionSummary("9900000", "Espana"),
        Oficiales = false,
        EscaniosTotales = 100,
        MayoriaAbsoluta = 50,
        Partidos =
        [
            new PartidoSnapshot("001", "00001", "P1", "Partido 1", "", 20, 30, 55, 10, 0, 0, 0, 0, 0, 0, 0),
            new PartidoSnapshot("002", "00002", "P2", "Partido 2", "", 15, 12, 22, 8, 0, 0, 0, 0, 0, 0, 0)
        ]
    };

    var entra = composer.Compose(
        new OperationRequest
        {
            OperatorId = "op",
            Module = GraphicModule.Faldon,
            Action = OperationActionType.Update,
            Command = GraphicCommand.PactosEntraDerecha,
            CommandValue = "001",
            Oficiales = false,
            Target = GraphicsTarget.Ipf
        },
        snapshot);

    AssertContains(entra, "Pactometro_DerVALORDesde");
    AssertContains(entra, "Pactometro_DerVALORHasta");
    AssertContains(entra, "PactometroSondeo/PasaMayoria");
    AssertContains(entra, "PactometroSondeo/lanzaPactometroDer");
    AssertContains(entra, "Graficos/PactometroSondeo/Der/BarraDerechasHasta");
    AssertContains(entra, "Hasta/01");

    var sale = composer.Compose(
        new OperationRequest
        {
            OperatorId = "op",
            Module = GraphicModule.Faldon,
            Action = OperationActionType.Update,
            Command = GraphicCommand.PactosSale,
            Oficiales = false,
            Target = GraphicsTarget.Ipf
        },
        snapshot);

    AssertContains(sale, "PactometroSondeo/Sale");
    AssertContains(sale, "PactometroSondeo/reinicioPactometroIzq");
    AssertContains(sale, "PactometroSondeo/reinicioPactometroDer");
}

static void RunUltimoEscanoCapacityChecks(DefaultSignalComposer composer)
{
    var parties = Enumerable.Range(1, 7)
        .Select(i => new PartidoSnapshot(
            i.ToString("D3"),
            i.ToString("D5"),
            $"P{i}",
            $"Partido {i}",
            "",
            10,
            10,
            10,
            5,
            0,
            0,
            0,
            0,
            0,
            0,
            0))
        .ToList();

    var snapshot = new BrainStormSnapshot
    {
        Circunscripcion = new CircunscripcionSummary("9900000", "Espana"),
        Oficiales = true,
        EscaniosTotales = 350,
        Partidos = parties
    };

    string? sixth = null;
    string? seventh = null;

    for (var i = 1; i <= 7; i++)
    {
        var signal = composer.Compose(
            new OperationRequest
            {
                OperatorId = "op",
                Module = GraphicModule.Carton,
                Action = OperationActionType.Update,
                Command = GraphicCommand.UltimoEntraPartidoIzquierda,
                CommandValue = i.ToString("D3"),
                Oficiales = true,
                Target = GraphicsTarget.Ipf
            },
            snapshot);

        if (i == 6)
        {
            sixth = signal;
        }

        if (i == 7)
        {
            seventh = signal;
        }
    }

    if (sixth is null || seventh is null)
    {
        throw new InvalidOperationException("No se pudieron capturar señales de prueba de ultimo escaño.");
    }

    AssertContains(sixth, "Ultimo_Escano/Barras/Barra_Izq5");
    AssertNotContains(seventh, "Ultimo_Escano/Barras/");
}

static async Task RunCsvTemplateAlignmentChecksAsync()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "EleccionesWeb_SignalParityChecks");
    Directory.CreateDirectory(tempRoot);

    var writer = new FileBrainStormCsvWriter(
        Options.Create(new StorageOptions
        {
            CsvFolder = tempRoot,
            EnableRecuentosJson = false
        }),
        NullLogger<FileBrainStormCsvWriter>.Instance);

    var snapshot = new BrainStormSnapshot
    {
        Circunscripcion = new CircunscripcionSummary("0101000", "Provincia Test"),
        Oficiales = true,
        EscaniosTotales = 10,
        Partidos =
        [
            new PartidoSnapshot("00001", "00001", "A", "Partido A", "", 3, 3, 3, 1, 0, 0, 1000, 900, 0, 0, 0),
            new PartidoSnapshot("00003", "00003", "C", "Partido C", "", 2, 2, 2, 2, 0, 0, 800, 850, 0, 0, 0),
            new PartidoSnapshot("00004", "00004", "D", "Partido D", "", 1, 1, 1, 0, 0, 0, 400, 0, 0, 0, 0)
        ],
        CsvTemplatePartidos =
        [
            new PartidoSnapshot("1", "00001", "A", "Partido A", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new PartidoSnapshot("2", "00002", "B", "Partido B", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            new PartidoSnapshot("3", "00003", "C", "Partido C", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        ]
    };

    var csvPath = await writer.WriteAsync(snapshot, "TemplateAlignment", CancellationToken.None);
    var lines = await File.ReadAllLinesAsync(csvPath);

    var partyHeaderIndex = Array.FindIndex(lines, line =>
        line.StartsWith("Codigo;Padre;Siglas;Candidato;", StringComparison.Ordinal));

    if (partyHeaderIndex < 0)
    {
        throw new InvalidOperationException("No se encontró cabecera de partidos en CSV.");
    }

    // +1 => codigo 1, +2 => hueco codigo 2, +3 => codigo 3, +4 => extra codigo 4
    AssertStartsWith(lines[partyHeaderIndex + 1], "00001;");
    AssertStartsWith(lines[partyHeaderIndex + 2], ";;;;;;;;;;;;;;;");
    AssertStartsWith(lines[partyHeaderIndex + 3], "00003;");
    AssertStartsWith(lines[partyHeaderIndex + 4], "00004;");
}

static void AssertContains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected signal to contain '{expected}'.");
    }
}

static void AssertNotContains(string value, string forbidden)
{
    if (value.Contains(forbidden, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected signal to NOT contain '{forbidden}'.");
    }
}

static void AssertStartsWith(string value, string prefix)
{
    if (!value.StartsWith(prefix, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected line to start with '{prefix}', but got '{value}'.");
    }
}
