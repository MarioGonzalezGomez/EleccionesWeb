using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    public string Compose(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var scene = ResolveScene(request);
        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var eventPath = ResolveEventPath(scene, request.Action);

        var lines = new List<string>
        {
            $"itemset('<{{BD}}>META/CIRC','{snapshot.Circunscripcion.Codigo}');",
            $"itemset('<{{BD}}>META/MODE','{(request.Oficiales ? "OFICIAL" : "SONDEO")}');",
            $"itemset('<{{BD}}>META/AVANCE','{snapshot.Avance}');"
        };

        if (NeedsCodeReload(scene, request.Action))
        {
            lines.Add($"itemset('<{{BD}}>{codeFile}','MAP_LLSTRING_LOAD');");
        }

        if (NeedsUltimoCsvReload(scene, request.Action))
        {
            lines.Add("itemset('<{BD}>UltimoEscanoCSV','MAP_LLSTRING_LOAD');");
        }

        if (request.Action == OperationActionType.Reset)
        {
            lines.Add("itemset('<{BD}>RESET','EVENT_RUN');");
            return string.Join("\n", lines);
        }

        if (!string.IsNullOrWhiteSpace(eventPath))
        {
            lines.Add($"itemset('<{{BD}}>{eventPath}','EVENT_RUN');");
        }

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
            GraphicModule.Faldon => request.Oficiales ? "Escrutinio" : "Sondeo",
            GraphicModule.Carton => "CARTON_PARTIDOS",
            GraphicModule.Superfaldon => "SUPERFALDON",
            _ => "CONTROL"
        };
    }

    private static bool NeedsCodeReload(string scene, OperationActionType action)
    {
        if (action is not (OperationActionType.Prepare or OperationActionType.Enter or OperationActionType.Update))
        {
            return false;
        }

        var normalized = scene.ToUpperInvariant();

        return normalized is "ESCRUTINIO"
            or "SONDEO"
            or "TICKER"
            or "CARRUSEL"
            or "CARTON_PARTIDOS"
            or "ULTIMO_ESCANO"
            or "ULTIMOESCANO";
    }

    private static bool NeedsUltimoCsvReload(string scene, OperationActionType action)
    {
        if (action is not (OperationActionType.Prepare or OperationActionType.Enter or OperationActionType.Update))
        {
            return false;
        }

        var normalized = scene.ToUpperInvariant();
        return normalized is "ULTIMO_ESCANO" or "ULTIMOESCANO" or "CARTON_PARTIDOS";
    }

    private static string ResolveEventPath(string scene, OperationActionType action)
    {
        var normalized = scene.ToUpperInvariant();

        return normalized switch
        {
            "ESCRUTINIO" => MapSimple("Escrutinio", action),
            "SONDEO" => MapSimple("Sondeo", action),
            "TICKER" => MapSimple("TICKER", action),

            "PARTICIPACION" => MapWithEncadena("PARTICIPACION", action),
            "CCAA_CARTONES" => action switch
            {
                OperationActionType.Prepare => "CCAA_CARTONES/PREPARA",
                OperationActionType.Enter => "CCAA_CARTONES/ENTRA",
                OperationActionType.Update => "CCAA/CAMBIA",
                OperationActionType.Exit => "CCAA/SALE",
                _ => string.Empty
            },
            "CARRUSEL" => MapWithEncadena("CARRUSEL", action),
            "MAYORIAS" => MapWithEncadena("MAYORIAS", action),
            "CARTON_PARTIDOS" => MapSimple("CARTON_PARTIDOS", action),
            "ULTIMO_ESCANO" => MapSimple("ULTIMO_ESCANO", action),

            "SUPERFALDON" => MapSimple("SUPERFALDON", action),
            "ULTIMOESCANO" => MapSimple("ULTIMOESCANO", action),
            "SEDES" => MapWithEncadena("SEDES", action),

            _ => MapSimple(scene, action)
        };
    }

    private static string MapSimple(string scene, OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => $"{scene}/PREPARA",
            OperationActionType.Enter => $"{scene}/ENTRA",
            OperationActionType.Update => $"{scene}/ACTUALIZA",
            OperationActionType.Exit => $"{scene}/SALE",
            _ => string.Empty
        };
    }

    private static string MapWithEncadena(string scene, OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => $"{scene}/PREPARA",
            OperationActionType.Enter => $"{scene}/ENTRA",
            OperationActionType.Update => $"{scene}/ENCADENA",
            OperationActionType.Exit => $"{scene}/SALE",
            _ => string.Empty
        };
    }
}
