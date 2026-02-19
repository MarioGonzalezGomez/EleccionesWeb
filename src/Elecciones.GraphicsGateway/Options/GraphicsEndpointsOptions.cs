namespace Elecciones.GraphicsGateway.Options;

public sealed class GraphicsEndpointsOptions
{
    public const string SectionName = "GraphicsEndpoints";

    public GraphicsEndpointOptions Ipf { get; set; } = new();
    public GraphicsEndpointOptions Prime { get; set; } = new();
}

public sealed class GraphicsEndpointOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
}
