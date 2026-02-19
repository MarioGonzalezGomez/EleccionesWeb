using Elecciones.Application.Models;

namespace Elecciones.Application.Abstractions;

public interface IGraphicsGateway
{
    Task<IReadOnlyList<string>> SendAsync(
        GraphicsTarget target,
        string message,
        CancellationToken cancellationToken = default);
}
