using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class InMemorySignalDiagnosticsService : ISignalDiagnosticsService
{
    private const int Capacity = 250;
    private readonly object _sync = new();
    private readonly LinkedList<SignalDiagnosticEntry> _entries = new();

    public event Action? EntriesChanged;

    public void Record(SignalDiagnosticEntry entry)
    {
        lock (_sync)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }

        EntriesChanged?.Invoke();
    }

    public IReadOnlyList<SignalDiagnosticEntry> GetRecentEntries(
        GraphicModule? module = null,
        string? endpointName = null,
        int maxCount = 200)
    {
        lock (_sync)
        {
            IEnumerable<SignalDiagnosticEntry> query = _entries;

            if (module.HasValue)
            {
                query = query.Where(e => e.Module == module.Value);
            }

            if (!string.IsNullOrWhiteSpace(endpointName))
            {
                var target = endpointName.Trim();
                query = query.Where(e => string.Equals(e.EndpointName, target, StringComparison.OrdinalIgnoreCase));
            }

            return query.Take(Math.Max(1, maxCount)).ToList();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }

        EntriesChanged?.Invoke();
    }
}

