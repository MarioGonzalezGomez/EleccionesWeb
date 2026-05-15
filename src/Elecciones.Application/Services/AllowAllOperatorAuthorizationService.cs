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

    public bool CanExecuteAction(string operatorId, GraphicModule module, OperationActionType action)
    {
        return true;
    }

    public bool CanExecuteCommand(string operatorId, GraphicModule module, GraphicCommand command)
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
