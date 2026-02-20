using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class AllowAllOperatorAuthorizationService : IOperatorAuthorizationService
{
    private static readonly IReadOnlyList<GraphicModule> AllModules =
        Enum.GetValues<GraphicModule>().ToList();

    public bool CanOperate(string operatorId, GraphicModule module)
    {
        return true;
    }

    public string GetRole(string operatorId)
    {
        return "admin";
    }

    public IReadOnlyList<GraphicModule> GetAllowedModules(string operatorId)
    {
        return AllModules;
    }
}
