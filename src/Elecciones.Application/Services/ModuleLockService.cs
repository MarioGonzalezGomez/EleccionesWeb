using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class ModuleLockService : IModuleLockService
{
    private readonly object _sync = new();
    private readonly Dictionary<GraphicModule, InternalLockState> _states;
    private readonly IModuleLockStateStore _stateStore;

    public event Action? LocksChanged;

    public ModuleLockService(IModuleLockStateStore stateStore)
    {
        _stateStore = stateStore;
        _states = Enum
            .GetValues<GraphicModule>()
            .ToDictionary(m => m, m => new InternalLockState { Module = m });

        RefreshFromStore();
    }

    public IReadOnlyList<ModuleLockInfo> GetStates()
    {
        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            return _states.Values
                .OrderBy(s => s.Module)
                .Select(ToExternal)
                .ToList();
        }
    }

    public bool TryAcquire(GraphicModule module, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return false;
        }

        var acquired = false;

        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            var state = _states[module];
            if (state.IsLocked && !string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            state.IsLocked = true;
            state.Owner = operatorId.Trim();
            state.LockedAtUtc ??= DateTime.UtcNow;
            acquired = true;

            PersistUnsafe();
        }

        if (acquired)
        {
            LocksChanged?.Invoke();
        }

        return acquired;
    }

    public bool Release(GraphicModule module, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return false;
        }

        var released = false;

        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            var state = _states[module];
            if (!state.IsLocked)
            {
                return true;
            }

            if (!string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            state.IsLocked = false;
            state.IsOnAir = false;
            state.Owner = string.Empty;
            state.LockedAtUtc = null;
            released = true;

            PersistUnsafe();
        }

        if (released)
        {
            LocksChanged?.Invoke();
        }

        return released;
    }

    public bool ForceRelease(GraphicModule module)
    {
        var released = false;

        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            var state = _states[module];
            if (!state.IsLocked && !state.IsOnAir && string.IsNullOrWhiteSpace(state.Owner))
            {
                return true;
            }

            state.IsLocked = false;
            state.IsOnAir = false;
            state.Owner = string.Empty;
            state.LockedAtUtc = null;
            released = true;

            PersistUnsafe();
        }

        if (released)
        {
            LocksChanged?.Invoke();
        }

        return true;
    }

    public bool IsOwner(GraphicModule module, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return false;
        }

        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            var state = _states[module];
            return state.IsLocked && string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void MarkAction(GraphicModule module, string operatorId, OperationActionType action, string scene)
    {
        lock (_sync)
        {
            RefreshFromStoreUnsafe();
            var state = _states[module];
            if (!state.IsLocked)
            {
                return;
            }

            if (!string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            state.LastActionUtc = DateTime.UtcNow;
            state.LastActionType = action;
            state.LastScene = scene?.Trim() ?? string.Empty;

            if (action == OperationActionType.Enter)
            {
                state.IsOnAir = true;
            }
            else if (action is OperationActionType.Exit or OperationActionType.Reset)
            {
                state.IsOnAir = false;
            }

            PersistUnsafe();
        }

        LocksChanged?.Invoke();
    }

    private void RefreshFromStore()
    {
        lock (_sync)
        {
            RefreshFromStoreUnsafe();
        }
    }

    private void RefreshFromStoreUnsafe()
    {
        var loaded = _stateStore.Load();
        if (loaded.Count == 0)
        {
            return;
        }

        foreach (var persisted in loaded)
        {
            if (!_states.TryGetValue(persisted.Module, out var state))
            {
                continue;
            }

            state.IsLocked = persisted.IsLocked;
            state.IsOnAir = persisted.IsOnAir;
            state.Owner = persisted.Owner ?? string.Empty;
            state.LockedAtUtc = persisted.LockedAtUtc;
            state.LastActionUtc = persisted.LastActionUtc;
            state.LastActionType = persisted.LastActionType;
            state.LastScene = persisted.LastScene ?? string.Empty;
        }
    }

    private void PersistUnsafe()
    {
        var snapshot = _states.Values
            .OrderBy(s => s.Module)
            .Select(ToExternal)
            .ToList();
        _stateStore.Save(snapshot);
    }

    private static ModuleLockInfo ToExternal(InternalLockState state)
    {
        return new ModuleLockInfo
        {
            Module = state.Module,
            IsLocked = state.IsLocked,
            IsOnAir = state.IsOnAir,
            Owner = state.Owner,
            LockedAtUtc = state.LockedAtUtc,
            LastActionUtc = state.LastActionUtc,
            LastActionType = state.LastActionType,
            LastScene = state.LastScene
        };
    }

    private sealed class InternalLockState
    {
        public GraphicModule Module { get; init; }
        public bool IsLocked { get; set; }
        public bool IsOnAir { get; set; }
        public string Owner { get; set; } = string.Empty;
        public DateTime? LockedAtUtc { get; set; }
        public DateTime? LastActionUtc { get; set; }
        public OperationActionType? LastActionType { get; set; }
        public string LastScene { get; set; } = string.Empty;
    }
}
