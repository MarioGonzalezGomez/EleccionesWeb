using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    public string Compose(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var scene = ResolveScene(request);
        var actionPath = ResolveActionPath(request.Action);
        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";

        var lines = new List<string>
        {
            $"itemset('<{{BD}}>META/CIRC','{snapshot.Circunscripcion.Codigo}');",
            $"itemset('<{{BD}}>META/MODE','{(request.Oficiales ? "OFICIAL" : "SONDEO")}');",
            $"itemset('<{{BD}}>META/AVANCE','{snapshot.Avance}');"
        };

        if (request.Action is OperationActionType.Prepare or OperationActionType.Enter or OperationActionType.Update)
        {
            lines.Add($"itemset('<{{BD}}>{codeFile}','MAP_LLSTRING_LOAD');");

            if (scene.Contains("ULTIMO", StringComparison.OrdinalIgnoreCase)
                || scene.Contains("CARTON", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("itemset('<{BD}>UltimoEscanoCSV','MAP_LLSTRING_LOAD');");
            }
        }

        if (request.Action == OperationActionType.Reset)
        {
            lines.Add("itemset('<{BD}>RESET','EVENT_RUN');");
            return string.Join("\n", lines);
        }

        lines.Add($"itemset('<{{BD}}>{scene}/{actionPath}','EVENT_RUN');");
        return string.Join("\n", lines);
    }

    private static string ResolveScene(OperationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Scene))
        {
            return request.Scene.Trim();
        }

        return request.Module switch
        {
            GraphicModule.Faldon => "Escrutinio",
            GraphicModule.Carton => "CARTON_PARTIDOS",
            GraphicModule.Superfaldon => "SUPERFALDON",
            _ => "CONTROL"
        };
    }

    private static string ResolveActionPath(OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => "PREPARA",
            OperationActionType.Enter => "ENTRA",
            OperationActionType.Update => "ACTUALIZA",
            OperationActionType.Exit => "SALE",
            OperationActionType.Reset => "RESET",
            _ => "EVENT"
        };
    }
}
