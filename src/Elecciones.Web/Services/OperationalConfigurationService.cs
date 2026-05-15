using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elecciones.GraphicsGateway.Options;
using Elecciones.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace EleccionesWeb.Services;

public sealed class OperationalConfigurationService : IOperationalConfigurationService
{
    private readonly IOptionsMonitor<GraphicsEndpointsOptions> _graphicsMonitor;
    private readonly IOptionsMonitor<OperationalOptions> _operationalMonitor;
    private readonly IWebHostEnvironment _environment;

    public OperationalConfigurationService(
        IOptionsMonitor<GraphicsEndpointsOptions> graphicsMonitor,
        IOptionsMonitor<OperationalOptions> operationalMonitor,
        IWebHostEnvironment environment)
    {
        _graphicsMonitor = graphicsMonitor;
        _operationalMonitor = operationalMonitor;
        _environment = environment;
    }

    public OperationalConfigurationSnapshot GetSnapshot()
    {
        return new OperationalConfigurationSnapshot
        {
            GraphicsEndpoints = Clone(_graphicsMonitor.CurrentValue),
            Operational = NormalizeOperational(Clone(_operationalMonitor.CurrentValue))
        };
    }

    public async Task<IReadOnlyList<EndpointConnectivityStatus>> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = _graphicsMonitor.CurrentValue;
        return
        [
            await CheckEndpointAsync("IPF", endpoints.Ipf, cancellationToken),
            await CheckEndpointAsync("PRIME", endpoints.Prime, cancellationToken),
            await CheckEndpointAsync("IPF2", endpoints.Ipf2, cancellationToken)
        ];
    }

    public async Task<SaveOperationalConfigurationResult> SaveAsync(
        OperationalConfigurationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var targetPath = ResolveSettingsFilePath();
        try
        {
            var graphics = Clone(snapshot.GraphicsEndpoints);
            var operational = NormalizeOperational(Clone(snapshot.Operational));

            var json = await File.ReadAllTextAsync(targetPath, cancellationToken);
            var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            var graphicsSection = root["GraphicsEndpoints"]?.AsObject() ?? new JsonObject();
            graphicsSection["SendToIpf2WithIpf"] = graphics.SendToIpf2WithIpf;
            graphicsSection["Ipf"] = BuildEndpointNode(graphics.Ipf);
            graphicsSection["Prime"] = BuildEndpointNode(graphics.Prime);
            graphicsSection["Ipf2"] = BuildEndpointNode(graphics.Ipf2);
            root["GraphicsEndpoints"] = graphicsSection;

            var operationalSection = root["Operational"]?.AsObject() ?? new JsonObject();
            operationalSection["Hours"] = BuildHoursNode(operational.Hours);
            operationalSection["VideoSources"] = BuildVideoSourcesNode(operational.VideoSources);
            root["Operational"] = operationalSection;

            var output = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(targetPath, output, cancellationToken);

            return new SaveOperationalConfigurationResult(
                true,
                $"Configuracion guardada en {Path.GetFileName(targetPath)}.",
                targetPath);
        }
        catch (Exception ex)
        {
            return new SaveOperationalConfigurationResult(
                false,
                $"Error guardando configuracion: {ex.Message}",
                targetPath);
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
        return new GraphicsEndpointsOptions
        {
            SendToIpf2WithIpf = options.SendToIpf2WithIpf,
            Ipf = CloneEndpoint(options.Ipf),
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
            return new EndpointConnectivityStatus(name, true, host, port, false, "Host o puerto invalido");
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
