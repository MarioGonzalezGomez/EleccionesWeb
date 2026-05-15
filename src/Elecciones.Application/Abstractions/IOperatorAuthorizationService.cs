using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperatorAuthorizationService
{
    bool CanOperate(string operatorId, GraphicModule module);
    bool CanExecuteAction(string operatorId, GraphicModule module, OperationActionType action);
    bool CanExecuteCommand(string operatorId, GraphicModule module, GraphicCommand command);
    string GetRole(string operatorId);
    IReadOnlyList<GraphicModule> GetAllowedModules(string operatorId);
}
