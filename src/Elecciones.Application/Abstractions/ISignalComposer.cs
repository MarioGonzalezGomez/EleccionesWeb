using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface ISignalComposer
{
    string Compose(OperationRequest request, BrainStormSnapshot snapshot);
}
