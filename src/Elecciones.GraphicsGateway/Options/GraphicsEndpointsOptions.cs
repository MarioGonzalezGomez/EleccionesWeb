using Elecciones.Application.Models;

namespace Elecciones.GraphicsGateway.Options;

public sealed class GraphicsEndpointsOptions
{
    public const string SectionName = "GraphicsEndpoints";

    public GraphicsEndpointOptions Ipf { get; set; } = new();
    public GraphicsEndpointOptions Prime { get; set; } = new();
    public GraphicsEndpointOptions Ipf2 { get; set; } = new();
    public bool SendToIpf2WithIpf { get; set; }
    public Dictionary<string, GraphicsEndpointOverrideOptions> IpfByModule { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public GraphicsEndpointOptions ResolveIpfForModule(GraphicModule? module)
    {
        if (module.HasValue
            && IpfByModule is not null
            && IpfByModule.TryGetValue(module.Value.ToString(), out var overrideOptions)
            && overrideOptions is not null)
        {
            return new GraphicsEndpointOptions
            {
                Enabled = overrideOptions.Enabled ?? Ipf.Enabled,
                Host = !string.IsNullOrWhiteSpace(overrideOptions.Host) ? overrideOptions.Host.Trim() : Ipf.Host,
                Port = overrideOptions.Port is > 0 ? overrideOptions.Port.Value : Ipf.Port,
                Bd = !string.IsNullOrWhiteSpace(overrideOptions.Bd) ? overrideOptions.Bd.Trim() : Ipf.Bd
            };
        }

        return Ipf;
    }
}

public sealed class GraphicsEndpointOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string Bd { get; set; } = "dbs1";
}

public sealed class GraphicsEndpointOverrideOptions
{
    public bool? Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string Bd { get; set; } = string.Empty;
}
