using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    public string Compose(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var modulePath = request.Module switch
        {
            GraphicModule.Faldon => "TICKER",
            GraphicModule.Carton => "CARTON",
            GraphicModule.Superfaldon => "SUPERFALDON",
            _ => "CONTROL"
        };

        var actionPath = request.Action switch
        {
            OperationActionType.Prepare => "PREPARA",
            OperationActionType.Enter => "ENTRA",
            OperationActionType.Update => "ACTUALIZA",
            OperationActionType.Exit => "SALE",
            OperationActionType.Reset => "RESET",
            _ => "EVENT"
        };

        var mode = request.Oficiales ? "OFICIAL" : "SONDEO";

        var lines = new List<string>
        {
            $"itemset('<{{BD}}>META/CIRC', '{snapshot.Circunscripcion.Codigo}');",
            $"itemset('<{{BD}}>META/MODE', '{mode}');",
            $"itemset('<{{BD}}>META/AVANCE', '{snapshot.Avance}');",
            $"itemset('<{{BD}}>{modulePath}/{actionPath}', 'EVENT_RUN');"
        };

        return string.Join("\n", lines);
    }
}
