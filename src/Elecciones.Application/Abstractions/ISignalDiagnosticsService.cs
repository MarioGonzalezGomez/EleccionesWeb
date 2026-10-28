using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface ISignalDiagnosticsService
{
    event Action? EntriesChanged;

    void Record(SignalDiagnosticEntry entry);

    IReadOnlyList<SignalDiagnosticEntry> GetRecentEntries(
        GraphicModule? module = null,
        string? endpointName = null,
        int maxCount = 200);

    void Clear();
}

public sealed record SignalDiagnosticEntry(
    DateTime TimestampUtc,
    GraphicModule? Module,
    string EndpointName,
    string Host,
    int Port,
    string Bd,
    bool Delivered,
    string StatusMessage,
    string Payload
);

