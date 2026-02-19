using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IModuleLockService
{
    event Action? LocksChanged;

    IReadOnlyList<ModuleLockInfo> GetStates();

    bool TryAcquire(GraphicModule module, string operatorId);

    bool Release(GraphicModule module, string operatorId);

    bool IsOwner(GraphicModule module, string operatorId);

    void MarkAction(GraphicModule module, string operatorId);
}
