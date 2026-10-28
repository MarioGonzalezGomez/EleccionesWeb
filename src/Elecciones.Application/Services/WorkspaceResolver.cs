using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public static class WorkspaceResolver
{
    public const string DefaultFaldonOperator = "op_faldon";
    public const string DefaultCartonOperator = "op_carton";
    public const string DefaultAdminOperator = "op_admin";

    public static WorkspaceKind Resolve(string? host, string? path)
    {
        var normalizedPath = (path ?? string.Empty).Trim();

        // 1. Explicit path overrides (useful for fallback routes or local testing on same host)
        if (StartsWithSegment(normalizedPath, "/faldon")
            || StartsWithSegment(normalizedPath, "/operacion/faldon")
            || StartsWithSegment(normalizedPath, "/operacion/botonera")
            || StartsWithSegment(normalizedPath, "/operacion/pactos"))
        {
            return WorkspaceKind.Faldon;
        }

        if (StartsWithSegment(normalizedPath, "/carton")
            || StartsWithSegment(normalizedPath, "/operacion/carton"))
        {
            return WorkspaceKind.Carton;
        }

        if (StartsWithSegment(normalizedPath, "/admin")
            || StartsWithSegment(normalizedPath, "/control")
            || StartsWithSegment(normalizedPath, "/operacion/admin")
            || StartsWithSegment(normalizedPath, "/operacion/diagnostico"))
        {
            return WorkspaceKind.ControlGeneral;
        }

        // 2. Subdomain resolution (e.g., faldon.elecciones.local, carton.elecciones.local, admin.elecciones.local)
        var normalizedHost = (host ?? string.Empty).Trim();
        var colonIndex = normalizedHost.IndexOf(':');
        if (colonIndex >= 0)
        {
            normalizedHost = normalizedHost[..colonIndex];
        }

        if (normalizedHost.StartsWith("faldon.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedHost, "faldon", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceKind.Faldon;
        }

        if (normalizedHost.StartsWith("carton.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedHost, "carton", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceKind.Carton;
        }

        if (normalizedHost.StartsWith("admin.", StringComparison.OrdinalIgnoreCase)
            || normalizedHost.StartsWith("control.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedHost, "admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedHost, "control", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceKind.ControlGeneral;
        }

        return WorkspaceKind.ControlGeneral;
    }

    public static string GetDefaultOperatorId(WorkspaceKind workspace)
    {
        return workspace switch
        {
            WorkspaceKind.Faldon => DefaultFaldonOperator,
            WorkspaceKind.Carton => DefaultCartonOperator,
            _ => DefaultAdminOperator
        };
    }

    public static string GetDefaultRole(WorkspaceKind workspace)
    {
        return workspace switch
        {
            WorkspaceKind.Faldon => "faldon",
            WorkspaceKind.Carton => "carton",
            _ => "admin"
        };
    }

    public static string ResolveEffectiveOperatorId(
        WorkspaceKind workspace,
        string? authenticatedUserName,
        bool autoLoginByWorkspace)
    {
        if (autoLoginByWorkspace)
        {
            return GetDefaultOperatorId(workspace);
        }

        if (!string.IsNullOrWhiteSpace(authenticatedUserName))
        {
            return authenticatedUserName.Trim();
        }

        return GetDefaultOperatorId(workspace);
    }

    private static bool StartsWithSegment(string path, string segment)
    {
        if (!path.StartsWith(segment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == segment.Length || path[segment.Length] is '/' or '?';
    }
}

