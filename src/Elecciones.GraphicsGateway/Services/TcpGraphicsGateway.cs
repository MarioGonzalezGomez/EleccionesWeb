using System.Net.Sockets;
using System.Text;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.GraphicsGateway.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elecciones.GraphicsGateway.Services;

public sealed class TcpGraphicsGateway : IGraphicsGateway
{
    private readonly GraphicsEndpointsOptions _options;
    private readonly ILogger<TcpGraphicsGateway> _logger;

    public TcpGraphicsGateway(IOptions<GraphicsEndpointsOptions> options, ILogger<TcpGraphicsGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SendAsync(
        GraphicsTarget target,
        string message,
        CancellationToken cancellationToken = default)
    {
        var successfulTargets = new List<string>();

        foreach (var endpoint in ResolveTargets(target))
        {
            if (!endpoint.Enabled)
            {
                continue;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, timeoutCts.Token);

                var payload = Encoding.UTF8.GetBytes(message);
                await client.GetStream().WriteAsync(payload, timeoutCts.Token);

                successfulTargets.Add(endpoint.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send TCP signal to {Name} ({Host}:{Port})",
                    endpoint.Name,
                    endpoint.Host,
                    endpoint.Port);
            }
        }

        return successfulTargets;
    }

    private IEnumerable<Endpoint> ResolveTargets(GraphicsTarget target)
    {
        return target switch
        {
            GraphicsTarget.Ipf => [new Endpoint("IPF", _options.Ipf.Enabled, _options.Ipf.Host, _options.Ipf.Port)],
            GraphicsTarget.Prime => [new Endpoint("PRIME", _options.Prime.Enabled, _options.Prime.Host, _options.Prime.Port)],
            GraphicsTarget.Both =>
            [
                new Endpoint("IPF", _options.Ipf.Enabled, _options.Ipf.Host, _options.Ipf.Port),
                new Endpoint("PRIME", _options.Prime.Enabled, _options.Prime.Host, _options.Prime.Port)
            ],
            _ => []
        };
    }

    private sealed record Endpoint(string Name, bool Enabled, string Host, int Port);
}
