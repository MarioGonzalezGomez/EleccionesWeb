using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class ModuleLockService : IModuleLockService
{
    private readonly object _sync = new();
    private readonly Dictionary<GraphicModule, InternalLockState> _states;

    public event Action? LocksChanged;

    public ModuleLockService()
    {
        _states = Enum
            .GetValues<GraphicModule>()
            .ToDictionary(m => m, m => new InternalLockState { Module = m });
    }

    public IReadOnlyList<ModuleLockInfo> GetStates()
    {
        lock (_sync)
        {
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
            var state = _states[module];
            if (state.IsLocked && !string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            state.IsLocked = true;
            state.Owner = operatorId.Trim();
            state.LockedAtUtc ??= DateTime.UtcNow;
            acquired = true;
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
            state.Owner = string.Empty;
            state.LockedAtUtc = null;
            released = true;
        }

        if (released)
        {
            LocksChanged?.Invoke();
        }

        return released;
    }

    public bool IsOwner(GraphicModule module, string operatorId)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return false;
        }

        lock (_sync)
        {
            var state = _states[module];
            return state.IsLocked && string.Equals(state.Owner, operatorId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void MarkAction(GraphicModule module, string operatorId)
    {
        lock (_sync)
        {
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
        }

        LocksChanged?.Invoke();
    }

    private static ModuleLockInfo ToExternal(InternalLockState state)
    {
        return new ModuleLockInfo
        {
            Module = state.Module,
            IsLocked = state.IsLocked,
            Owner = state.Owner,
            LockedAtUtc = state.LockedAtUtc,
            LastActionUtc = state.LastActionUtc
        };
    }

    private sealed class InternalLockState
    {
        public GraphicModule Module { get; init; }
        public bool IsLocked { get; set; }
        public string Owner { get; set; } = string.Empty;
        public DateTime? LockedAtUtc { get; set; }
        public DateTime? LastActionUtc { get; set; }
    }
}
