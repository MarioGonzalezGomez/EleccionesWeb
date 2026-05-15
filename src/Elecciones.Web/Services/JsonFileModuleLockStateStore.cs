using System.Text.Json;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace EleccionesWeb.Services;

public sealed class JsonFileModuleLockStateStore : IModuleLockStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly Mutex _mutex;

    public JsonFileModuleLockStateStore(IWebHostEnvironment environment)
    {
        var folder = Path.Combine(environment.ContentRootPath, "Data");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "module-locks.json");
        _mutex = new Mutex(false, @"Global\EleccionesWeb_ModuleLocks_v1");
    }

    public IReadOnlyList<ModuleLockInfo> Load()
    {
        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(TimeSpan.FromSeconds(3));
            if (!acquired || !File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<ModuleLockInfo>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
        finally
        {
            if (acquired)
            {
                _mutex.ReleaseMutex();
            }
        }
    }

    public void Save(IReadOnlyList<ModuleLockInfo> states)
    {
        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(TimeSpan.FromSeconds(3));
            if (!acquired)
            {
                return;
            }

            var json = JsonSerializer.Serialize(states, JsonOptions);
            var tempPath = $"{_filePath}.tmp";
            File.WriteAllText(tempPath, json);
            File.Copy(tempPath, _filePath, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
        }
        finally
        {
            if (acquired)
            {
                _mutex.ReleaseMutex();
            }
        }
    }
}
