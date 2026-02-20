using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace EleccionesWeb.Services;

public sealed class ConfiguredOperatorAuthorizationService : IOperatorAuthorizationService
{
    private static readonly IReadOnlyList<GraphicModule> AllModules = Enum.GetValues<GraphicModule>().ToList();

    private readonly Dictionary<string, OperatorGrant> _grants;
    private readonly bool _allowAllWhenEmpty;

    public ConfiguredOperatorAuthorizationService(IConfiguration configuration)
    {
        var config = new OperatorAuthorizationConfig();
        configuration.GetSection("OperatorAuthorization").Bind(config);

        _allowAllWhenEmpty = config.AllowAllWhenEmpty;
        _grants = new Dictionary<string, OperatorGrant>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in config.Operators)
        {
            var operatorId = entry.OperatorId?.Trim();
            if (string.IsNullOrWhiteSpace(operatorId))
            {
                continue;
            }

            var modules = entry.Modules
                .Select(TryParseModule)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            _grants[operatorId] = new OperatorGrant(
                string.IsNullOrWhiteSpace(entry.Role) ? "operator" : entry.Role.Trim(),
                modules);
        }
    }

    public bool CanOperate(string operatorId, GraphicModule module)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.Modules.Contains(module);
        }

        return _allowAllWhenEmpty && _grants.Count == 0;
    }

    public string GetRole(string operatorId)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.Role;
        }

        return _allowAllWhenEmpty && _grants.Count == 0 ? "admin" : "viewer";
    }

    public IReadOnlyList<GraphicModule> GetAllowedModules(string operatorId)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.ModuleList;
        }

        return _allowAllWhenEmpty && _grants.Count == 0 ? AllModules : [];
    }

    private OperatorGrant? ResolveGrant(string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return null;
        }

        _grants.TryGetValue(operatorId.Trim(), out var grant);
        return grant;
    }

    private static GraphicModule? TryParseModule(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<GraphicModule>(raw, true, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record OperatorGrant(string Role, List<GraphicModule> ModuleList)
    {
        public HashSet<GraphicModule> Modules { get; } = ModuleList.ToHashSet();
    }

    private sealed class OperatorAuthorizationConfig
    {
        public bool AllowAllWhenEmpty { get; set; } = true;
        public List<OperatorEntry> Operators { get; set; } = [];
    }

    private sealed class OperatorEntry
    {
        public string OperatorId { get; set; } = string.Empty;
        public string Role { get; set; } = "operator";
        public List<string> Modules { get; set; } = [];
    }
}
