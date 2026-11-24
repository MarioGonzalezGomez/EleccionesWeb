using System.Diagnostics;
using System.Net.Sockets;

var targetUrl = ResolveTargetUrl(args);
var uri = new Uri(targetUrl);

await EnsureServerRunningAsync(uri);

Console.WriteLine($"Abriendo cliente web en: {targetUrl}");

try
{
    Process.Start(new ProcessStartInfo
    {
        FileName = targetUrl,
        UseShellExecute = true
    });

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"No se pudo abrir el navegador: {ex.Message}");
    return 1;
}

static async Task EnsureServerRunningAsync(Uri targetUri)
{
    if (await IsPortOpenAsync("127.0.0.1", targetUri.Port))
    {
        return;
    }

    var serverExe = FindServerExecutable();
    if (serverExe is null)
    {
        Console.WriteLine($"Aviso: El servidor no responde en el puerto {targetUri.Port} y no se encontró EleccionesWeb.exe automáticamente.");
        return;
    }

    var workingDir = ResolveServerWorkingDirectory(serverExe);
    Console.WriteLine($"Iniciando servidor web ({serverExe})...");

    Process.Start(new ProcessStartInfo
    {
        FileName = serverExe,
        WorkingDirectory = workingDir,
        UseShellExecute = true,
        WindowStyle = ProcessWindowStyle.Normal
    });

    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
        if (await IsPortOpenAsync("127.0.0.1", targetUri.Port))
        {
            return;
        }

        await Task.Delay(250);
    }
}

static async Task<bool> IsPortOpenAsync(string host, int port)
{
    try
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await client.ConnectAsync(host, port, cts.Token);
        return true;
    }
    catch
    {
        return false;
    }
}

static string? FindServerExecutable()
{
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.GetFullPath(Path.Combine(baseDir, "EleccionesWeb.exe")),
        Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Elecciones.Web", "bin", "Debug", "net8.0", "EleccionesWeb.exe")),
        Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Elecciones.Web", "bin", "Release", "net8.0", "EleccionesWeb.exe"))
    };

    return candidates.FirstOrDefault(File.Exists);
}

static string ResolveServerWorkingDirectory(string serverExePath)
{
    var exeDir = Path.GetDirectoryName(serverExePath)!;
    var projectDirCandidate = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
    if (File.Exists(Path.Combine(projectDirCandidate, "appsettings.json")))
    {
        return projectDirCandidate;
    }

    return exeDir;
}

static string ResolveTargetUrl(string[] args)
{
    var baseUrl = Environment.GetEnvironmentVariable("ELECCIONES_WEB_URL");
    if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
    {
        baseUrl = "http://localhost:5177";
    }
    baseUrl = baseUrl.TrimEnd('/');

    if (args.Length > 0)
    {
        var arg = args[0].Trim();
        if (Uri.IsWellFormedUriString(arg, UriKind.Absolute))
        {
            return arg;
        }

        return arg.ToLowerInvariant() switch
        {
            "faldon" or "faldón" => $"{baseUrl}/faldon",
            "carton" or "cartón" => $"{baseUrl}/carton",
            "admin" or "control" => $"{baseUrl}/admin",
            _ => $"{baseUrl}/admin"
        };
    }

    return $"{baseUrl}/admin";
}
