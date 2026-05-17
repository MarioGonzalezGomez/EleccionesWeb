using System.Diagnostics;

var targetUrl = ResolveTargetUrl(args);

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

static string ResolveTargetUrl(string[] args)
{
    if (args.Length > 0 && Uri.IsWellFormedUriString(args[0], UriKind.Absolute))
    {
        return args[0];
    }

    var fromEnv = Environment.GetEnvironmentVariable("ELECCIONES_WEB_URL");
    if (!string.IsNullOrWhiteSpace(fromEnv) && Uri.IsWellFormedUriString(fromEnv, UriKind.Absolute))
    {
        return fromEnv;
    }

    return "http://localhost:5177/operacion";
}
