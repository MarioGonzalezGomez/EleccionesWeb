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
    private readonly IOptionsMonitor<GraphicsEndpointsOptions> _optionsMonitor;
    private readonly ILogger<TcpGraphicsGateway> _logger;

    public TcpGraphicsGateway(IOptionsMonitor<GraphicsEndpointsOptions> optionsMonitor, ILogger<TcpGraphicsGateway> logger)
    {
        _optionsMonitor = optionsMonitor;
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
                var concreteMessage = ResolveMessageForEndpoint(message, endpoint.Bd);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                using var client = new TcpClient();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, timeoutCts.Token);

                var payload = Encoding.UTF8.GetBytes(concreteMessage);
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

    private static string ResolveMessageForEndpoint(string rawMessage, string bd)
    {
        var selectedBd = string.IsNullOrWhiteSpace(bd) ? "dbs1" : bd.Trim();
        return rawMessage.Replace("{BD}", selectedBd, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<Endpoint> ResolveTargets(GraphicsTarget target)
    {
        var options = _optionsMonitor.CurrentValue;

        return target switch
        {
            GraphicsTarget.Ipf => BuildIpfTargets(options),
            GraphicsTarget.Prime => [new Endpoint("PRIME", options.Prime.Enabled, options.Prime.Host, options.Prime.Port, options.Prime.Bd)],
            GraphicsTarget.Both =>
            [
                .. BuildIpfTargets(options),
                new Endpoint("PRIME", options.Prime.Enabled, options.Prime.Host, options.Prime.Port, options.Prime.Bd)
            ],
            _ => []
        };
    }

    private static IEnumerable<Endpoint> BuildIpfTargets(GraphicsEndpointsOptions options)
    {
        var targets = new List<Endpoint>
        {
            new("IPF", options.Ipf.Enabled, options.Ipf.Host, options.Ipf.Port, options.Ipf.Bd)
        };

        if (options.SendToIpf2WithIpf)
        {
            targets.Add(new Endpoint("IPF2", options.Ipf2.Enabled, options.Ipf2.Host, options.Ipf2.Port, options.Ipf2.Bd));
        }

        return targets;
    }

    private sealed record Endpoint(string Name, bool Enabled, string Host, int Port, string Bd);
}
