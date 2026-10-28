using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperationStateService
{
    event Action<GraphicModule, ModuleOperationalState>? OnStateChanged;

    ModuleOperationalState GetState(GraphicModule module);
    IReadOnlyList<ModuleOperationalState> GetAllStates();
    void RecordExecution(OperationRequest request, BrainStormSnapshot snapshot);
}

