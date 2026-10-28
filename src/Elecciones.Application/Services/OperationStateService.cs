using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class OperationStateService : IOperationStateService
{
    private readonly object _sync = new();
    private readonly Dictionary<GraphicModule, MutableModuleState> _states = new()
    {
        [GraphicModule.Faldon] = new MutableModuleState(GraphicModule.Faldon),
        [GraphicModule.Carton] = new MutableModuleState(GraphicModule.Carton),
        [GraphicModule.Superfaldon] = new MutableModuleState(GraphicModule.Superfaldon)
    };

    public event Action<GraphicModule, ModuleOperationalState>? OnStateChanged;

    public ModuleOperationalState GetState(GraphicModule module)
    {
        lock (_sync)
        {
            return GetOrCreate(module).ToSnapshot();
        }
    }

    public IReadOnlyList<ModuleOperationalState> GetAllStates()
    {
        lock (_sync)
        {
            return _states.Values
                .OrderBy(s => s.Module)
                .Select(s => s.ToSnapshot())
                .ToList();
        }
    }

    public void RecordExecution(OperationRequest request, BrainStormSnapshot snapshot)
    {
        ModuleOperationalState updatedSnapshot;
        lock (_sync)
        {
            var state = GetOrCreate(request.Module);
            state.LastOperatorId = request.OperatorId;
            state.Oficiales = request.Oficiales;
            state.Target = request.Target;
            state.ActiveQuery = request.Query;
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (request.Action == OperationActionType.Reset)
            {
                state.Reset();
            }
            else if (request.Command != GraphicCommand.None)
            {
                ApplyCommandState(state, request);
            }
            else
            {
                var scene = !string.IsNullOrWhiteSpace(request.Scene)
                    ? request.Scene.Trim()
                    : ResolveDefaultScene(request.Module, request.Oficiales);

                switch (request.Action)
                {
                    case OperationActionType.Enter:
                        state.IsOnAir = true;
                        state.ActiveScene = scene;
                        break;
                    case OperationActionType.Update:
                        if (string.IsNullOrWhiteSpace(state.ActiveScene))
                        {
                            state.ActiveScene = scene;
                        }
                        break;
                    case OperationActionType.Exit:
                        state.IsOnAir = false;
                        state.ActiveScene = string.Empty;
                        state.IsSedeExpanding = false;
                        state.ExpandedVideos.Clear();
                        break;
                }
            }

            updatedSnapshot = state.ToSnapshot();
        }

        OnStateChanged?.Invoke(request.Module, updatedSnapshot);
    }

    private static void ApplyCommandState(MutableModuleState state, OperationRequest request)
    {
        switch (request.Command)
        {
            case GraphicCommand.VideoIn:
                if (!string.IsNullOrWhiteSpace(request.CommandValue)
                    && !state.ExpandedVideos.Contains(request.CommandValue, StringComparer.OrdinalIgnoreCase))
                {
                    state.ExpandedVideos.Add(request.CommandValue.Trim());
                }
                break;

            case GraphicCommand.VideoOut:
                if (!string.IsNullOrWhiteSpace(request.CommandValue))
                {
                    state.ExpandedVideos.RemoveAll(v => string.Equals(v, request.CommandValue.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                break;

            case GraphicCommand.VideoOutTodos:
                state.ExpandedVideos.Clear();
                break;

            case GraphicCommand.SedesEntra:
                state.IsSedeExpanding = true;
                break;

            case GraphicCommand.SedesSale:
                state.IsSedeExpanding = false;
                break;

            case GraphicCommand.PactosEntra:
                state.IsOnAir = true;
                state.ActiveScene = "PACTOS";
                break;

            case GraphicCommand.PactosSale:
                state.PactosIzquierda.Clear();
                state.PactosDerecha.Clear();
                break;

            case GraphicCommand.PactosReinicio:
                state.PactosIzquierda.Clear();
                state.PactosDerecha.Clear();
                break;

            case GraphicCommand.PactosEntraIzquierda:
                if (!string.IsNullOrWhiteSpace(request.CommandValue)
                    && !state.PactosIzquierda.Contains(request.CommandValue.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    state.PactosIzquierda.Add(request.CommandValue.Trim());
                }
                break;

            case GraphicCommand.PactosEntraDerecha:
                if (!string.IsNullOrWhiteSpace(request.CommandValue)
                    && !state.PactosDerecha.Contains(request.CommandValue.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    state.PactosDerecha.Add(request.CommandValue.Trim());
                }
                break;

            case GraphicCommand.PactosSaleIzquierda:
                if (state.PactosIzquierda.Count > 0)
                {
                    state.PactosIzquierda.RemoveAt(state.PactosIzquierda.Count - 1);
                }
                break;

            case GraphicCommand.PactosSaleDerecha:
                if (state.PactosDerecha.Count > 0)
                {
                    state.PactosDerecha.RemoveAt(state.PactosDerecha.Count - 1);
                }
                break;
        }
    }

    private static string ResolveDefaultScene(GraphicModule module, bool oficiales)
    {
        return module switch
        {
            GraphicModule.Faldon => oficiales ? "Escrutinio" : "Sondeo",
            GraphicModule.Carton => "CARTON_PARTIDOS",
            GraphicModule.Superfaldon => "SUPERFALDON",
            _ => "CONTROL"
        };
    }

    private MutableModuleState GetOrCreate(GraphicModule module)
    {
        if (_states.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new MutableModuleState(module);
        _states[module] = state;
        return state;
    }

    private sealed class MutableModuleState(GraphicModule module)
    {
        public GraphicModule Module { get; } = module;
        public bool IsOnAir { get; set; }
        public string ActiveScene { get; set; } = string.Empty;
        public SnapshotQuery? ActiveQuery { get; set; }
        public bool Oficiales { get; set; } = true;
        public GraphicsTarget Target { get; set; } = GraphicsTarget.Both;
        public string LastOperatorId { get; set; } = string.Empty;
        public bool IsSedeExpanding { get; set; }
        public List<string> ExpandedVideos { get; } = [];
        public List<string> PactosIzquierda { get; } = [];
        public List<string> PactosDerecha { get; } = [];
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public void Reset()
        {
            IsOnAir = false;
            ActiveScene = string.Empty;
            IsSedeExpanding = false;
            ExpandedVideos.Clear();
            PactosIzquierda.Clear();
            PactosDerecha.Clear();
        }

        public ModuleOperationalState ToSnapshot() => new()
        {
            Module = Module,
            IsOnAir = IsOnAir,
            ActiveScene = ActiveScene,
            ActiveQuery = ActiveQuery,
            Oficiales = Oficiales,
            Target = Target,
            LastOperatorId = LastOperatorId,
            IsSedeExpanding = IsSedeExpanding,
            ExpandedVideos = ExpandedVideos.ToList(),
            PactosIzquierda = PactosIzquierda.ToList(),
            PactosDerecha = PactosDerecha.ToList(),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

