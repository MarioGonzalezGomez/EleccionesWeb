using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperationStateService
{
    event Action<GraphicModule, ModuleOperationalState>? OnStateChanged;

    ModuleOperationalState GetState(GraphicModule module);
    IReadOnlyList<ModuleOperationalState> GetAllStates();
    ModuleOperationalState ConfigureModule(GraphicModule module, int electionSlot, bool oficiales);
    ModuleOperationalState SyncModuleSelection(
        GraphicModule module,
        string operatorId,
        CartonKind? cartonKind,
        SnapshotQuery query,
        bool oficiales,
        GraphicsTarget target,
        string exportName);
    void SyncAllModules(int electionSlot, bool oficiales);
    void RecordExecution(OperationRequest request, BrainStormSnapshot snapshot);
}

