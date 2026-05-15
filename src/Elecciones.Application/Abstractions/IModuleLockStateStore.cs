using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IModuleLockStateStore
{
    IReadOnlyList<ModuleLockInfo> Load();
    void Save(IReadOnlyList<ModuleLockInfo> states);
}
