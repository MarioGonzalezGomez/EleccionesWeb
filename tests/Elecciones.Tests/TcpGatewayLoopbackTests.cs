using System.Net;
using System.Net.Sockets;
using System.Text;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.GraphicsGateway.Options;
using Elecciones.GraphicsGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elecciones.Tests;

public sealed class TcpGatewayLoopbackTests
{
    [Fact]
    public async Task SendAsync_WithModuleSpecificIpfOverride_SendsToModulePortAndRecordsDiagnosticEntry()
    {
        using var globalListener = new TcpListener(IPAddress.Loopback, 0);
        using var cartonListener = new TcpListener(IPAddress.Loopback, 0);
        globalListener.Start();
        cartonListener.Start();

        var globalPort = ((IPEndPoint)globalListener.LocalEndpoint).Port;
        var cartonPort = ((IPEndPoint)cartonListener.LocalEndpoint).Port;

        var options = new GraphicsEndpointsOptions
        {
            Ipf = new GraphicsEndpointOptions
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = globalPort,
                Bd = "DBS_GLOBAL"
            },
            IpfByModule = new Dictionary<string, GraphicsEndpointOverrideOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["Carton"] = new GraphicsEndpointOverrideOptions
                {
                    Enabled = true,
                    Host = "127.0.0.1",
                    Port = cartonPort,
                    Bd = "DBS_CARTON"
                }
            }
        };

        var diagnostics = new InMemorySignalDiagnosticsService();
        var monitor = new StaticOptionsMonitor<GraphicsEndpointsOptions>(options);
        var gateway = new TcpGraphicsGateway(monitor, NullLogger<TcpGraphicsGateway>.Instance, diagnostics);

        var acceptCartonTask = ReceiveSingleMessageAsync(cartonListener);
        var sentTargets = await gateway.SendAsync(
            GraphicsTarget.Ipf,
            "itemset('<{BD}>PARTICIPACION/ENTRA','EVENT_RUN');",
            CancellationToken.None,
            GraphicModule.Carton);

        var received = await acceptCartonTask;

        Assert.Contains("IPF", sentTargets);
        Assert.Equal("itemset('<DBS_CARTON>PARTICIPACION/ENTRA','EVENT_RUN');", received);

        var entries = diagnostics.GetRecentEntries(GraphicModule.Carton, "IPF");
        Assert.Single(entries);
        Assert.True(entries[0].Delivered);
        Assert.Equal(cartonPort, entries[0].Port);
        Assert.Equal("DBS_CARTON", entries[0].Bd);
    }

    [Fact]
    public async Task SendAsync_WhenEndpointDisabled_DoesNotConnectButRecordsDryRunDiagnosticEntry()
    {
        var options = new GraphicsEndpointsOptions
        {
            Ipf = new GraphicsEndpointOptions
            {
                Enabled = false,
                Host = "127.0.0.1",
                Port = 59999,
                Bd = "DBS_DRY"
            }
        };

        var diagnostics = new InMemorySignalDiagnosticsService();
        var monitor = new StaticOptionsMonitor<GraphicsEndpointsOptions>(options);
        var gateway = new TcpGraphicsGateway(monitor, NullLogger<TcpGraphicsGateway>.Instance, diagnostics);

        var sentTargets = await gateway.SendAsync(
            GraphicsTarget.Ipf,
            "itemset('<{BD}>TICKER/ENTRA','EVENT_RUN');",
            CancellationToken.None,
            GraphicModule.Faldon);

        Assert.Empty(sentTargets);

        var entries = diagnostics.GetRecentEntries(GraphicModule.Faldon, "IPF");
        Assert.Single(entries);
        Assert.False(entries[0].Delivered);
        Assert.Equal("itemset('<DBS_DRY>TICKER/ENTRA','EVENT_RUN');", entries[0].Payload);
    }

    private static async Task<string> ReceiveSingleMessageAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

