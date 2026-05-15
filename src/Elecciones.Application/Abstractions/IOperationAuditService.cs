using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IOperationAuditService
{
    Task RecordAsync(
        OperationRequest request,
        OperationResult result,
        CancellationToken cancellationToken = default);
}
