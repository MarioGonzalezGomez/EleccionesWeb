using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperatorAuthorizationService
{
    bool CanOperate(string operatorId, GraphicModule module);
    string GetRole(string operatorId);
    IReadOnlyList<GraphicModule> GetAllowedModules(string operatorId);
}
