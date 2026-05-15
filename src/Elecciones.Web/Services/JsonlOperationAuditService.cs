using System.Text.Json;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace EleccionesWeb.Services;

public sealed class JsonlOperationAuditService : IOperationAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonlOperationAuditService(IWebHostEnvironment environment)
    {
        var folder = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "operation-audit.jsonl");
    }

    public async Task RecordAsync(
        OperationRequest request,
        OperationResult result,
        CancellationToken cancellationToken = default)
    {
        var entry = new
        {
            TimestampUtc = DateTime.UtcNow,
            OperatorId = request.OperatorId,
            Module = request.Module.ToString(),
            Scene = request.Scene,
            Action = request.Action.ToString(),
            Command = request.Command.ToString(),
            CommandValue = request.CommandValue,
            Target = request.Target.ToString(),
            Oficiales = request.Oficiales,
            ExportName = request.ExportName,
            Query = request.Query,
            Result = new
            {
                result.Success,
                result.Message,
                result.CsvPath,
                result.Signal,
                result.DispatchedTargets
            }
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
