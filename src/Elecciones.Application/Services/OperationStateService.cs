using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class OperationStateService : IOperationStateService
{
    private readonly object _sync = new();
    private readonly int _defaultElectionSlot;
    private readonly bool _defaultOficiales;
    private readonly Dictionary<GraphicModule, MutableModuleState> _states;

    public OperationStateService(int defaultElectionSlot = 1, bool defaultOficiales = true)
    {
        _defaultElectionSlot = Math.Clamp(defaultElectionSlot, 1, 3);
        _defaultOficiales = defaultOficiales;
        _states = new Dictionary<GraphicModule, MutableModuleState>
        {
            [GraphicModule.Faldon] = new MutableModuleState(GraphicModule.Faldon, _defaultElectionSlot, _defaultOficiales),
            [GraphicModule.Carton] = new MutableModuleState(GraphicModule.Carton, _defaultElectionSlot, _defaultOficiales),
            [GraphicModule.Superfaldon] = new MutableModuleState(GraphicModule.Superfaldon, _defaultElectionSlot, _defaultOficiales)
        };
    }

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

    public ModuleOperationalState ConfigureModule(GraphicModule module, int electionSlot, bool oficiales)
    {
        var clampedSlot = Math.Clamp(electionSlot, 1, 3);
        ModuleOperationalState snapshot;
        var changed = false;

        lock (_sync)
        {
            var state = GetOrCreate(module);
            if (state.ElectionSlot != clampedSlot || state.Oficiales != oficiales)
            {
                state.ElectionSlot = clampedSlot;
                state.Oficiales = oficiales;
                if (!state.IsOnAir && state.ActiveQuery is not null)
                {
                    state.ActiveQuery = new SnapshotQuery
                    {
                        ElectionSlot = clampedSlot,
                        ConnectionType = state.ActiveQuery.ConnectionType,
                        Kind = state.ActiveQuery.Kind,
                        CircunscripcionCodigo = state.ActiveQuery.CircunscripcionCodigo,
                        AutonomiaCodigo = state.ActiveQuery.AutonomiaCodigo,
                        PartidoCodigo = state.ActiveQuery.PartidoCodigo,
                        Avance = state.ActiveQuery.Avance,
                        IncludeZeroSeats = state.ActiveQuery.IncludeZeroSeats,
                        MedioCodigo = state.ActiveQuery.MedioCodigo
                    };
                }

                state.UpdatedAtUtc = DateTimeOffset.UtcNow;
                changed = true;
            }

            snapshot = state.ToSnapshot();
        }

        if (changed)
        {
            OnStateChanged?.Invoke(module, snapshot);
        }

        return snapshot;
    }

    public ModuleOperationalState SyncModuleSelection(
        GraphicModule module,
        string operatorId,
        CartonKind? cartonKind,
        SnapshotQuery query,
        bool oficiales,
        GraphicsTarget target,
        string exportName)
    {
        ModuleOperationalState snapshot;
        lock (_sync)
        {
            var state = GetOrCreate(module);
            if (!string.IsNullOrWhiteSpace(operatorId))
            {
                state.LastOperatorId = operatorId.Trim();
            }

            state.PreviewQuery = query;

            // Mientras el módulo está EN AIRE (Program), preservamos ActiveQuery, CartonKind y Oficiales de emisión
            // para que un cambio de previsualización en UI no mute el gráfico en directo durante un auto-refresh de BD.
            if (!state.IsOnAir)
            {
                if (module == GraphicModule.Carton && cartonKind.HasValue)
                {
                    state.CartonKind = cartonKind.Value;
                }

                state.ActiveQuery = query;
                state.ElectionSlot = Math.Clamp(query?.ElectionSlot ?? state.ElectionSlot, 1, 3);
                state.Oficiales = oficiales;
                state.Target = target;
                if (!string.IsNullOrWhiteSpace(exportName))
                {
                    state.ExportName = exportName.Trim();
                }
            }

            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            snapshot = state.ToSnapshot();
        }

        return snapshot;
    }

    public void SyncAllModules(int electionSlot, bool oficiales)
    {
        foreach (var module in Enum.GetValues<GraphicModule>())
        {
            ConfigureModule(module, electionSlot, oficiales);
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
            state.ElectionSlot = Math.Clamp(request.Query?.ElectionSlot ?? state.ElectionSlot, 1, 3);
            state.ExportName = string.IsNullOrWhiteSpace(request.ExportName) ? "BrainStorm" : request.ExportName.Trim();
            if (request.Module == GraphicModule.Carton && request.Command == GraphicCommand.None)
            {
                state.CartonKind = CartonKindResolver.Resolve(request);
            }
            state.Target = request.Target;
            var isPureLanguageCmd = GraphicLanguageExtensions.TryFromCommand(request.Command, out _);
            if (!isPureLanguageCmd && (request.Command == GraphicCommand.None || !state.IsOnAir || state.ActiveQuery is null))
            {
                state.ActiveQuery = request.Query;
                state.PreviewQuery = request.Query;
            }
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

            case GraphicCommand.PrimerosResultadosActiva:
                state.PrimerosResultadosEnabled = true;
                break;

            case GraphicCommand.PrimerosResultadosDesactiva:
                state.PrimerosResultadosEnabled = false;
                break;

            case GraphicCommand.AnimacionSondeoActiva:
                state.AnimacionSondeoEnabled = true;
                break;

            case GraphicCommand.AnimacionSondeoDesactiva:
                state.AnimacionSondeoEnabled = false;
                break;

            case GraphicCommand.CambioEleccionesEuropa:
                state.AmbitoEuropaEnabled = true;
                break;

            case GraphicCommand.CambioEleccionesNacional:
                state.AmbitoEuropaEnabled = false;
                break;

            case GraphicCommand.IdiomaCastellano:
            case GraphicCommand.IdiomaEuskera:
            case GraphicCommand.IdiomaCatalan:
            case GraphicCommand.IdiomaGallego:
            case GraphicCommand.IdiomaValenciano:
                if (GraphicLanguageExtensions.TryFromCommand(request.Command, out var lang))
                {
                    state.GraphicLanguage = lang;
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

        state = new MutableModuleState(module, _defaultElectionSlot, _defaultOficiales);
        _states[module] = state;
        return state;
    }

    private sealed class MutableModuleState(GraphicModule module, int electionSlot, bool oficiales)
    {
        public GraphicModule Module { get; } = module;
        public CartonKind? CartonKind { get; set; } = module == GraphicModule.Carton ? Models.CartonKind.Participacion : null;
        public bool IsOnAir { get; set; }
        public string ActiveScene { get; set; } = string.Empty;
        public SnapshotQuery? ActiveQuery { get; set; }
        public SnapshotQuery? PreviewQuery { get; set; }
        public int ElectionSlot { get; set; } = electionSlot;
        public bool Oficiales { get; set; } = oficiales;
        public GraphicLanguage GraphicLanguage { get; set; } = GraphicLanguage.Castellano;
        public string ExportName { get; set; } = "BrainStorm";
        public GraphicsTarget Target { get; set; } = GraphicsTarget.Both;
        public string LastOperatorId { get; set; } = string.Empty;
        public bool IsSedeExpanding { get; set; }
        public bool PrimerosResultadosEnabled { get; set; } = true;
        public bool AnimacionSondeoEnabled { get; set; } = true;
        public bool AmbitoEuropaEnabled { get; set; }
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
            CartonKind = CartonKind,
            IsOnAir = IsOnAir,
            ActiveScene = ActiveScene,
            ActiveQuery = ActiveQuery,
            PreviewQuery = PreviewQuery,
            ElectionSlot = ElectionSlot,
            Oficiales = Oficiales,
            GraphicLanguage = GraphicLanguage,
            ExportName = ExportName,
            Target = Target,
            LastOperatorId = LastOperatorId,
            IsSedeExpanding = IsSedeExpanding,
            PrimerosResultadosEnabled = PrimerosResultadosEnabled,
            AnimacionSondeoEnabled = AnimacionSondeoEnabled,
            AmbitoEuropaEnabled = AmbitoEuropaEnabled,
            ExpandedVideos = ExpandedVideos.ToList(),
            PactosIzquierda = PactosIzquierda.ToList(),
            PactosDerecha = PactosDerecha.ToList(),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

