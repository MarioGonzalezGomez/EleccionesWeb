using System.Security.Cryptography;
using System.Text;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;

namespace EleccionesWeb.Services;

public sealed class ConfiguredOperatorAuthorizationService :
    IOperatorAuthorizationService,
    IOperatorAuthenticationService
{
    private static readonly IReadOnlyList<GraphicModule> AllModules = Enum.GetValues<GraphicModule>().ToList();
    private static readonly HashSet<OperationActionType> AllActions = Enum.GetValues<OperationActionType>().ToHashSet();
    private static readonly HashSet<GraphicCommand> AllCommands = Enum.GetValues<GraphicCommand>().ToHashSet();

    private readonly Dictionary<string, OperatorGrant> _grants;
    private readonly bool _allowAllWhenEmpty;
    public bool AutoLoginByWorkspace { get; }

    public ConfiguredOperatorAuthorizationService(IConfiguration configuration)
    {
        var config = new OperatorAuthorizationConfig();
        configuration.GetSection("OperatorAuthorization").Bind(config);

        _allowAllWhenEmpty = config.AllowAllWhenEmpty;
        AutoLoginByWorkspace = config.AutoLoginByWorkspace;
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

            if (modules.Count == 0)
            {
                modules = AllModules.ToList();
            }

            var byModule = CreateDefaultModuleGrants(modules);

            foreach (var permission in entry.Permissions)
            {
                var parsedModule = TryParseModule(permission.Module);
                if (!parsedModule.HasValue)
                {
                    continue;
                }

                var module = parsedModule.Value;
                var actions = permission.Actions
                    .Select(TryParseAction)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToHashSet();

                var commands = permission.Commands
                    .Select(TryParseCommand)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToHashSet();

                byModule[module] = new ModulePermissionGrant(
                    actions.Count == 0 ? AllActions.ToHashSet() : actions,
                    commands.Count == 0 ? AllCommands.ToHashSet() : commands);
            }

            _grants[operatorId] = new OperatorGrant(
                operatorId,
                string.IsNullOrWhiteSpace(entry.Role) ? "operator" : entry.Role.Trim(),
                modules,
                byModule,
                entry.Password?.Trim() ?? string.Empty,
                entry.PasswordSha256?.Trim() ?? string.Empty,
                entry.PasswordEnvVar?.Trim() ?? string.Empty);
        }

        EnsureWorkspaceDefaultGrants();
    }

    private void EnsureWorkspaceDefaultGrants()
    {
        if (!_grants.ContainsKey(WorkspaceResolver.DefaultFaldonOperator))
        {
            var modules = new List<GraphicModule> { GraphicModule.Faldon };
            _grants[WorkspaceResolver.DefaultFaldonOperator] = new OperatorGrant(
                WorkspaceResolver.DefaultFaldonOperator,
                "faldon",
                modules,
                CreateDefaultModuleGrants(modules),
                WorkspaceResolver.DefaultFaldonOperator,
                string.Empty,
                string.Empty);
        }

        if (!_grants.ContainsKey(WorkspaceResolver.DefaultCartonOperator))
        {
            var modules = new List<GraphicModule> { GraphicModule.Carton };
            _grants[WorkspaceResolver.DefaultCartonOperator] = new OperatorGrant(
                WorkspaceResolver.DefaultCartonOperator,
                "carton",
                modules,
                CreateDefaultModuleGrants(modules),
                WorkspaceResolver.DefaultCartonOperator,
                string.Empty,
                string.Empty);
        }

        if (!_grants.ContainsKey(WorkspaceResolver.DefaultAdminOperator))
        {
            var modules = AllModules.ToList();
            _grants[WorkspaceResolver.DefaultAdminOperator] = new OperatorGrant(
                WorkspaceResolver.DefaultAdminOperator,
                "admin",
                modules,
                CreateDefaultModuleGrants(modules),
                WorkspaceResolver.DefaultAdminOperator,
                string.Empty,
                string.Empty);
        }
    }

    private static Dictionary<GraphicModule, ModulePermissionGrant> CreateDefaultModuleGrants(IEnumerable<GraphicModule> modules)
    {
        var byModule = new Dictionary<GraphicModule, ModulePermissionGrant>();
        foreach (var module in modules)
        {
            byModule[module] = new ModulePermissionGrant(
                AllActions.ToHashSet(),
                AllCommands.ToHashSet());
        }

        return byModule;
    }

    public bool ValidateCredentials(string operatorId, string password)
    {
        if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var grant = ResolveGrant(operatorId);
        if (grant is null)
        {
            return _allowAllWhenEmpty && _grants.Count == 0;
        }

        var configuredPassword = ResolveConfiguredPassword(grant);
        if (!string.IsNullOrWhiteSpace(grant.PasswordSha256))
        {
            var hash = ComputeSha256(password.Trim());
            return string.Equals(hash, grant.PasswordSha256, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            return string.Equals(configuredPassword, password, StringComparison.Ordinal);
        }

        return false;
    }

    public bool CanOperate(string operatorId, GraphicModule module)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.Modules.Contains(module);
        }

        return _allowAllWhenEmpty;
    }

    public bool CanExecuteAction(string operatorId, GraphicModule module, OperationActionType action)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.ByModule.TryGetValue(module, out var permission)
                   && permission.Actions.Contains(action);
        }

        return _allowAllWhenEmpty;
    }

    public bool CanExecuteCommand(string operatorId, GraphicModule module, GraphicCommand command)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.ByModule.TryGetValue(module, out var permission)
                   && permission.Commands.Contains(command);
        }

        return _allowAllWhenEmpty;
    }

    public string GetRole(string operatorId)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.Role;
        }

        return _allowAllWhenEmpty ? "admin" : "viewer";
    }

    public IReadOnlyList<GraphicModule> GetAllowedModules(string operatorId)
    {
        var grant = ResolveGrant(operatorId);
        if (grant is not null)
        {
            return grant.ModuleList;
        }

        return _allowAllWhenEmpty ? AllModules : [];
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

    private static string ResolveConfiguredPassword(OperatorGrant grant)
    {
        if (!string.IsNullOrWhiteSpace(grant.PasswordEnvVar))
        {
            var fromEnv = Environment.GetEnvironmentVariable(grant.PasswordEnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }
        }

        return grant.Password;
    }

    private static string ComputeSha256(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
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

    private static OperationActionType? TryParseAction(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<OperationActionType>(raw, true, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static GraphicCommand? TryParseCommand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (Enum.TryParse<GraphicCommand>(raw, true, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record OperatorGrant(
        string OperatorId,
        string Role,
        List<GraphicModule> ModuleList,
        Dictionary<GraphicModule, ModulePermissionGrant> ByModule,
        string Password,
        string PasswordSha256,
        string PasswordEnvVar)
    {
        public HashSet<GraphicModule> Modules { get; } = ModuleList.ToHashSet();
    }

    private sealed record ModulePermissionGrant(
        HashSet<OperationActionType> Actions,
        HashSet<GraphicCommand> Commands);

    private sealed class OperatorAuthorizationConfig
    {
        public bool AllowAllWhenEmpty { get; set; }
        public bool AutoLoginByWorkspace { get; set; } = true;
        public List<OperatorEntry> Operators { get; set; } = [];
    }

    private sealed class OperatorEntry
    {
        public string OperatorId { get; set; } = string.Empty;
        public string Role { get; set; } = "operator";
        public string Password { get; set; } = string.Empty;
        public string PasswordSha256 { get; set; } = string.Empty;
        public string PasswordEnvVar { get; set; } = string.Empty;
        public List<string> Modules { get; set; } = [];
        public List<ModulePermissionEntry> Permissions { get; set; } = [];
    }

    private sealed class ModulePermissionEntry
    {
        public string Module { get; set; } = string.Empty;
        public List<string> Actions { get; set; } = [];
        public List<string> Commands { get; set; } = [];
    }
}
