using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elecciones.Application.Models;
using Elecciones.GraphicsGateway.Options;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EleccionesWeb.Services;

public sealed class OperationalConfigurationService : IOperationalConfigurationService
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private readonly IOptionsMonitor<GraphicsEndpointsOptions> _graphicsMonitor;
    private readonly IOptionsMonitor<OperationalOptions> _operationalMonitor;
    private readonly IOptionsMonitor<StorageOptions> _storageMonitor;
    private readonly IOptionsMonitor<DatabaseOptions> _databaseMonitor;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration? _configuration;

    public event Action? ConfigurationChanged;

    public OperationalConfigurationService(
        IOptionsMonitor<GraphicsEndpointsOptions> graphicsMonitor,
        IOptionsMonitor<OperationalOptions> operationalMonitor,
        IOptionsMonitor<StorageOptions> storageMonitor,
        IOptionsMonitor<DatabaseOptions> databaseMonitor,
        IWebHostEnvironment environment,
        IConfiguration? configuration = null)
    {
        _graphicsMonitor = graphicsMonitor;
        _operationalMonitor = operationalMonitor;
        _storageMonitor = storageMonitor;
        _databaseMonitor = databaseMonitor;
        _environment = environment;
        _configuration = configuration;
    }

    public OperationalConfigurationSnapshot GetSnapshot()
    {
        return new OperationalConfigurationSnapshot
        {
            GraphicsEndpoints = Clone(_graphicsMonitor.CurrentValue),
            Operational = NormalizeOperational(Clone(_operationalMonitor.CurrentValue)),
            Storage = CloneStorage(_storageMonitor.CurrentValue),
            Database = CloneDatabase(_databaseMonitor.CurrentValue)
        };
    }

    public async Task<IReadOnlyList<EndpointConnectivityStatus>> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = _graphicsMonitor.CurrentValue;
        var faldonIpf = endpoints.ResolveIpfForModule(GraphicModule.Faldon);
        var cartonIpf = endpoints.ResolveIpfForModule(GraphicModule.Carton);

        return await Task.WhenAll(
            CheckEndpointAsync("Brainstorm Faldón", faldonIpf, cancellationToken),
            CheckEndpointAsync("Brainstorm Cartón", cartonIpf, cancellationToken),
            CheckEndpointAsync("PRIME", endpoints.Prime, cancellationToken),
            CheckEndpointAsync("IPF2 (Reserva Faldón)", endpoints.Ipf2, cancellationToken));
    }

    public async Task<IReadOnlyList<EndpointConnectivityStatus>> CheckDatabaseConnectivityAsync(
        DatabaseOptions? candidate = null,
        CancellationToken cancellationToken = default)
    {
        var dbOpts = CloneDatabase(candidate ?? _databaseMonitor.CurrentValue);
        var host = string.IsNullOrWhiteSpace(dbOpts.Host) ? "127.0.0.1" : dbOpts.Host.Trim();
        var port = dbOpts.Port > 0 ? dbOpts.Port : 3306;
        var password = !string.IsNullOrWhiteSpace(dbOpts.Password)
            ? dbOpts.Password
            : (Environment.GetEnvironmentVariable("ELECCIONES_DB_PASSWORD") ?? string.Empty);

        var results = new List<EndpointConnectivityStatus>();
        foreach (var elec in dbOpts.GetActiveElections())
        {
            var label = $"E{elec.Slot} · {elec.Label}";
            if (string.IsNullOrWhiteSpace(elec.DatabaseName))
            {
                results.Add(new EndpointConnectivityStatus(
                    label,
                    dbOpts.Enabled,
                    host,
                    port,
                    false,
                    "Nombre de base de datos vacío"));
                continue;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));

                var connStr = $"server={host};port={port};uid={dbOpts.User};pwd={password};database={elec.DatabaseName};Connect Timeout=3;";
                var builder = new DbContextOptionsBuilder<EleccionesDbContext>();
                builder.UseMySQL(connStr, mysql => mysql.CommandTimeout(3));

                await using var db = new EleccionesDbContext(builder.Options);
                var circCount = await db.Circunscripciones.AsNoTracking().CountAsync(timeoutCts.Token);

                results.Add(new EndpointConnectivityStatus(
                    $"{label} ({elec.DatabaseName})",
                    true,
                    host,
                    port,
                    true,
                    $"Conectada · {circCount} circunscripciones"));
            }
            catch (Exception ex)
            {
                results.Add(new EndpointConnectivityStatus(
                    $"{label} ({elec.DatabaseName})",
                    true,
                    host,
                    port,
                    false,
                    ex.Message));
            }
        }

        return results;
    }

    public async Task<SaveOperationalConfigurationResult> SaveAsync(
        OperationalConfigurationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveSettingsFilePath();
        await SaveGate.WaitAsync(cancellationToken);
        try
        {
            var graphics = Clone(snapshot.GraphicsEndpoints);
            var operational = NormalizeOperational(Clone(snapshot.Operational));
            var storage = CloneStorage(snapshot.Storage);
            var database = CloneDatabase(snapshot.Database);

            var json = await File.ReadAllTextAsync(targetPath, cancellationToken);
            var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            var graphicsSection = root["GraphicsEndpoints"]?.AsObject() ?? new JsonObject();
            graphicsSection["SendToIpf2WithIpf"] = graphics.SendToIpf2WithIpf;
            graphicsSection["Ipf"] = BuildEndpointNode(graphics.Ipf);

            var ipfByModuleNode = new JsonObject();
            foreach (var kvp in graphics.IpfByModule)
            {
                ipfByModuleNode[kvp.Key] = BuildOverrideEndpointNode(kvp.Value, graphics.Ipf);
            }
            graphicsSection["IpfByModule"] = ipfByModuleNode;

            graphicsSection["Prime"] = BuildEndpointNode(graphics.Prime);
            graphicsSection["Ipf2"] = BuildEndpointNode(graphics.Ipf2);
            root["GraphicsEndpoints"] = graphicsSection;

            var operationalSection = root["Operational"]?.AsObject() ?? new JsonObject();
            operationalSection["Hours"] = BuildHoursNode(operational.Hours);
            operationalSection["VideoSources"] = BuildVideoSourcesNode(operational.VideoSources);
            root["Operational"] = operationalSection;

            var storageSection = root["Storage"]?.AsObject() ?? new JsonObject();
            storageSection["CsvFolder"] = storage.CsvFolder;
            storageSection["JsonFolder"] = storage.JsonFolder;
            var csvByModuleNode = storageSection["CsvFoldersByModule"]?.AsObject() ?? new JsonObject();
            foreach (var kvp in storage.CsvFoldersByModule)
            {
                csvByModuleNode[kvp.Key] = kvp.Value?.Trim() ?? string.Empty;
            }
            storageSection["CsvFoldersByModule"] = csvByModuleNode;
            var cartonFiles = storage.CartonCsvFiles ?? new CartonCsvFilesOptions();
            storageSection["CartonCsvFiles"] = new JsonObject
            {
                ["Delimiter"] = string.IsNullOrEmpty(cartonFiles.Delimiter) ? ";" : cartonFiles.Delimiter,
                ["DatosPartidosFileName"] = cartonFiles.DatosPartidosFileName?.Trim() ?? "datos_partidos.csv",
                ["MayoriasProvinciasFileName"] = cartonFiles.MayoriasProvinciasFileName?.Trim() ?? "mayorias_provincias.csv",
                ["DatosSondeoFileName"] = cartonFiles.DatosSondeoFileName?.Trim() ?? "datos_sondeo.csv",
                ["SondeoProvinciasFileName"] = cartonFiles.SondeoProvinciasFileName?.Trim() ?? "sondeo_provincias.csv"
            };
            root["Storage"] = storageSection;

            var databaseSection = root["Database"]?.AsObject() ?? new JsonObject();
            databaseSection["Enabled"] = database.Enabled;
            databaseSection["Host"] = database.Host;
            databaseSection["HostBackup"] = database.HostBackup;
            databaseSection["HostLocal"] = database.HostLocal;
            databaseSection["Port"] = database.Port;
            databaseSection["User"] = database.User;
            databaseSection["Password"] = database.Password;
            databaseSection["CommandTimeoutSeconds"] = Math.Max(2, database.CommandTimeoutSeconds);
            databaseSection["NumEleccionesSimultaneas"] = database.ClampedActiveCount;
            databaseSection["DefaultElectionSlot"] = Math.Clamp(database.DefaultElectionSlot, 1, database.ClampedActiveCount);
            databaseSection["Label1"] = database.GetElectionLabel(1);
            databaseSection["Label2"] = database.GetElectionLabel(2);
            databaseSection["Label3"] = database.GetElectionLabel(3);
            databaseSection["Name"] = database.Name;
            databaseSection["Name2"] = database.Name2;
            databaseSection["Name3"] = database.Name3;
            databaseSection["TipoElecciones"] = database.TipoElecciones is 1 or 2 ? database.TipoElecciones : 1;
            databaseSection["Regional"] = database.Regional;
            databaseSection["CodigoRegionalBD1"] = database.CodigoRegionalBD1;
            databaseSection["CodigoRegionalBD2"] = database.CodigoRegionalBD2;
            databaseSection["CodigoRegionalBD3"] = database.CodigoRegionalBD3;
            root["Database"] = databaseSection;

            var output = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tempPath, output, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);

            // Fuerza la recarga síncrona inmediata de IOptionsMonitor antes de notificar a las pantallas activas
            (_configuration as IConfigurationRoot)?.Reload();
            ConfigurationChanged?.Invoke();

            return new SaveOperationalConfigurationResult(
                true,
                $"Configuración guardada en {Path.GetFileName(targetPath)}.",
                targetPath);
        }
        catch (Exception ex)
        {
            return new SaveOperationalConfigurationResult(
                false,
                $"Error guardando configuración: {ex.Message}",
                targetPath);
        }
        finally
        {
            SaveGate.Release();
        }
    }

    private string ResolveSettingsFilePath()
    {
        var fileName = _environment.IsDevelopment()
            ? "appsettings.Development.json"
            : "appsettings.json";
        return Path.Combine(_environment.ContentRootPath, fileName);
    }

    private static JsonObject BuildEndpointNode(GraphicsEndpointOptions endpoint)
    {
        return new JsonObject
        {
            ["Enabled"] = endpoint.Enabled,
            ["Host"] = endpoint.Host,
            ["Port"] = endpoint.Port,
            ["Bd"] = endpoint.Bd
        };
    }

    private static JsonObject BuildOverrideEndpointNode(GraphicsEndpointOverrideOptions endpoint, GraphicsEndpointOptions fallback)
    {
        return new JsonObject
        {
            ["Enabled"] = endpoint.Enabled ?? fallback.Enabled,
            ["Host"] = string.IsNullOrWhiteSpace(endpoint.Host) ? fallback.Host : endpoint.Host.Trim(),
            ["Port"] = endpoint.Port is > 0 ? endpoint.Port.Value : fallback.Port,
            ["Bd"] = string.IsNullOrWhiteSpace(endpoint.Bd) ? fallback.Bd : endpoint.Bd.Trim()
        };
    }

    private static JsonObject BuildHoursNode(OperationalHoursOptions hours)
    {
        return new JsonObject
        {
            ["HoraAvance1"] = hours.HoraAvance1,
            ["HoraAvance2"] = hours.HoraAvance2,
            ["HoraAvance3"] = hours.HoraAvance3,
            ["HoraParticipacion"] = hours.HoraParticipacion,
            ["HoraAvance1Historico"] = hours.HoraAvance1Historico,
            ["HoraAvance2Historico"] = hours.HoraAvance2Historico,
            ["HoraAvance3Historico"] = hours.HoraAvance3Historico,
            ["HoraParticipacionHistorico"] = hours.HoraParticipacionHistorico
        };
    }

    private static JsonArray BuildVideoSourcesNode(IEnumerable<VideoSourceOptions> sources)
    {
        var array = new JsonArray();
        foreach (var source in sources.OrderBy(x => x.Slot))
        {
            array.Add(new JsonObject
            {
                ["Slot"] = source.Slot,
                ["IsLive"] = source.IsLive,
                ["Path"] = source.Path
            });
        }

        return array;
    }

    private static OperationalOptions NormalizeOperational(OperationalOptions options)
    {
        var bySlot = options.VideoSources
            .Where(x => x is not null)
            .GroupBy(x => x.Slot)
            .ToDictionary(g => g.Key, g => g.First());

        var normalizedSources = Enumerable.Range(1, 6)
            .Select(slot =>
            {
                if (bySlot.TryGetValue(slot, out var source))
                {
                    return new VideoSourceOptions
                    {
                        Slot = slot,
                        IsLive = source.IsLive,
                        Path = source.Path?.Trim() ?? string.Empty
                    };
                }

                return new VideoSourceOptions
                {
                    Slot = slot,
                    IsLive = false,
                    Path = string.Empty
                };
            })
            .ToList();

        options.VideoSources = normalizedSources;
        options.Hours ??= new OperationalHoursOptions();
        options.Hours.HoraAvance1 = options.Hours.HoraAvance1?.Trim() ?? string.Empty;
        options.Hours.HoraAvance2 = options.Hours.HoraAvance2?.Trim() ?? string.Empty;
        options.Hours.HoraAvance3 = options.Hours.HoraAvance3?.Trim() ?? string.Empty;
        options.Hours.HoraParticipacion = options.Hours.HoraParticipacion?.Trim() ?? string.Empty;
        options.Hours.HoraAvance1Historico = options.Hours.HoraAvance1Historico?.Trim() ?? string.Empty;
        options.Hours.HoraAvance2Historico = options.Hours.HoraAvance2Historico?.Trim() ?? string.Empty;
        options.Hours.HoraAvance3Historico = options.Hours.HoraAvance3Historico?.Trim() ?? string.Empty;
        options.Hours.HoraParticipacionHistorico = options.Hours.HoraParticipacionHistorico?.Trim() ?? string.Empty;

        return options;
    }

    private static GraphicsEndpointsOptions Clone(GraphicsEndpointsOptions options)
    {
        var byModule = new Dictionary<string, GraphicsEndpointOverrideOptions>(StringComparer.OrdinalIgnoreCase);
        if (options.IpfByModule is not null)
        {
            foreach (var kvp in options.IpfByModule)
            {
                if (kvp.Value is null)
                {
                    continue;
                }

                byModule[kvp.Key] = new GraphicsEndpointOverrideOptions
                {
                    Enabled = kvp.Value.Enabled,
                    Host = kvp.Value.Host?.Trim() ?? string.Empty,
                    Port = kvp.Value.Port,
                    Bd = kvp.Value.Bd?.Trim() ?? string.Empty
                };
            }
        }

        return new GraphicsEndpointsOptions
        {
            SendToIpf2WithIpf = options.SendToIpf2WithIpf,
            Ipf = CloneEndpoint(options.Ipf),
            IpfByModule = byModule,
            Prime = CloneEndpoint(options.Prime),
            Ipf2 = CloneEndpoint(options.Ipf2)
        };
    }

    private static GraphicsEndpointOptions CloneEndpoint(GraphicsEndpointOptions endpoint)
    {
        return new GraphicsEndpointOptions
        {
            Enabled = endpoint.Enabled,
            Host = endpoint.Host?.Trim() ?? "127.0.0.1",
            Port = endpoint.Port,
            Bd = endpoint.Bd?.Trim() ?? "dbs1"
        };
    }

    private static OperationalOptions Clone(OperationalOptions options)
    {
        return new OperationalOptions
        {
            Hours = new OperationalHoursOptions
            {
                HoraAvance1 = options.Hours.HoraAvance1,
                HoraAvance2 = options.Hours.HoraAvance2,
                HoraAvance3 = options.Hours.HoraAvance3,
                HoraParticipacion = options.Hours.HoraParticipacion,
                HoraAvance1Historico = options.Hours.HoraAvance1Historico,
                HoraAvance2Historico = options.Hours.HoraAvance2Historico,
                HoraAvance3Historico = options.Hours.HoraAvance3Historico,
                HoraParticipacionHistorico = options.Hours.HoraParticipacionHistorico
            },
            VideoSources = options.VideoSources
                .Select(x => new VideoSourceOptions
                {
                    Slot = x.Slot,
                    IsLive = x.IsLive,
                    Path = x.Path
                })
                .ToList()
        };
    }

    private static StorageOptions CloneStorage(StorageOptions options)
    {
        var byModule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Faldon"] = options.ResolveCsvFolderOverride("Faldon"),
            ["Carton"] = options.ResolveCsvFolderOverride("Carton"),
            ["Superfaldon"] = options.ResolveCsvFolderOverride("Superfaldon")
        };

        var srcFiles = options.CartonCsvFiles ?? new CartonCsvFilesOptions();
        return new StorageOptions
        {
            CsvFolder = options.CsvFolder?.Trim() ?? "Data/CSV",
            JsonFolder = options.JsonFolder?.Trim() ?? "Data/JSON",
            BrainStormCsvFormat = options.BrainStormCsvFormat,
            EnableRecuentosJson = options.EnableRecuentosJson,
            RecuentosJsonFileName = options.RecuentosJsonFileName,
            CsvFoldersByModule = byModule,
            CartonCsvFiles = new CartonCsvFilesOptions
            {
                Delimiter = string.IsNullOrEmpty(srcFiles.Delimiter) ? ";" : srcFiles.Delimiter,
                DatosPartidosFileName = srcFiles.DatosPartidosFileName?.Trim() ?? "datos_partidos.csv",
                MayoriasProvinciasFileName = srcFiles.MayoriasProvinciasFileName?.Trim() ?? "mayorias_provincias.csv",
                DatosSondeoFileName = srcFiles.DatosSondeoFileName?.Trim() ?? "datos_sondeo.csv",
                SondeoProvinciasFileName = srcFiles.SondeoProvinciasFileName?.Trim() ?? "sondeo_provincias.csv"
            }
        };
    }

    private static DatabaseOptions CloneDatabase(DatabaseOptions options)
    {
        return new DatabaseOptions
        {
            Enabled = options.Enabled,
            Host = string.IsNullOrWhiteSpace(options.Host) ? "127.0.0.1" : options.Host.Trim(),
            HostBackup = options.HostBackup?.Trim() ?? string.Empty,
            HostLocal = string.IsNullOrWhiteSpace(options.HostLocal) ? "127.0.0.1" : options.HostLocal.Trim(),
            Port = options.Port > 0 ? options.Port : 3306,
            User = options.User?.Trim() ?? string.Empty,
            Password = options.Password ?? string.Empty,
            CommandTimeoutSeconds = options.CommandTimeoutSeconds > 0 ? options.CommandTimeoutSeconds : 5,
            NumEleccionesSimultaneas = Math.Clamp(options.NumEleccionesSimultaneas, 1, 3),
            DefaultElectionSlot = Math.Clamp(options.DefaultElectionSlot, 1, 3),
            Label1 = options.GetElectionLabel(1),
            Label2 = options.GetElectionLabel(2),
            Label3 = options.GetElectionLabel(3),
            Name = options.Name?.Trim() ?? string.Empty,
            Name2 = options.Name2?.Trim() ?? string.Empty,
            Name3 = options.Name3?.Trim() ?? string.Empty,
            ConnectionDefault1 = options.ConnectionDefault1 is >= 1 and <= 3 ? options.ConnectionDefault1 : 1,
            ConnectionDefault2 = options.ConnectionDefault2 is >= 1 and <= 3 ? options.ConnectionDefault2 : 1,
            ConnectionDefault3 = options.ConnectionDefault3 is >= 1 and <= 3 ? options.ConnectionDefault3 : 1,
            TipoElecciones = options.TipoElecciones is 1 or 2 ? options.TipoElecciones : 1,
            Regional = options.Regional,
            CodigoRegionalBD1 = options.CodigoRegionalBD1?.Trim() ?? string.Empty,
            CodigoRegionalBD2 = options.CodigoRegionalBD2?.Trim() ?? string.Empty,
            CodigoRegionalBD3 = options.CodigoRegionalBD3?.Trim() ?? string.Empty
        };
    }

    private static async Task<EndpointConnectivityStatus> CheckEndpointAsync(
        string name,
        GraphicsEndpointOptions endpoint,
        CancellationToken cancellationToken)
    {
        var host = endpoint.Host?.Trim() ?? string.Empty;
        var port = endpoint.Port;
        if (!endpoint.Enabled)
        {
            return new EndpointConnectivityStatus(name, false, host, port, false, "Desactivado");
        }

        if (string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            return new EndpointConnectivityStatus(name, true, host, port, false, "Host o puerto inválido");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return new EndpointConnectivityStatus(name, true, host, port, true, "Conectado");
        }
        catch (Exception ex)
        {
            return new EndpointConnectivityStatus(name, true, host, port, false, ex.Message);
        }
    }
}
