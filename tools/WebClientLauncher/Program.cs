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
