using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    public string Compose(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var scene = ResolveScene(request);

        var lines = new List<string>
        {
            $"itemset('<{{BD}}>META/CIRC','{snapshot.Circunscripcion.Codigo}');",
            $"itemset('<{{BD}}>META/MODE','{(request.Oficiales ? "OFICIAL" : "SONDEO")}');",
            $"itemset('<{{BD}}>META/AVANCE','{snapshot.Avance}');"
        };

        if (request.Action == OperationActionType.Reset)
        {
            lines.Add(EventRun("RESET"));
            return string.Join("\n", lines);
        }

        if (request.Command != GraphicCommand.None)
        {
            lines.AddRange(ResolveCommandLines(request));
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var eventPath = ResolveEventPath(scene, request.Action);

        if (NeedsCodeReload(scene, request.Action))
        {
            lines.Add($"itemset('<{{BD}}>{codeFile}','MAP_LLSTRING_LOAD');");
        }

        if (NeedsUltimoCsvReload(scene, request.Action))
        {
            lines.Add("itemset('<{BD}>UltimoEscanoCSV','MAP_LLSTRING_LOAD');");
        }

        if (!string.IsNullOrWhiteSpace(eventPath))
        {
            lines.Add(EventRun(eventPath));
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

    private static IEnumerable<string> ResolveCommandLines(OperationRequest request)
    {
        return request.Command switch
        {
            GraphicCommand.TickerVotosEntra =>
            [
                MapString("Datos", "DiferenciaSale"),
                MapString("Datos", "PorcentajitoEntra")
            ],
            GraphicCommand.TickerVotosSale =>
            [
                MapString("Datos", "PorcentajitoSale"),
                MapString("Datos", "DiferenciaEntra")
            ],
            GraphicCommand.TickerHistoricosEntra =>
            [
                MapString("Datos", "PorcentajitoSale"),
                MapString("Datos", "DiferenciaEntra")
            ],
            GraphicCommand.TickerHistoricosSale =>
            [
                EventRun(request.Oficiales ? "TICKER/HISTORICOS/SALE" : "TICKER_SONDEO/HISTORICOS/SALE")
            ],
            GraphicCommand.TickerFotosEntra => [EventRun("EntraFoto")],
            GraphicCommand.TickerFotosSale => [EventRun("SaleFoto")],

            GraphicCommand.SedesEntra => BuildSedesLines("SEDES/ENTRA", request.CommandValue),
            GraphicCommand.SedesEncadena => BuildSedesLines("SEDES/ENCADENA", request.CommandValue),
            GraphicCommand.SedesSale => [EventRun("SEDES/SALE")],

            GraphicCommand.PactosEntra => [EventRun("Pactometro/Entra")],
            GraphicCommand.PactosSale =>
            [
                EventRun("Pactometro/Sale"),
                EventRun("Pactometro/reinicioPactometroIzq"),
                EventRun("Pactometro/reinicioPactometroDer")
            ],
            GraphicCommand.PactosReinicio =>
            [
                EventRun("Pactometro/reinicioPactometroIzq"),
                EventRun("Pactometro/reinicioPactometroDer")
            ],
            GraphicCommand.PactosEntraIzquierda =>
            [
                TryMapString("Pactometro/PartidoIzq", request.CommandValue),
                EventRun("Pactometro/lanzaPactometroIzq")
            ],
            GraphicCommand.PactosEntraDerecha =>
            [
                TryMapString("Pactometro/PartidoDer", request.CommandValue),
                EventRun("Pactometro/lanzaPactometroDer")
            ],
            GraphicCommand.PactosSaleIzquierda =>
            [
                "itemset('<{BD}>Graficos/Pactometro/Izq/LogosIzq','OBJ_GRID_JUMP_PREV');"
            ],
            GraphicCommand.PactosSaleDerecha =>
            [
                "itemset('<{BD}>Graficos/Pactometro/Der/LogosDer','OBJ_GRID_JUMP_PREV');"
            ],

            GraphicCommand.UltimoLimpiaPartidos =>
            [
                EventRun("ULTIMO_ESCANO/SALE_BARRAS")
            ],
            GraphicCommand.UltimoEntraPartidoIzquierda =>
            [
                TryMapString("ULTIMO_ESCANO/PARTIDO_IZQ", request.CommandValue),
                EventRun("ULTIMO_ESCANO/ENTRA_PARTIDO_IZQ")
            ],
            GraphicCommand.UltimoEntraPartidoDerecha =>
            [
                TryMapString("ULTIMO_ESCANO/PARTIDO_DER", request.CommandValue),
                EventRun("ULTIMO_ESCANO/ENTRA_PARTIDO_DER")
            ],

            _ => []
        };
    }

    private static IEnumerable<string> BuildSedesLines(string eventPath, string commandValue)
    {
        if (string.IsNullOrWhiteSpace(commandValue))
        {
            return [EventRun(eventPath)];
        }

        return
        [
            MapString("SEDES/PARTIDO", commandValue),
            EventRun(eventPath)
        ];
    }

    private static string EventRun(string path)
    {
        return $"itemset('<{{BD}}>{path}','EVENT_RUN');";
    }

    private static string MapString(string path, string value)
    {
        return $"itemset('<{{BD}}>{path}','MAP_STRING_PAR','{EscapeValue(value)}');";
    }

    private static string TryMapString(string path, string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : MapString(path, value);
    }

    private static string EscapeValue(string value)
    {
        return value
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }
}
