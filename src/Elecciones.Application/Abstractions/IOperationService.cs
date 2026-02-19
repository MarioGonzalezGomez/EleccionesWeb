using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperationService
{
    Task<OperationResult> ExecuteAsync(OperationRequest request, CancellationToken cancellationToken = default);
}
