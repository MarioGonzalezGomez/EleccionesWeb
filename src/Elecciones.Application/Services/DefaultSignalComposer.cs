using System.Globalization;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    private const double PactosTotalWidth = 1748d;
    private const string PrimeTdProject = "01_TD_2026";
    private const string PrimeEspecialesProject = "Especiales_ConPicto_2026";

    private const int UltimoMaxFichaWidth = 1756;
    private const int UltimoPosInicialIzq = 90;
    private const int UltimoPosInicialDch = 1844;
    private const int UltimoAnchoPequenoThreshold = 126;

    private static readonly string[] UltimoBarrasIzq = ["Barra_Izq", "Barra_Izq1", "Barra_Izq2", "Barra_Izq3", "Barra_Izq4", "Barra_Izq5"];
    private static readonly string[] UltimoBarrasDch = ["Barra_Dch", "Barra_Dch1", "Barra_Dch2", "Barra_Dch3", "Barra_Dch4", "Barra_Dch5"];

    private static readonly Dictionary<int, (int Size, int[] Positions, int LogoPos, int EscanosPos)> TickerTdLayouts = new()
    {
        { 1,  (1344, [0], -124, -10) },
        { 2,  (662,  [0, 682], -450, -327) },
        { 3,  (433,  [0, 453, 905], -565, -395) },
        { 4,  (320,  [0, 341, 683, 1024], -621, -441) },
        { 5,  (252,  [0, 273, 546, 819, 1092], -655, -470) },
        { 6,  (207,  [0, 228, 455, 683, 910, 1138], -678, -502) },
        { 7,  (175,  [0, 195, 390, 585, 780, 975, 1170], -695, -523) },
        { 8,  (150,  [0, 171, 341, 512, 682, 853, 1024, 1194], -705, -539) },
        { 9,  (132,  [0, 152, 304, 456, 608, 760, 912, 1064, 1215], -715, -552) },
        { 10, (117,  [0, 137, 274, 411, 548, 684, 821, 958, 1095, 1232], -721, -562) },
        { 11, (104,  [0, 124, 249, 373, 498, 622, 747, 871, 995, 1120, 1244], -730, -569) },
        { 12, (94,   [0, 114, 228, 343, 457, 571, 685, 799, 914, 1028, 1142, 1256], -733, -579) }
    };

    private readonly object _stateLock = new();
    private readonly Dictionary<GraphicModule, PactosState> _pactosStates = new();
    private readonly Dictionary<GraphicModule, SedesState> _sedesStates = new();
    private readonly Dictionary<GraphicModule, UltimoEscanoState> _ultimoEscanoStates = new();
    private readonly Dictionary<GraphicModule, FaldonState> _faldonStates = new();
    private readonly Dictionary<GraphicModule, CartonState> _cartonStates = new();
    private bool _animacionPrimerosEnabled = true;
    private bool _animacionSondeoEnabled = true;

    public string Compose(OperationRequest request, BrainStormSnapshot snapshot)
    {
        if (request.Command != GraphicCommand.None && IsRawPrimeCommand(request.Command))
        {
            return BuildPrimeCommandSignal(request.Command);
        }

        var scene = ResolveScene(request);

        var lines = new List<string>
        {
            $"itemset('<{{BD}}>META/CIRC','{snapshot.Circunscripcion.Codigo}');",
            $"itemset('<{{BD}}>META/MODE','{(request.Oficiales ? "OFICIAL" : "SONDEO")}');",
            $"itemset('<{{BD}}>META/AVANCE','{snapshot.Avance}');"
        };

        if (request.Action == OperationActionType.Reset)
        {
            ClearModuleState(request.Module);
            lines.Add(EventRun("RESET"));
            return string.Join("\n", lines);
        }

        if (request.Command != GraphicCommand.None)
        {
            lines.AddRange(ResolveCommandLines(request, snapshot));
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        if (request.Module == GraphicModule.Faldon && IsFaldonTickerScene(scene))
        {
            lines.AddRange(ResolveFaldonTickerScene(request, snapshot));
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        SyncUltimoEscanoLifecycle(request, snapshot, scene);

        if (string.Equals(scene, "CuentaAtras", StringComparison.OrdinalIgnoreCase))
        {
            lines.AddRange(ResolveCuentaAtrasLines(request.Action, request.SceneSeconds));
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        if (TryComposeUltimoEscanoOnAirUpdate(request, snapshot, lines))
        {
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        if (request.Module == GraphicModule.Carton && TryComposeCartonScene(request, snapshot, scene, lines))
        {
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        if (request.Module == GraphicModule.Superfaldon
            && string.Equals(scene, "SUPERFALDON", StringComparison.OrdinalIgnoreCase)
            && request.Action == OperationActionType.Exit)
        {
            lock (_stateLock)
            {
                var sedesState = GetOrCreateSedesState(request.Module);
                if (sedesState.IsSuperfaldonSedeOnAir)
                {
                    lines.Add(EventRun("Superfaldon/Sedes/RepliegaSede"));
                    sedesState.IsSuperfaldonSedeOnAir = false;
                }
            }
        }

        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var eventPaths = ResolveEventPaths(request, scene).ToList();

        if (NeedsCodeReload(scene, request.Action))
        {
            lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
        }

        if (NeedsUltimoCsvReload(scene, request.Action))
        {
            lines.Add(ItemSetNoValue("UltimoEscanoCSV", "MAP_LLSTRING_LOAD"));
        }

        if (eventPaths.Count > 0)
        {
            if (ShouldEmitPrimerosResultados(request, scene))
            {
                lines.Add(EventRun("PrimerosResultados/Entra"));
            }

            lines.AddRange(eventPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(EventRun));
        }

        return string.Join("\n", lines);
    }

    private static bool IsFaldonTickerScene(string scene)
    {
        return scene.Equals("TICKER", StringComparison.OrdinalIgnoreCase)
            || scene.Equals("Escrutinio", StringComparison.OrdinalIgnoreCase)
            || scene.Equals("Sondeo", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> ResolveFaldonTickerScene(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var lines = new List<string>();
        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var tipo = request.Oficiales ? "Escrutinio" : "Sondeo";

        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            var partidoIdMap = BuildPartidoIdMap(snapshot);
            var partidosActivos = GetOrderedActiveParties(snapshot, request.Oficiales);

            switch (request.Action)
            {
                case OperationActionType.Prepare:
                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    lines.Add(EventRun($"{tipo}/Prepara"));
                    break;

                case OperationActionType.Enter:
                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    if (_animacionPrimerosEnabled && (request.Oficiales || _animacionSondeoEnabled))
                    {
                        lines.Add(EventRun("PrimerosResultados/Entra"));
                    }

                    var countEntra = Math.Max(partidosActivos.Count, 1);
                    var tamanoFichaEntra = (1748.0 - (10 * (countEntra - 1))) / countEntra;
                    lines.Add(ItemSetNumeric("fichaPartido", "PRIM_RECGLO_LEN[0]", tamanoFichaEntra));

                    double posAcumuladaEntra = 0;
                    for (int i = 0; i < partidosActivos.Count; i++)
                    {
                        var partido = partidosActivos[i];
                        var partidoId = GetPartyIdFromMap(partidoIdMap, partido.Codigo, i + 1);
                        lines.Add(ItemSetNumeric($"Graficos/{tipo}/partidos/partido{partidoId}", "OBJ_DISPLACEMENT[0]", posAcumuladaEntra));
                        if (i < partidosActivos.Count - 1)
                        {
                            posAcumuladaEntra += tamanoFichaEntra + 10;
                        }
                    }

                    lines.AddRange(BuildRecolocarPartidosNoActivosLines(
                        partidoIdMap,
                        partidosActivos,
                        tipo,
                        posAcumuladaEntra + tamanoFichaEntra,
                        tamanoFichaEntra,
                        useItemGo: false,
                        animTime: 0,
                        delay: 0));

                    if (partidosActivos.Count > 6)
                    {
                        lines.Add(EventRun("SaleFoto"));
                    }

                    lines.Add(EventRun($"{tipo}/Entra"));
                    break;

                case OperationActionType.Update:
                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    if (state.PartidosExpandidos.Count > 0)
                    {
                        lines.Add(EventRun($"{tipo}/Actualiza"));
                        break;
                    }

                    var countUpdate = Math.Max(partidosActivos.Count, 1);
                    var tamanoFichaUpdate = (1748.0 - (10 * (countUpdate - 1))) / countUpdate;
                    lines.Add(ItemGoNumeric("fichaPartido", "PRIM_RECGLO_LEN[0]", tamanoFichaUpdate, 0.3, 0));

                    double posAcumuladaUpdate = 0;
                    for (int i = 0; i < partidosActivos.Count; i++)
                    {
                        var partido = partidosActivos[i];
                        var partidoId = GetPartyIdFromMap(partidoIdMap, partido.Codigo, i + 1);
                        lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{partidoId}", "OBJ_DISPLACEMENT[0]", posAcumuladaUpdate, 0.3, 0));
                        if (i < partidosActivos.Count - 1)
                        {
                            posAcumuladaUpdate += tamanoFichaUpdate + 10;
                        }
                    }

                    lines.AddRange(BuildRecolocarPartidosNoActivosLines(
                        partidoIdMap,
                        partidosActivos,
                        tipo,
                        posAcumuladaUpdate + tamanoFichaUpdate,
                        tamanoFichaUpdate,
                        useItemGo: true,
                        animTime: 0.3,
                        delay: 0));

                    if (partidosActivos.Count > 6)
                    {
                        lines.Add(EventRun("SaleFoto"));
                    }

                    lines.Add(EventRun($"{tipo}/Actualiza"));
                    break;

                case OperationActionType.Exit:
                    if (state.PartidosExpandidos.Count > 0)
                    {
                        lines.AddRange(ResolveVideoOutTodosInternal(request, snapshot, state));
                    }
                    lines.Add(EventRun($"{tipo}/Sale"));
                    break;
            }
        }

        return lines;
    }

    private static List<string> BuildRecolocarPartidosNoActivosLines(
        Dictionary<string, string> partidoIdMap,
        List<PartidoSnapshot> partidosActivos,
        string tipo,
        double posicionBaseNoActivos,
        double tamanoFicha,
        bool useItemGo,
        double animTime,
        double delay)
    {
        var lines = new List<string>();
        var codigosActivos = new HashSet<string>(
            partidosActivos.Where(p => !string.IsNullOrWhiteSpace(p.Codigo)).Select(p => p.Codigo),
            StringComparer.OrdinalIgnoreCase);

        var partidosNoActivos = partidoIdMap
            .Where(kvp => !codigosActivos.Contains(kvp.Key))
            .OrderBy(kvp => int.TryParse(kvp.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MaxValue)
            .ToList();

        int offset = 0;
        foreach (var kvp in partidosNoActivos)
        {
            var posicion = posicionBaseNoActivos + (offset * (tamanoFicha + 10));
            var path = $"Graficos/{tipo}/partidos/partido{kvp.Value}";
            lines.Add(useItemGo
                ? ItemGoNumeric(path, "OBJ_DISPLACEMENT[0]", posicion, animTime, delay)
                : ItemSetNumeric(path, "OBJ_DISPLACEMENT[0]", posicion));
            offset++;
        }

        return lines;
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

    private static IEnumerable<string> ResolveEventPaths(OperationRequest request, string scene)
    {
        var normalized = scene.ToUpperInvariant();
        var action = request.Action;

        if (request.Module == GraphicModule.Superfaldon)
        {
            if (normalized is "SUPERFALDON")
            {
                return YieldIfNotEmpty(ResolveSuperfaldonCarruselPath(request.Oficiales, action));
            }

            if (normalized is "ULTIMOESCANO" or "ULTIMO_ESCANO")
            {
                return YieldIfNotEmpty(ResolveSuperfaldonUltimoPath(action));
            }
        }

        var path = normalized switch
        {
            "ESCRUTINIO" => MapSimple("Escrutinio", action),
            "SONDEO" => MapSimple("Sondeo", action),
            "TICKER" => MapSimple("TICKER", action),

            "PARTICIPACION" => ResolveParticipacionPath(action),
            "CCAA_CARTONES" => action switch
            {
                OperationActionType.Prepare => "CCAA_CARTONES/PREPARA",
                OperationActionType.Enter => "CCAA_CARTONES/ENTRA",
                OperationActionType.Update => "CCAA/CAMBIA",
                OperationActionType.Exit => "CCAA/SALE",
                _ => string.Empty
            },
            "CARRUSEL" => ResolveCarruselPath(action),
            "MAYORIAS" => ResolveMayoriasPath(action),
            "CARTON_PARTIDOS" => MapSimpleNoUpdate("CARTON_PARTIDOS", action),
            "ULTIMO_ESCANO" => MapSimpleNoUpdate("ULTIMO_ESCANO", action),

            "SUPERFALDON" => MapSimple("SUPERFALDON", action),
            "ULTIMOESCANO" => MapSimpleNoUpdate("ULTIMOESCANO", action),
            "SEDES" => MapWithEncadena("SEDES", action),

            _ => MapSimple(scene, action)
        };

        if (normalized == "PARTICIPACION" && action == OperationActionType.Enter)
        {
            return YieldIfNotEmpty("PARTICIPACION/PREPARA", path);
        }

        if (normalized == "CCAA_CARTONES" && action == OperationActionType.Enter)
        {
            return YieldIfNotEmpty("CCAA_CARTONES/PREPARA", path);
        }

        if (normalized == "MAYORIAS" && action == OperationActionType.Enter)
        {
            return YieldIfNotEmpty("MAYORIAS/PREPARA", path);
        }

        if (normalized == "CARRUSEL" && action == OperationActionType.Enter)
        {
            var modePath = request.Oficiales ? "CARRUSEL/OFICIAL" : "CARRUSEL/SONDEO";
            return YieldIfNotEmpty(modePath, path);
        }

        return YieldIfNotEmpty(path);
    }

    private static string ResolveSuperfaldonCarruselPath(bool oficiales, OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Enter => oficiales ? "Superfaldon/Oficial/Entra" : "Superfaldon/Sondeo/Entra",
            OperationActionType.Update => "ReloadPartidos",
            OperationActionType.Exit => oficiales ? "Superfaldon/Oficial/Sale" : "Superfaldon/Sondeo/Sale",
            _ => string.Empty
        };
    }

    private static string ResolveSuperfaldonUltimoPath(OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Enter => "UltimoEscanoN/Entra",
            OperationActionType.Exit => "UltimoEscanoN/Sale",
            _ => string.Empty
        };
    }

    private static IEnumerable<string> ResolveCuentaAtrasLines(OperationActionType action, int? seconds)
    {
        var lines = new List<string>();
        var safeSeconds = seconds.GetValueOrDefault();
        if (safeSeconds < 0)
        {
            safeSeconds = 0;
        }

        switch (action)
        {
            case OperationActionType.Prepare:
                if (safeSeconds > 0)
                {
                    lines.Add(ItemSetNumeric("CuentaAtras", "TIMER_LENGTH", safeSeconds));
                }
                lines.Add(EventRun("CuentaAtras/PonerEnInicio"));
                break;
            case OperationActionType.Enter:
                if (safeSeconds > 0)
                {
                    lines.Add(ItemSetNumeric("CuentaAtras", "TIMER_LENGTH", safeSeconds));
                    lines.Add(EventRun("CuentaAtras/PonerEnInicio"));
                    lines.Add(EventRun("CuentaAtras/Play"));
                }
                lines.Add(EventRun("CuentaAtras/Entra"));
                break;
            case OperationActionType.Update:
                if (safeSeconds > 0)
                {
                    lines.Add(ItemSetNumeric("CuentaAtras", "TIMER_LENGTH", safeSeconds));
                }
                lines.Add(EventRun("CuentaAtras/PonerEnInicio"));
                lines.Add(EventRun("CuentaAtras/Play"));
                break;
            case OperationActionType.Exit:
                lines.Add(EventRun("CuentaAtras/Sale"));
                break;
        }

        return lines;
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

    private static string MapSimpleNoUpdate(string scene, OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => $"{scene}/PREPARA",
            OperationActionType.Enter => $"{scene}/ENTRA",
            OperationActionType.Update => string.Empty,
            OperationActionType.Exit => $"{scene}/SALE",
            _ => string.Empty
        };
    }

    private static string ResolveCarruselPath(OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => "CARRUSEL/PREPARA",
            OperationActionType.Enter => "CARRUSEL/ENTRA",
            OperationActionType.Update => string.Empty,
            OperationActionType.Exit => "CARRUSEL/SALE",
            _ => string.Empty
        };
    }

    private static string ResolveParticipacionPath(OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => "PARTICIPACION/PREPARA",
            OperationActionType.Enter => "PARTICIPACION/ENTRA",
            OperationActionType.Update => "PARTICIPACION/ENCADENA",
            OperationActionType.Exit => "PARTICIPACION/SALE",
            _ => string.Empty
        };
    }

    private static string ResolveMayoriasPath(OperationActionType action)
    {
        return action switch
        {
            OperationActionType.Prepare => "MAYORIAS/PREPARA",
            OperationActionType.Enter => "MAYORIAS/ENTRA",
            OperationActionType.Update => string.Empty,
            OperationActionType.Exit => "MAYORIAS/SALE",
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

    private static IEnumerable<string> YieldIfNotEmpty(params string[] paths)
    {
        return paths.Where(path => !string.IsNullOrWhiteSpace(path));
    }

    private IEnumerable<string> ResolveCommandLines(OperationRequest request, BrainStormSnapshot snapshot)
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
            GraphicCommand.TickerTDEntra => ResolveTickerTdEntra(request, snapshot),
            GraphicCommand.TickerTDActualiza => ResolveTickerTdActualiza(request, snapshot),
            GraphicCommand.TickerTDSale => [EventRun("FALDON_TD/SALE")],

            GraphicCommand.VideoPrepara => ResolveVideoPrepara(request, snapshot),
            GraphicCommand.VideoIn => ResolveVideoIn(request, snapshot),
            GraphicCommand.VideoOut => ResolveVideoOut(request, snapshot),
            GraphicCommand.VideoInTodos => ResolveVideoInTodos(request, snapshot),
            GraphicCommand.VideoOutTodos => ResolveVideoOutTodos(request, snapshot),

            GraphicCommand.CcaaSube => [EventRun("CCAA_CARTONES/SUBE")],
            GraphicCommand.CcaaBaja => [EventRun("CCAA_CARTONES/BAJA")],
            GraphicCommand.MayoriasSube => [EventRun("MAYORIAS/SUBE")],
            GraphicCommand.MayoriasBaja => [EventRun("MAYORIAS/BAJA")],
            GraphicCommand.CarruselEncadena => ResolveCarruselEncadena(request, snapshot),

            GraphicCommand.SedesEntra => request.Module == GraphicModule.Superfaldon
                ? ResolveSuperfaldonSedesEntra(request, snapshot)
                : ResolveFaldonSedesEntra(request, snapshot),
            GraphicCommand.SedesEncadena => request.Module == GraphicModule.Superfaldon
                ? ResolveSuperfaldonSedesEncadena(request, snapshot)
                : ResolveFaldonSedesEncadena(request, snapshot),
            GraphicCommand.SedesSale => request.Module == GraphicModule.Superfaldon
                ? ResolveSuperfaldonSedesSale(request)
                : ResolveFaldonSedesSale(request),

            GraphicCommand.PactosEntra => [EventRun("Pactometro/Entra")],
            GraphicCommand.PactosSale => ResolvePactosSale(request),
            GraphicCommand.PactosReinicio => ResolvePactosReinicio(request),
            GraphicCommand.PactosEntraIzquierda => ResolvePactosEntra(request, snapshot, true),
            GraphicCommand.PactosEntraDerecha => ResolvePactosEntra(request, snapshot, false),
            GraphicCommand.PactosSaleIzquierda => ResolvePactosSaleLateral(request, snapshot, true),
            GraphicCommand.PactosSaleDerecha => ResolvePactosSaleLateral(request, snapshot, false),

            GraphicCommand.UltimoLimpiaPartidos => ResolveUltimoLimpiaPartidos(request),
            GraphicCommand.UltimoEntraPartidoIzquierda => ResolveUltimoEntraPartido(request, snapshot, true),
            GraphicCommand.UltimoEntraPartidoDerecha => ResolveUltimoEntraPartido(request, snapshot, false),

            GraphicCommand.PrimerosResultadosActiva => ResolvePrimerosResultadosCommand(true),
            GraphicCommand.PrimerosResultadosDesactiva => ResolvePrimerosResultadosCommand(false),
            GraphicCommand.AnimacionSondeoActiva => ResolveAnimacionSondeoCommand(true),
            GraphicCommand.AnimacionSondeoDesactiva => ResolveAnimacionSondeoCommand(false),
            GraphicCommand.DeSondeoAOficiales => [],
            GraphicCommand.CambioEleccionesEuropa => [ItemSetNumeric("TICKER/EUROPA", "MAP_INT_PAR", 1)],
            GraphicCommand.CambioEleccionesNacional => [ItemSetNumeric("TICKER/EUROPA", "MAP_INT_PAR", 0)],

            _ => []
        };
    }

    private static bool IsRawPrimeCommand(GraphicCommand command)
    {
        return command is GraphicCommand.PrimeSubirRotulosTD
            or GraphicCommand.PrimeBajarRotulosTD
            or GraphicCommand.PrimeSubirRotulosEspeciales
            or GraphicCommand.PrimeBajarRotulosEspeciales;
    }

    private static string BuildPrimeCommandSignal(GraphicCommand command)
    {
        return command switch
        {
            GraphicCommand.PrimeSubirRotulosTD => string.Concat(
                PrimeChangeProject(PrimeTdProject),
                PrimeProjectParameter("TickerElec", "True")),
            GraphicCommand.PrimeBajarRotulosTD => string.Concat(
                PrimeChangeProject(PrimeTdProject),
                PrimeProjectParameter("TickerElec", "False")),
            GraphicCommand.PrimeSubirRotulosEspeciales => string.Concat(
                PrimeChangeProject(PrimeEspecialesProject),
                PrimeProjectParameter("Par_Posicion_GrupoXY", "2")),
            GraphicCommand.PrimeBajarRotulosEspeciales => string.Concat(
                PrimeChangeProject(PrimeEspecialesProject),
                PrimeProjectParameter("Par_Posicion_GrupoXY", "1")),
            _ => string.Empty
        };
    }

    private static string PrimeChangeProject(string project)
    {
        return $"P\\CHANGE_PROJECT\\{project}\\\\\r\n";
    }

    private static string PrimeProjectParameter(string parameterName, string parameterValue)
    {
        return $"P\\PROJECT_PARAMETER\\{parameterName}\\{parameterValue}\\\\\r\n";
    }

    private IEnumerable<string> ResolvePrimerosResultadosCommand(bool enabled)
    {
        lock (_stateLock)
        {
            _animacionPrimerosEnabled = enabled;
        }

        return [];
    }

    private IEnumerable<string> ResolveAnimacionSondeoCommand(bool enabled)
    {
        lock (_stateLock)
        {
            _animacionSondeoEnabled = enabled;
        }

        return [];
    }

    private IEnumerable<string> ResolveVideoPrepara(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var slotId = TryResolvePartySlotId(snapshot, request.CommandValue);
        if (string.IsNullOrEmpty(slotId))
        {
            return [];
        }

        return [EventRun($"VIDEOS/{slotId}/PREPARA")];
    }

    private IEnumerable<string> ResolveVideoIn(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var partidoSeleccionado = TryResolveParty(snapshot, request.CommandValue);
        if (partidoSeleccionado is null)
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            if (!state.PartidosExpandidos.Any(x => string.Equals(x, partidoSeleccionado.Codigo, StringComparison.OrdinalIgnoreCase)))
            {
                state.PartidosExpandidos.Add(partidoSeleccionado.Codigo);
            }

            return BuildVideoInLines(request.Oficiales, snapshot, state, partidoSeleccionado.Codigo);
        }
    }

    private static List<string> BuildVideoInLines(
        bool oficiales,
        BrainStormSnapshot snapshot,
        FaldonState state,
        string? triggeredPartyCode)
    {
        var lines = new List<string>();
        var partidoIdMap = BuildPartidoIdMap(snapshot);
        var partidosActivos = GetOrderedActiveParties(snapshot, oficiales);
        var count = partidosActivos.Count;
        if (count == 0)
        {
            return lines;
        }

        var usarExpansionDinamica = count > 4;
        var totalWidthAvailable = 1748.0 - (10 * (count - 1));
        var minWidth = 1748.0 / 5.0; // 349.6

        var numExpandidos = partidosActivos.Count(p =>
            state.PartidosExpandidos.Any(c => string.Equals(c, p.Codigo, StringComparison.OrdinalIgnoreCase)));

        double widthOthers;
        if (!usarExpansionDinamica || numExpandidos == 0)
        {
            widthOthers = totalWidthAvailable / count;
        }
        else
        {
            widthOthers = (count - numExpandidos) > 0
                ? (totalWidthAvailable - (numExpandidos * minWidth)) / (count - numExpandidos)
                : 0;
        }

        var tipo = oficiales ? "Escrutinio" : "Sondeo";
        double posicionAcumulada = 0;

        for (int i = 0; i < count; i++)
        {
            var partido = partidosActivos[i];
            var sceneObjectId = GetPartyIdFromMap(partidoIdMap, partido.Codigo, i + 1);
            var isExpanded = state.PartidosExpandidos.Any(c => string.Equals(c, partido.Codigo, StringComparison.OrdinalIgnoreCase));

            double currentWidth;
            double scaleX;
            double scaleZ;

            if (isExpanded)
            {
                currentWidth = usarExpansionDinamica ? minWidth : widthOthers;
                scaleX = (usarExpansionDinamica && widthOthers > 0) ? (minWidth / widthOthers) : 1.0;
                scaleZ = 1.4;
            }
            else
            {
                currentWidth = widthOthers;
                scaleX = 1.0;
                scaleZ = 1.0;
            }

            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}", "OBJ_DISPLACEMENT[0]", posicionAcumulada, 0.5, 0));
            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[0]", scaleX, 0.5, 0));
            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[2]", scaleZ, 0.5, 0));

            if (isExpanded)
            {
                var posVid = count >= 5 ? -656 : count == 4 ? -566 : count == 3 ? -426 : -656;
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/Vid", "OBJ_DISPLACEMENT[0]", posVid, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DirectoMascara/Directo", "OBJ_DISPLACEMENT[2]", 40, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Escanios", "OBJ_HOTPOINT[1]", 45, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Escanios", "OBJ_HOTPOINT[1]", 33, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Porcentaje1", "OBJ_HOTPOINT[1]", -11, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Diferencia", "OBJ_HOTPOINT[1]", -11, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Porcentaje1", "OBJ_HOTPOINT[1]", -16, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Diferencia", "OBJ_HOTPOINT[1]", -16, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG", "OBJ_DISPLACEMENT[2]", 25, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP", "OBJ_DISPLACEMENT[2]", 25, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/Logo", "OBJ_DISPLACEMENT[2]", 25, 0.5, 0));
            }

            if (triggeredPartyCode is null || string.Equals(partido.Codigo, triggeredPartyCode, StringComparison.OrdinalIgnoreCase))
            {
                if (isExpanded)
                {
                    lines.Add(EventRun($"VIDEOS/{sceneObjectId}/ENTRA"));
                }
            }

            if (i < count - 1)
            {
                posicionAcumulada += currentWidth + 10;
            }
        }

        if (usarExpansionDinamica)
        {
            var baseWidth = (numExpandidos == count) ? minWidth : widthOthers;
            lines.Add(ItemGoNumeric("fichaPartido", "PRIM_RECGLO_LEN[0]", baseWidth, 0.5, 0));
            var ultimoAncho = state.PartidosExpandidos.Any(c => string.Equals(c, partidosActivos[^1].Codigo, StringComparison.OrdinalIgnoreCase))
                ? minWidth
                : widthOthers;
            lines.AddRange(BuildRecolocarPartidosNoActivosLines(
                partidoIdMap,
                partidosActivos,
                tipo,
                posicionAcumulada + ultimoAncho,
                baseWidth,
                useItemGo: true,
                animTime: 0.5,
                delay: 0));
        }

        lines.Add(EventRun("Titular/Ocultar"));
        return lines;
    }

    private IEnumerable<string> ResolveVideoOut(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var partidoSeleccionado = TryResolveParty(snapshot, request.CommandValue);
        if (partidoSeleccionado is null)
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            state.PartidosExpandidos.RemoveAll(c => string.Equals(c, partidoSeleccionado.Codigo, StringComparison.OrdinalIgnoreCase));

            var lines = new List<string>();
            var partidoIdMap = BuildPartidoIdMap(snapshot);
            var partidosActivos = GetOrderedActiveParties(snapshot, request.Oficiales);
            var count = partidosActivos.Count;
            if (count == 0)
            {
                return lines;
            }

            var usarExpansionDinamica = count > 4;
            var totalWidthAvailable = 1748.0 - (10 * (count - 1));
            var minWidth = 1748.0 / 5.0;

            var numExpandidos = partidosActivos.Count(p =>
                state.PartidosExpandidos.Any(c => string.Equals(c, p.Codigo, StringComparison.OrdinalIgnoreCase)));

            if (numExpandidos == 0)
            {
                lines.Add(EventRun("Titular/Recuperar"));
            }

            double widthOthers;
            if (!usarExpansionDinamica || numExpandidos == 0)
            {
                widthOthers = totalWidthAvailable / count;
            }
            else
            {
                widthOthers = (count - numExpandidos) > 0
                    ? (totalWidthAvailable - (numExpandidos * minWidth)) / (count - numExpandidos)
                    : 0;
            }

            var tipo = request.Oficiales ? "Escrutinio" : "Sondeo";
            double posicionAcumulada = 0;

            for (int i = 0; i < count; i++)
            {
                var partido = partidosActivos[i];
                var sceneObjectId = GetPartyIdFromMap(partidoIdMap, partido.Codigo, i + 1);
                var isExpanded = state.PartidosExpandidos.Any(c => string.Equals(c, partido.Codigo, StringComparison.OrdinalIgnoreCase));

                double currentWidth;
                double scaleX;
                double scaleZ;

                if (isExpanded)
                {
                    currentWidth = usarExpansionDinamica ? minWidth : widthOthers;
                    scaleX = (usarExpansionDinamica && widthOthers > 0) ? (minWidth / widthOthers) : 1.0;
                    scaleZ = 1.4;
                }
                else
                {
                    currentWidth = widthOthers;
                    scaleX = 1.0;
                    scaleZ = 1.0;
                }

                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}", "OBJ_DISPLACEMENT[0]", posicionAcumulada, 0.5, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[0]", scaleX, 0.5, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[2]", scaleZ, 0.5, 0.3));

                if (string.Equals(partido.Codigo, partidoSeleccionado.Codigo, StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DirectoMascara/Directo", "OBJ_DISPLACEMENT[2]", 0, 0.5, 0));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Escanios", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Escanios", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Porcentaje1", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Diferencia", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Porcentaje1", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Diferencia", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG", "OBJ_DISPLACEMENT[2]", -7, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP", "OBJ_DISPLACEMENT[2]", -7, 0.3, 0.3));
                    lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/Logo", "OBJ_DISPLACEMENT[2]", -15, 0.3, 0.3));
                    lines.Add(EventRun($"VIDEOS/{sceneObjectId}/SALE"));
                }

                if (i < count - 1)
                {
                    posicionAcumulada += currentWidth + 10;
                }
            }

            if (usarExpansionDinamica)
            {
                var baseWidth = (numExpandidos == count) ? minWidth : widthOthers;
                lines.Add(ItemGoNumeric("fichaPartido", "PRIM_RECGLO_LEN[0]", baseWidth, 0.5, 0.3));
                var ultimoAncho = state.PartidosExpandidos.Any(c => string.Equals(c, partidosActivos[^1].Codigo, StringComparison.OrdinalIgnoreCase))
                    ? minWidth
                    : widthOthers;
                lines.AddRange(BuildRecolocarPartidosNoActivosLines(
                    partidoIdMap,
                    partidosActivos,
                    tipo,
                    posicionAcumulada + ultimoAncho,
                    baseWidth,
                    useItemGo: true,
                    animTime: 0.5,
                    delay: 0.3));
            }

            return lines;
        }
    }

    private IEnumerable<string> ResolveVideoInTodos(OperationRequest request, BrainStormSnapshot snapshot)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            var partidosActivos = GetOrderedActiveParties(snapshot, request.Oficiales);
            foreach (var p in partidosActivos)
            {
                if (!state.PartidosExpandidos.Any(c => string.Equals(c, p.Codigo, StringComparison.OrdinalIgnoreCase)))
                {
                    state.PartidosExpandidos.Add(p.Codigo);
                }
            }

            return BuildVideoInLines(request.Oficiales, snapshot, state, triggeredPartyCode: null);
        }
    }

    private IEnumerable<string> ResolveVideoOutTodos(OperationRequest request, BrainStormSnapshot snapshot)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            return ResolveVideoOutTodosInternal(request, snapshot, state);
        }
    }

    private static List<string> ResolveVideoOutTodosInternal(
        OperationRequest request,
        BrainStormSnapshot snapshot,
        FaldonState state)
    {
        var lines = new List<string>();
        if (state.PartidosExpandidos.Count == 0)
        {
            return lines;
        }

        var partidosCerrando = state.PartidosExpandidos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.PartidosExpandidos.Clear();

        lines.Add(EventRun("Titular/Recuperar"));

        var partidoIdMap = BuildPartidoIdMap(snapshot);
        var partidosActivos = GetOrderedActiveParties(snapshot, request.Oficiales);
        var count = partidosActivos.Count;
        if (count == 0)
        {
            return lines;
        }

        var usarExpansionDinamica = count > 4;
        var totalWidthAvailable = 1748.0 - (10 * (count - 1));
        var widthOthers = totalWidthAvailable / count;
        var tipo = request.Oficiales ? "Escrutinio" : "Sondeo";
        double posicionAcumulada = 0;

        for (int i = 0; i < count; i++)
        {
            var partido = partidosActivos[i];
            var sceneObjectId = GetPartyIdFromMap(partidoIdMap, partido.Codigo, i + 1);

            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}", "OBJ_DISPLACEMENT[0]", posicionAcumulada, 0.5, 0.3));
            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[0]", 1.0, 0.5, 0.3));
            lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/fichaPartido", "OBJ_SCALE[2]", 1.0, 0.5, 0.3));

            if (partidosCerrando.Contains(partido.Codigo))
            {
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DirectoMascara/Directo", "OBJ_DISPLACEMENT[2]", 0, 0.5, 0));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Escanios", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Escanios", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Porcentaje1", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG/Diferencia", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Porcentaje1", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP/Diferencia", "OBJ_HOTPOINT[1]", 0, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosG", "OBJ_DISPLACEMENT[2]", -7, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/DatosP", "OBJ_DISPLACEMENT[2]", -7, 0.3, 0.3));
                lines.Add(ItemGoNumeric($"Graficos/{tipo}/partidos/partido{sceneObjectId}/Logo", "OBJ_DISPLACEMENT[2]", -15, 0.3, 0.3));
                lines.Add(EventRun($"VIDEOS/{sceneObjectId}/SALE"));
            }

            if (i < count - 1)
            {
                posicionAcumulada += widthOthers + 10;
            }
        }

        if (usarExpansionDinamica)
        {
            lines.Add(ItemGoNumeric("fichaPartido", "PRIM_RECGLO_LEN[0]", widthOthers, 0.5, 0.3));
            lines.AddRange(BuildRecolocarPartidosNoActivosLines(
                partidoIdMap,
                partidosActivos,
                tipo,
                posicionAcumulada + widthOthers,
                widthOthers,
                useItemGo: true,
                animTime: 0.5,
                delay: 0.3));
        }

        return lines;
    }

    private IEnumerable<string> ResolveTickerTdEntra(OperationRequest request, BrainStormSnapshot snapshot)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            var partidosOrdenados = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);
            var partidosActivos = partidosOrdenados.Where(p => ResolveEscanos(p, request.Oficiales) > 0).ToList();
            var partidosNoActivos = partidosOrdenados.Where(p => ResolveEscanos(p, request.Oficiales) == 0).ToList();

            var count = Math.Clamp(partidosActivos.Count, 1, 12);
            if (!TickerTdLayouts.TryGetValue(count, out var layout))
            {
                return [];
            }

            state.LastTickerTdActiveSiglas = partidosActivos
                .Take(count)
                .Select(p => NormalizeSiglasForScene(p.Siglas))
                .ToList();

            var lines = new List<string>
            {
                ItemGoString("NumeroEscrutado", "TEXT_STRING", $"{snapshot.Escrutado.ToString("0.##", CultureInfo.InvariantCulture)}%", 0.5, 0),
                ItemGoNumeric("Pastilla", "PRIM_BAR_LEN[0]", layout.Size, 0.5, 0)
            };

            for (int i = 0; i < count && i < partidosActivos.Count; i++)
            {
                var partido = partidosActivos[i];
                var siglaObj = NormalizeSiglasForScene(partido.Siglas);
                lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[0]", layout.Positions[i], 0.5, 0));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Logo", "OBJ_DISPLACEMENT[0]", layout.LogoPos));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Escaños", "OBJ_DISPLACEMENT[0]", layout.EscanosPos));
                lines.Add(ItemGoNumeric($"DatoTD/{siglaObj}", "MAP_INT_PAR", ResolveEscanos(partido, request.Oficiales), 0.5, 0));
            }

            foreach (var partido in partidosNoActivos)
            {
                var siglaObj = NormalizeSiglasForScene(partido.Siglas);
                lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[0]", 1370, 0.5, 0));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Logo", "OBJ_DISPLACEMENT[0]", layout.LogoPos));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Escaños", "OBJ_DISPLACEMENT[0]", layout.EscanosPos));
                lines.Add(ItemGoNumeric($"DatoTD/{siglaObj}", "MAP_INT_PAR", 0, 0.5, 0));
            }

            lines.Add(EventRun("FALDON_TD/ENTRA"));
            return lines;
        }
    }

    private IEnumerable<string> ResolveTickerTdActualiza(OperationRequest request, BrainStormSnapshot snapshot)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateFaldonState(request.Module);
            var partidosOrdenados = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);
            var partidosActivos = partidosOrdenados.Where(p => ResolveEscanos(p, request.Oficiales) > 0).ToList();
            var partidosNoActivos = partidosOrdenados.Where(p => ResolveEscanos(p, request.Oficiales) == 0).ToList();

            var count = Math.Clamp(partidosActivos.Count, 1, 12);
            if (!TickerTdLayouts.TryGetValue(count, out var layout))
            {
                return [];
            }

            var oldPositions = state.LastTickerTdActiveSiglas
                .Select((sigla, index) => (sigla, index))
                .ToDictionary(x => x.sigla, x => x.index, StringComparer.OrdinalIgnoreCase);

            var lines = new List<string>
            {
                ItemGoString("NumeroEscrutado", "TEXT_STRING", $"{snapshot.Escrutado.ToString("0.##", CultureInfo.InvariantCulture)}%", 0.5, 0),
                ItemGoNumeric("Pastilla", "PRIM_BAR_LEN[0]", layout.Size, 0.5, 0)
            };

            for (int i = 0; i < count && i < partidosActivos.Count; i++)
            {
                var partido = partidosActivos[i];
                var siglaObj = NormalizeSiglasForScene(partido.Siglas);
                var newPosIndex = i;

                if (oldPositions.TryGetValue(siglaObj, out var oldPosIndex) && oldPosIndex > newPosIndex)
                {
                    lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[1]", -100, 0.25, 0));
                    lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[0]", layout.Positions[newPosIndex], 0.5, 0.25));
                    lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[1]", 0, 0.25, 0.75));
                }
                else
                {
                    lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[0]", layout.Positions[newPosIndex], 0.5, 0));
                }

                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Logo", "OBJ_DISPLACEMENT[0]", layout.LogoPos));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Escaños", "OBJ_DISPLACEMENT[0]", layout.EscanosPos));
                lines.Add(ItemGoNumeric($"DatoTD/{siglaObj}", "MAP_INT_PAR", ResolveEscanos(partido, request.Oficiales), 0.5, 0));
            }

            foreach (var partido in partidosNoActivos)
            {
                var siglaObj = NormalizeSiglasForScene(partido.Siglas);
                lines.Add(ItemGoNumeric($"Partidos/{siglaObj}", "OBJ_DISPLACEMENT[0]", 1370, 0.5, 0));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Logo", "OBJ_DISPLACEMENT[0]", layout.LogoPos));
                lines.Add(ItemSetNumeric($"Partidos/{siglaObj}/Escaños", "OBJ_DISPLACEMENT[0]", layout.EscanosPos));
                lines.Add(ItemGoNumeric($"DatoTD/{siglaObj}", "MAP_INT_PAR", 0, 0.5, 0));
            }

            state.LastTickerTdActiveSiglas = partidosActivos
                .Take(count)
                .Select(p => NormalizeSiglasForScene(p.Siglas))
                .ToList();

            return lines;
        }
    }

    private IEnumerable<string> ResolveFaldonSedesEntra(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var slotId = TryResolvePartySlotId(snapshot, request.CommandValue);
        if (string.IsNullOrEmpty(slotId))
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateSedesState(request.Module);
            return
            [
                MapString($"datosSede{state.CurrentSlot}", slotId),
                EventRun($"Sede{state.CurrentSlot}/Entra")
            ];
        }
    }

    private IEnumerable<string> ResolveSuperfaldonSedesEntra(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var partyCode = ResolveSedePartyCode(snapshot, request.CommandValue);
        if (string.IsNullOrWhiteSpace(partyCode))
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateSedesState(request.Module);
            state.IsSuperfaldonSedeOnAir = true;
        }

        return
        [
            MapString("codigoSede", $"'{partyCode}'"),
            EventRun("Superfaldon/Sedes/DespliegaSede")
        ];
    }

    private IEnumerable<string> ResolveSuperfaldonSedesSale(OperationRequest request)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateSedesState(request.Module);
            state.IsSuperfaldonSedeOnAir = false;
        }

        return [EventRun("Superfaldon/Sedes/RepliegaSede")];
    }

    private IEnumerable<string> ResolveSuperfaldonSedesEncadena(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var partyCode = ResolveSedePartyCode(snapshot, request.CommandValue);
        if (string.IsNullOrWhiteSpace(partyCode))
        {
            return [];
        }

        return
        [
            MapString("nextCodigoSede", $"'{partyCode}'"),
            EventRun("Superfaldon/Sedes/PreparaEncadenaSede"),
            EventRun("Superfaldon/Sedes/EncadenaSede")
        ];
    }

    private IEnumerable<string> ResolveFaldonSedesEncadena(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var slotId = TryResolvePartySlotId(snapshot, request.CommandValue);
        if (string.IsNullOrEmpty(slotId))
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateSedesState(request.Module);

            var sedeSiguiente = state.CurrentSlot == "01" ? "04" : "03";
            var otraSede = state.CurrentSlot == "01" ? "02" : "01";
            state.CurrentSlot = otraSede;

            return
            [
                MapString($"datosSede{otraSede}", slotId),
                EventRun($"Sede{sedeSiguiente}")
            ];
        }
    }

    private IEnumerable<string> ResolveFaldonSedesSale(OperationRequest request)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateSedesState(request.Module);
            var currentSlot = state.CurrentSlot;
            state.CurrentSlot = "01";

            return [EventRun($"Sede{currentSlot}/Sale")];
        }
    }

    private IEnumerable<string> ResolvePactosSale(OperationRequest request)
    {
        var eventRoot = GetPactometroEventRoot(request.Oficiales);

        lock (_stateLock)
        {
            _pactosStates.Remove(request.Module);
        }

        return
        [
            EventRun($"{eventRoot}/Sale"),
            EventRun($"{eventRoot}/reinicioPactometroIzq"),
            EventRun($"{eventRoot}/reinicioPactometroDer")
        ];
    }

    private IEnumerable<string> ResolvePactosReinicio(OperationRequest request)
    {
        var eventRoot = GetPactometroEventRoot(request.Oficiales);

        lock (_stateLock)
        {
            _pactosStates.Remove(request.Module);
        }

        return
        [
            EventRun($"{eventRoot}/reinicioPactometroIzq"),
            EventRun($"{eventRoot}/reinicioPactometroDer")
        ];
    }

    private IEnumerable<string> ResolvePactosEntra(OperationRequest request, BrainStormSnapshot snapshot, bool izquierda)
    {
        var partido = TryResolveParty(snapshot, request.CommandValue);
        if (partido is null)
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreatePactosState(request.Module);
            var list = izquierda ? state.PartidosIzquierda : state.PartidosDerecha;
            var eventRoot = GetPactometroEventRoot(request.Oficiales);
            var sceneRoot = GetPactometroSceneRoot(request.Oficiales);

            if (!list.Any(x => string.Equals(x, partido.Codigo, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(partido.Codigo);
            }

            RecalculatePactosState(state, snapshot, request.Oficiales);

            var seatCountDesde = izquierda ? state.EscaniosIzquierdaDesde : state.EscaniosDerechaDesde;
            var seatCountHasta = izquierda ? state.EscaniosIzquierdaHasta : state.EscaniosDerechaHasta;
            var widthDesde = izquierda ? state.AnchoIzquierdaDesde : state.AnchoDerechaDesde;
            var widthHasta = izquierda ? state.AnchoIzquierdaHasta : state.AnchoDerechaHasta;
            var isFirst = list.Count == 1;
            var logoId = ResolvePartySlotId(snapshot, partido.Codigo);
            var logoSlot = list.Count.ToString("D2", CultureInfo.InvariantCulture);

            var lines = new List<string>();

            if (izquierda)
            {
                if (request.Oficiales)
                {
                    lines.Add(ItemSetNumeric("Pactometro_IzqVALOR", "MAP_INT_PAR", seatCountDesde));
                    lines.Add(ItemGoNumeric("BarraIzquierdas", "PRIM_RECGLO_LEN[0]", widthDesde, 0.3, 0));
                }
                else
                {
                    lines.Add(ItemSetNumeric("Pactometro_IzqVALORDesde", "MAP_INT_PAR", seatCountDesde));
                    lines.Add(ItemSetNumeric("Pactometro_IzqVALORHasta", "MAP_INT_PAR", seatCountHasta));
                    lines.Add(ItemGoNumeric("BarraIzquierdasDesde", "PRIM_RECGLO_LEN[0]", widthDesde, 0.3, 0));
                    lines.Add(ItemGoNumeric("BarraIzquierdasHasta", "PRIM_RECGLO_LEN[0]", widthHasta, 0.3, 0));
                }
            }
            else
            {
                if (request.Oficiales)
                {
                    lines.Add(ItemSetNumeric("Pactometro_DerVALOR", "MAP_INT_PAR", seatCountDesde));
                    lines.Add(ItemGoNumeric("BarraDerechas", "PRIM_RECGLO_LEN[0]", widthDesde, 0.3, 0));
                }
                else
                {
                    lines.Add(ItemSetNumeric("Pactometro_DerVALORDesde", "MAP_INT_PAR", seatCountDesde));
                    lines.Add(ItemSetNumeric("Pactometro_DerVALORHasta", "MAP_INT_PAR", seatCountHasta));
                    lines.Add(ItemGoNumeric("BarraDerechasDesde", "PRIM_RECGLO_LEN[0]", widthDesde, 0.3, 0));
                    lines.Add(ItemGoNumeric("BarraDerechasHasta", "PRIM_RECGLO_LEN[0]", widthHasta, 0.3, 0));
                }
            }

            var seatCountMayoria = request.Oficiales ? seatCountDesde : seatCountHasta;
            if (snapshot.MayoriaAbsoluta > 0 && seatCountMayoria >= snapshot.MayoriaAbsoluta)
            {
                lines.Add(EventRun($"{eventRoot}/PasaMayoria"));
            }

            if (izquierda)
            {
                lines.Add(ItemSetString($"{sceneRoot}/Izq/LogosIzq/Logo{logoSlot}", "OBJ_OVERMAT", $"Logos/Logo{logoId}"));
                if (isFirst)
                {
                    lines.Add(ItemSetNoValue($"{sceneRoot}/Izq/LogosIzq", "OBJ_GRID_JUMP_TO_END"));
                    lines.Add(EventRun($"{eventRoot}/lanzaPactometroIzq"));
                    if (request.Oficiales)
                    {
                        lines.Add(ItemSetString($"{sceneRoot}/Izq/BarraIzquierdas", "OBJ_OVERMAT", logoId));
                    }
                    else
                    {
                        lines.Add(ItemSetString($"{sceneRoot}/Izq/BarraIzquierdasDesde", "OBJ_OVERMAT", logoId));
                        lines.Add(ItemSetString($"{sceneRoot}/Izq/BarraIzquierdasHasta", "OBJ_OVERMAT", $"Hasta/{logoId}"));
                    }
                }
                else
                {
                    lines.Add(ItemSetNoValue($"{sceneRoot}/Izq/LogosIzq", "OBJ_GRID_JUMP_NEXT"));
                }
            }
            else
            {
                lines.Add(ItemSetString($"{sceneRoot}/Der/LogosDer/Logo{logoSlot}", "OBJ_OVERMAT", $"Logos/Logo{logoId}"));
                if (isFirst)
                {
                    lines.Add(ItemSetNoValue($"{sceneRoot}/Der/LogosDer", "OBJ_GRID_JUMP_TO_END"));
                    lines.Add(EventRun($"{eventRoot}/lanzaPactometroDer"));
                    if (request.Oficiales)
                    {
                        lines.Add(ItemSetString($"{sceneRoot}/Der/BarraDerechas", "OBJ_OVERMAT", logoId));
                    }
                    else
                    {
                        lines.Add(ItemSetString($"{sceneRoot}/Der/BarraDerechasDesde", "OBJ_OVERMAT", logoId));
                        lines.Add(ItemSetString($"{sceneRoot}/Der/BarraDerechasHasta", "OBJ_OVERMAT", $"Hasta/{logoId}"));
                    }
                }
                else
                {
                    lines.Add(ItemSetNoValue($"{sceneRoot}/Der/LogosDer", "OBJ_GRID_JUMP_NEXT"));
                }
            }

            return lines;
        }
    }

    private IEnumerable<string> ResolvePactosSaleLateral(OperationRequest request, BrainStormSnapshot snapshot, bool izquierda)
    {
        lock (_stateLock)
        {
            var state = GetOrCreatePactosState(request.Module);
            var list = izquierda ? state.PartidosIzquierda : state.PartidosDerecha;
            var sceneRoot = GetPactometroSceneRoot(request.Oficiales);

            if (list.Count == 0)
            {
                return [];
            }

            list.RemoveAt(list.Count - 1);
            RecalculatePactosState(state, snapshot, request.Oficiales);

            if (izquierda)
            {
                if (request.Oficiales)
                {
                    return
                    [
                        ItemSetNumeric("Pactometro_IzqVALOR", "MAP_INT_PAR", state.EscaniosIzquierdaDesde),
                        ItemGoNumeric("BarraIzquierdas", "PRIM_RECGLO_LEN[0]", state.AnchoIzquierdaDesde, 0.3, 0),
                        ItemSetNoValue($"{sceneRoot}/Izq/LogosIzq", "OBJ_GRID_JUMP_PREV")
                    ];
                }

                return
                [
                    ItemSetNumeric("Pactometro_IzqVALORDesde", "MAP_INT_PAR", state.EscaniosIzquierdaDesde),
                    ItemSetNumeric("Pactometro_IzqVALORHasta", "MAP_INT_PAR", state.EscaniosIzquierdaHasta),
                    ItemGoNumeric("BarraIzquierdasDesde", "PRIM_RECGLO_LEN[0]", state.AnchoIzquierdaDesde, 0.3, 0),
                    ItemGoNumeric("BarraIzquierdasHasta", "PRIM_RECGLO_LEN[0]", state.AnchoIzquierdaHasta, 0.3, 0),
                    ItemSetNoValue($"{sceneRoot}/Izq/LogosIzq", "OBJ_GRID_JUMP_PREV")
                ];
            }

            if (request.Oficiales)
            {
                return
                [
                    ItemSetNumeric("Pactometro_DerVALOR", "MAP_INT_PAR", state.EscaniosDerechaDesde),
                    ItemGoNumeric("BarraDerechas", "PRIM_RECGLO_LEN[0]", state.AnchoDerechaDesde, 0.3, 0),
                    ItemSetNoValue($"{sceneRoot}/Der/LogosDer", "OBJ_GRID_JUMP_PREV")
                ];
            }

            return
            [
                ItemSetNumeric("Pactometro_DerVALORDesde", "MAP_INT_PAR", state.EscaniosDerechaDesde),
                ItemSetNumeric("Pactometro_DerVALORHasta", "MAP_INT_PAR", state.EscaniosDerechaHasta),
                ItemGoNumeric("BarraDerechasDesde", "PRIM_RECGLO_LEN[0]", state.AnchoDerechaDesde, 0.3, 0),
                ItemGoNumeric("BarraDerechasHasta", "PRIM_RECGLO_LEN[0]", state.AnchoDerechaHasta, 0.3, 0),
                ItemSetNoValue($"{sceneRoot}/Der/LogosDer", "OBJ_GRID_JUMP_PREV")
            ];
        }
    }

    private IEnumerable<string> ResolveUltimoLimpiaPartidos(OperationRequest request)
    {
        lock (_stateLock)
        {
            var state = GetOrCreateUltimoState(request.Module);
            state.ResetPartidos();
        }

        return [EventRun("ULTIMO_ESCANO/SALE_BARRAS")];
    }

    private IEnumerable<string> ResolveUltimoEntraPartido(OperationRequest request, BrainStormSnapshot snapshot, bool izquierda)
    {
        var partido = TryResolveParty(snapshot, request.CommandValue);
        if (partido is null)
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateUltimoState(request.Module);
            var sideEntries = izquierda ? state.PartidosIzquierda : state.PartidosDerecha;
            var sideIndex = sideEntries.Count;
            if (sideIndex >= UltimoBarrasIzq.Length)
            {
                return [];
            }

            var escanios = Math.Max(0, ResolveEscanos(partido, request.Oficiales));
            var escaniosTotales = Math.Max(1, snapshot.EscaniosTotales);
            var ancho = (int)Math.Round((double)UltimoMaxFichaWidth / escaniosTotales * escanios);
            var siglasNormalizadas = NormalizeSiglasForScene(partido.Siglas);

            var barra = izquierda ? UltimoBarrasIzq[sideIndex] : UltimoBarrasDch[sideIndex];
            var grupo = izquierda ? "Barras_Izq" : "Barras_Dch";

            int posX;
            if (izquierda)
            {
                posX = UltimoPosInicialIzq + state.AnchoAcumuladoIzquierda;
                state.AnchoAcumuladoIzquierda += ancho;
                state.EscaniosAcumuladosIzquierda += escanios;
                sideEntries.Add(new UltimoEntry(partido.Codigo, siglasNormalizadas, escanios, ancho));
            }
            else
            {
                posX = UltimoPosInicialDch - state.AnchoAcumuladoDerecha;
                state.AnchoAcumuladoDerecha += ancho;
                state.EscaniosAcumuladosDerecha += escanios;
                sideEntries.Add(new UltimoEntry(partido.Codigo, siglasNormalizadas, escanios, ancho));
            }

            var lines = new List<string>
            {
                ItemSetString($"Ultimo_Escano/Barras/{barra}", "MAT_LIST_COLOR", siglasNormalizadas),
                ItemSetNumeric($"Ultimo_Escano/Mayoria_Absoluta/Barras/{grupo}/{barra}", "OBJ_DISPLACEMENT[0]", posX),
                ItemGoNumeric($"Ultimo_Escano/Barras/{barra}", "PRIM_RECGLO_LEN[0]", ancho, 0.5, 0)
            };

            if (izquierda)
            {
                lines.Add(ItemGoNumeric("Num_Izq", "MAP_FLOAT_PAR", state.EscaniosAcumuladosIzquierda, 0.5, 0));
                lines.Add(ItemGoNumeric(
                    "Txt_Izq",
                    "BIND_VOFFSET[0]",
                    state.AnchoAcumuladoIzquierda < UltimoAnchoPequenoThreshold ? 120 : 0,
                    0.3,
                    0.1));
            }
            else
            {
                lines.Add(ItemGoNumeric("Num_Dch", "MAP_FLOAT_PAR", state.EscaniosAcumuladosDerecha, 0.5, 0));
                lines.Add(ItemGoNumeric(
                    "Txt_Dch",
                    "BIND_VOFFSET[0]",
                    state.AnchoAcumuladoDerecha < UltimoAnchoPequenoThreshold ? -50 : 46,
                    0.3,
                    0.1));
            }

            return lines;
        }
    }

    private void RecalculatePactosState(PactosState state, BrainStormSnapshot snapshot, bool oficiales)
    {
        state.EscaniosIzquierdaDesde = 0;
        state.EscaniosIzquierdaHasta = 0;
        state.EscaniosDerechaDesde = 0;
        state.EscaniosDerechaHasta = 0;
        state.AnchoIzquierdaDesde = 0;
        state.AnchoIzquierdaHasta = 0;
        state.AnchoDerechaDesde = 0;
        state.AnchoDerechaHasta = 0;

        var totalEscanios = Math.Max(1, snapshot.EscaniosTotales);
        var partidosPorCodigo = snapshot.Partidos.ToDictionary(x => x.Codigo, StringComparer.OrdinalIgnoreCase);

        foreach (var codigo in state.PartidosIzquierda)
        {
            if (!partidosPorCodigo.TryGetValue(codigo, out var partido))
            {
                continue;
            }

            var escaniosDesde = oficiales
                ? Math.Max(0, partido.Escanios)
                : Math.Max(0, partido.EscaniosDesdeSondeo);
            var escaniosHasta = oficiales
                ? Math.Max(0, partido.Escanios)
                : Math.Max(0, partido.EscaniosHastaSondeo);

            state.EscaniosIzquierdaDesde += escaniosDesde;
            state.EscaniosIzquierdaHasta += escaniosHasta;
            state.AnchoIzquierdaDesde += escaniosDesde * PactosTotalWidth / totalEscanios;
            state.AnchoIzquierdaHasta += escaniosHasta * PactosTotalWidth / totalEscanios;
        }

        foreach (var codigo in state.PartidosDerecha)
        {
            if (!partidosPorCodigo.TryGetValue(codigo, out var partido))
            {
                continue;
            }

            var escaniosDesde = oficiales
                ? Math.Max(0, partido.Escanios)
                : Math.Max(0, partido.EscaniosDesdeSondeo);
            var escaniosHasta = oficiales
                ? Math.Max(0, partido.Escanios)
                : Math.Max(0, partido.EscaniosHastaSondeo);

            state.EscaniosDerechaDesde += escaniosDesde;
            state.EscaniosDerechaHasta += escaniosHasta;
            state.AnchoDerechaDesde += escaniosDesde * PactosTotalWidth / totalEscanios;
            state.AnchoDerechaHasta += escaniosHasta * PactosTotalWidth / totalEscanios;
        }
    }

    private static Dictionary<string, string> BuildPartidoIdMap(BrainStormSnapshot snapshot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var basePartidos = snapshot.CsvTemplatePartidos is { Count: > 0 }
            ? snapshot.CsvTemplatePartidos
            : snapshot.Partidos;

        var idx = 1;
        foreach (var partido in basePartidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase))
        {
            if (map.TryAdd(partido.Codigo, idx.ToString("D2", CultureInfo.InvariantCulture)))
            {
                idx++;
            }
        }

        foreach (var partido in snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase))
        {
            if (map.TryAdd(partido.Codigo, idx.ToString("D2", CultureInfo.InvariantCulture)))
            {
                idx++;
            }
        }

        return map;
    }

    private static string GetPartyIdFromMap(Dictionary<string, string> map, string codigo, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(codigo) && map.TryGetValue(codigo, out var id))
        {
            return id;
        }

        return fallbackIndex.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static List<PartidoSnapshot> GetOrderedPartiesForSnapshot(BrainStormSnapshot snapshot, bool oficiales)
    {
        var list = snapshot.Partidos.ToList();
        list.Sort((x, y) => CompareLegacyAscending(x, y, oficiales));
        list.Reverse();
        return list;
    }

    private static List<PartidoSnapshot> GetOrderedActiveParties(BrainStormSnapshot snapshot, bool oficiales)
    {
        return GetOrderedPartiesForSnapshot(snapshot, oficiales)
            .Where(p => ResolveEscanos(p, oficiales) > 0)
            .ToList();
    }

    private static int CompareLegacyAscending(PartidoSnapshot? x, PartidoSnapshot? y, bool oficiales)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xSeats = ResolveEscanos(x, oficiales);
        var ySeats = ResolveEscanos(y, oficiales);

        if (string.Equals(x.Codigo, "99999", StringComparison.OrdinalIgnoreCase))
        {
            if (xSeats > 0)
            {
                return ySeats == 0 ? 1 : -1;
            }
            return -1;
        }

        if (string.Equals(y.Codigo, "99999", StringComparison.OrdinalIgnoreCase))
        {
            if (ySeats > 0)
            {
                return xSeats == 0 ? -1 : 1;
            }
            return 1;
        }

        if (oficiales)
        {
            var cmp = x.Escanios.CompareTo(y.Escanios);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = x.PorcentajeVoto.CompareTo(y.PorcentajeVoto);
            if (cmp != 0)
            {
                return cmp;
            }

            return x.Votos.CompareTo(y.Votos);
        }
        else
        {
            var cmp = x.EscaniosHastaSondeo.CompareTo(y.EscaniosHastaSondeo);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = x.EscaniosDesdeSondeo.CompareTo(y.EscaniosDesdeSondeo);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = x.PorcentajeVoto.CompareTo(y.PorcentajeVoto);
            if (cmp != 0)
            {
                return cmp;
            }

            return x.EscaniosHistoricos.CompareTo(y.EscaniosHistoricos);
        }
    }

    private static string? TryResolvePartySlotId(BrainStormSnapshot snapshot, string commandValue)
    {
        if (!string.IsNullOrWhiteSpace(commandValue)
            && commandValue.StartsWith("slot:", StringComparison.OrdinalIgnoreCase))
        {
            var rawSlot = commandValue["slot:".Length..].Trim();
            if (int.TryParse(rawSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitSlot)
                && explicitSlot is >= 1 and <= 99)
            {
                return explicitSlot.ToString("D2", CultureInfo.InvariantCulture);
            }
        }

        var partido = TryResolveParty(snapshot, commandValue);
        if (partido is not null)
        {
            return ResolvePartySlotId(snapshot, partido.Codigo);
        }

        if (int.TryParse(commandValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id.ToString("D2", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static PartidoSnapshot? TryResolveParty(BrainStormSnapshot snapshot, string commandValue)
    {
        if (string.IsNullOrWhiteSpace(commandValue))
        {
            return null;
        }

        var value = commandValue.Trim();
        var normalizedCode = NormalizeCode(value);
        var normalizedSiglas = NormalizeToken(value);

        return snapshot.Partidos.FirstOrDefault(p =>
                string.Equals(p.Codigo, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeCode(p.Codigo), normalizedCode, StringComparison.OrdinalIgnoreCase))
            ?? snapshot.Partidos.FirstOrDefault(p =>
                string.Equals(NormalizeToken(p.Siglas), normalizedSiglas, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePartySlotId(BrainStormSnapshot snapshot, string partyCode)
    {
        var map = BuildPartidoIdMap(snapshot);
        if (map.TryGetValue(partyCode, out var slotId))
        {
            return slotId;
        }

        if (int.TryParse(partyCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed.ToString("D2", CultureInfo.InvariantCulture);
        }

        return "01";
    }

    private static string? ResolveSedePartyCode(BrainStormSnapshot snapshot, string commandValue)
    {
        var partido = TryResolveParty(snapshot, commandValue);
        if (partido is not null && !string.IsNullOrWhiteSpace(partido.Codigo))
        {
            return partido.Codigo.Trim();
        }

        if (!string.IsNullOrWhiteSpace(commandValue))
        {
            return commandValue.Trim();
        }

        return null;
    }

    private static int ResolveEscanos(PartidoSnapshot partido, bool oficiales)
    {
        return oficiales ? partido.Escanios : partido.EscaniosHastaSondeo;
    }

    private static string GetPactometroEventRoot(bool oficiales)
    {
        return oficiales ? "Pactometro" : "PactometroSondeo";
    }

    private static string GetPactometroSceneRoot(bool oficiales)
    {
        return oficiales ? "Graficos/Pactometro" : "Graficos/PactometroSondeo";
    }

    private static string NormalizeSiglasForScene(string siglas)
    {
        return siglas
            .Trim()
            .Replace("+", "_", StringComparison.Ordinal)
            .Replace("-", "_", StringComparison.Ordinal);
    }

    private static string NormalizeCode(string raw)
    {
        var value = raw.Trim().TrimStart('0');
        return value.Length == 0 ? "0" : value;
    }

    private static string NormalizeToken(string raw)
    {
        return raw
            .Trim()
            .Replace("+", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private void SyncUltimoEscanoLifecycle(OperationRequest request, BrainStormSnapshot snapshot, string scene)
    {
        if (request.Command != GraphicCommand.None)
        {
            return;
        }

        if (request.Module is not (GraphicModule.Carton or GraphicModule.Superfaldon))
        {
            return;
        }

        if (!IsUltimoScene(scene))
        {
            return;
        }

        lock (_stateLock)
        {
            var state = GetOrCreateUltimoState(request.Module);

            switch (request.Action)
            {
                case OperationActionType.Enter:
                    state.IsUltimoOnAir = true;
                    state.UltimoSnapshot = BuildUltimoEscanoSnapshot(snapshot);
                    state.UltimoSnapshotInicializado = true;
                    state.UltimoCambioDetectado = false;
                    break;
                case OperationActionType.Exit:
                    state.IsUltimoOnAir = false;
                    state.UltimoSnapshot = string.Empty;
                    state.UltimoSnapshotInicializado = false;
                    state.UltimoCambioDetectado = false;
                    state.ResetPartidos();
                    break;
            }
        }
    }

    private bool TryComposeUltimoEscanoOnAirUpdate(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        if (request.Command != GraphicCommand.None || request.Action != OperationActionType.Update)
        {
            return false;
        }

        if (request.Module is not (GraphicModule.Carton or GraphicModule.Superfaldon))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (!_ultimoEscanoStates.TryGetValue(request.Module, out var state) || !state.IsUltimoOnAir)
            {
                return false;
            }

            var snapshotActual = BuildUltimoEscanoSnapshot(snapshot);
            var (ultimoAnterior, luchaAnterior) = SplitUltimoSnapshot(state.UltimoSnapshot);
            var (ultimoActual, luchaActual) = SplitUltimoSnapshot(snapshotActual);

            var cambioUltimo = state.UltimoSnapshotInicializado
                && !string.Equals(ultimoAnterior, ultimoActual, StringComparison.Ordinal);
            var cambioLucha = state.UltimoSnapshotInicializado
                && !string.Equals(luchaAnterior, luchaActual, StringComparison.Ordinal);

            state.UltimoSnapshot = snapshotActual;
            state.UltimoSnapshotInicializado = true;
            state.UltimoCambioDetectado = cambioUltimo || cambioLucha;

            if (cambioUltimo)
            {
                lines.Add(EventRun("UltimoEscanoN/Cambio"));
                return true;
            }

            if (cambioLucha)
            {
                lines.Add(EventRun("UltimoEscanoN/CambioAbajo"));
                return true;
            }

            if (request.Module == GraphicModule.Carton)
            {
                var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
                lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                lines.Add(ItemSetNoValue("UltimoEscanoNDatos/CSV", "MAP_LLSTRING_LOAD"));
                return true;
            }

            lines.Add(EventRun("ReloadPartidos"));
            return true;
        }
    }

    private static (string Ultimo, string Lucha) SplitUltimoSnapshot(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (string.Empty, string.Empty);
        }

        var parts = raw.Split('|', 2);
        var u = parts.Length > 0 ? parts[0] : string.Empty;
        var l = parts.Length > 1 ? parts[1] : string.Empty;
        return (u, l);
    }

    private static bool IsUltimoScene(string scene)
    {
        return string.Equals(scene, "ULTIMO_ESCANO", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scene, "ULTIMOESCANO", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUltimoEscanoSnapshot(BrainStormSnapshot snapshot)
    {
        if (snapshot.Partidos.Count == 0)
        {
            return "U:|L:";
        }

        static string PartidoId(PartidoSnapshot partido)
        {
            if (!string.IsNullOrWhiteSpace(partido.Codigo))
            {
                return $"C:{partido.Codigo.Trim()}";
            }

            if (!string.IsNullOrWhiteSpace(partido.Siglas))
            {
                return $"S:{partido.Siglas.Trim()}";
            }

            return string.Empty;
        }

        var ultimo = snapshot.Partidos
            .Where(p => p.EsUltimoEscano != 0)
            .Select(PartidoId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var lucha = snapshot.Partidos
            .Where(p => p.LuchaUltimoEscano != 0)
            .Select(PartidoId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return $"U:{string.Join(",", ultimo)}|L:{string.Join(",", lucha)}";
    }

    private bool TryComposeCartonScene(OperationRequest request, BrainStormSnapshot snapshot, string scene, List<string> lines)
    {
        var normalized = scene.ToUpperInvariant();
        return normalized switch
        {
            "PARTICIPACION" => ComposeCartonParticipacion(request, snapshot, lines),
            "CCAA_CARTONES" => ComposeCartonCcaa(request, snapshot, lines),
            "MAYORIAS" => ComposeCartonMayorias(request, snapshot, lines),
            "CARRUSEL" => ComposeCartonCarrusel(request, snapshot, lines),
            "CARTON_PARTIDOS" => ComposeCartonPartidos(request, snapshot, lines),
            "ULTIMO_ESCANO" or "ULTIMOESCANO" => ComposeCartonUltimoEscano(request, lines),
            _ => false
        };
    }

    private static List<string> ResolveMapaProvinciasNames(BrainStormSnapshot snapshot)
    {
        if (snapshot.Circunscripcion.Codigo.EndsWith("00000", StringComparison.OrdinalIgnoreCase)
            && snapshot.ProvinciasAutonomia.Count > 0)
        {
            return snapshot.ProvinciasAutonomia
                .Where(p => !string.IsNullOrWhiteSpace(p.Nombre))
                .Select(p => p.Nombre.Trim())
                .ToList();
        }

        return string.IsNullOrWhiteSpace(snapshot.Circunscripcion.Nombre)
            ? []
            : [snapshot.Circunscripcion.Nombre.Trim()];
    }

    private static bool ComposeCartonParticipacion(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        switch (request.Action)
        {
            case OperationActionType.Prepare:
                lines.Add(EventRun("PARTICIPACION/PREPARA"));
                return true;

            case OperationActionType.Exit:
                lines.Add(EventRun("PARTICIPACION/SALE"));
                return true;

            case OperationActionType.Enter:
            case OperationActionType.Update:
                var isEnter = request.Action == OperationActionType.Enter;
                lines.Add(EventRun(isEnter ? "PARTICIPACION/PREPARA" : "PARTICIPACION/ENCADENA"));

                var provincias = ResolveMapaProvinciasNames(snapshot);
                var mapDelay = isEnter ? 0.0 : 0.3;
                foreach (var prov in provincias)
                {
                    lines.Add(ItemGoNumeric($"Participacion/Mapa/{prov}", "OBJ_CULL", 0, 0.3, mapDelay));
                }

                lines.Add(ItemSetString("Participacion/LugarTxt", "TEXT_STRING", snapshot.Circunscripcion.Nombre));

                var (leftTime, rightTime, leftVal, rightVal) = snapshot.Avance switch
                {
                    1 => (snapshot.HoraAvance1Historico, snapshot.HoraAvance1, snapshot.Avance1Historico, snapshot.Avance1),
                    2 => (snapshot.HoraAvance2Historico, snapshot.HoraAvance2, snapshot.Avance2Historico, snapshot.Avance2),
                    3 => (snapshot.HoraAvance3Historico, snapshot.HoraAvance3, snapshot.Avance3Historico, snapshot.Avance3),
                    _ => ("100%", "100%", snapshot.ParticipacionHistorica, snapshot.Participacion)
                };

                lines.Add(ItemSetString("Participacion/Hora_Izq", "TEXT_STRING", leftTime));
                lines.Add(ItemSetString("Participacion/Hora_Dch", "TEXT_STRING", rightTime));

                var barDelay = isEnter ? 0.6 : 0.3;
                lines.Add(ItemGoNumeric("Participacion_Barra_Izq", "MAP_FLOAT_PAR", Math.Round(leftVal, 2), 0.5, barDelay));
                lines.Add(ItemGoNumeric("Participacion_Barra_Dch", "MAP_FLOAT_PAR", Math.Round(rightVal, 2), 0.5, barDelay));

                if (rightVal < 25.0)
                {
                    lines.Add(isEnter
                        ? ItemSetNumeric("Participacion/Barras/Barra_Dch/Txt_Dch", "OBJ_OFFSET[2]", 322)
                        : ItemGoNumeric("Participacion/Barras/Barra_Dch/Txt_Dch", "OBJ_OFFSET[2]", 322, 0.5, 0));
                    lines.Add(ItemSetString("Participacion/Txt_Dch", "TEXT_FONT", "Heavy_Naranja"));
                    lines.Add(ItemSetString("Participacion/Txt_Izq", "TEXT_FONT", "Heavy"));
                }
                else
                {
                    lines.Add(isEnter
                        ? ItemSetNumeric("Participacion/Barras/Barra_Dch/Txt_Dch", "OBJ_OFFSET[2]", 190)
                        : ItemGoNumeric("Participacion/Barras/Barra_Dch/Txt_Dch", "OBJ_OFFSET[2]", 190, 0.5, 0));
                    lines.Add(ItemSetString("Participacion/Txt_Dch", "TEXT_FONT", "Heavy_Negro"));
                    lines.Add(ItemSetString("Participacion/Txt_Izq", "TEXT_FONT", "Heavy_Negro"));
                }

                if (isEnter)
                {
                    lines.Add(EventRun("PARTICIPACION/ENTRA"));
                }
                return true;

            default:
                return false;
        }
    }

    private static bool ComposeCartonCcaa(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        switch (request.Action)
        {
            case OperationActionType.Prepare:
                lines.Add(EventRun("CCAA_CARTONES/PREPARA"));
                return true;
            case OperationActionType.Update:
                lines.Add(EventRun("CCAA/CAMBIA"));
                return true;
            case OperationActionType.Exit:
                lines.Add(EventRun("CCAA/SALE"));
                return true;
            case OperationActionType.Enter:
                lines.Add(EventRun("CCAA_CARTONES/PREPARA"));
                lines.Add(ItemSetString("CCAA_Carton/LugarTxt", "TEXT_STRING", snapshot.Circunscripcion.Nombre));
                foreach (var prov in ResolveMapaProvinciasNames(snapshot))
                {
                    lines.Add(ItemGoNumeric($"CCAA_Carton/Mapa/{prov}", "OBJ_CULL", 0, 0.3, 0));
                }

                var partidos = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);
                for (int i = 0; i < partidos.Count; i++)
                {
                    var sigla = NormalizeSiglasForScene(partidos[i].Siglas);
                    lines.Add(ItemSetNumeric($"CCAA_Carton/Fichas/{sigla}", "OBJ_DISPLACEMENT[2]", i * -110));
                    lines.Add(ItemSetNumeric($"CCAA_Carton/Fichas/{sigla}", "OBJ_CULL", 0));
                }

                lines.Add(EventRun("CCAA_CARTONES/ENTRA"));
                return true;
            default:
                return false;
        }
    }

    private static bool ComposeCartonMayorias(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        switch (request.Action)
        {
            case OperationActionType.Prepare:
                lines.Add(EventRun("MAYORIAS/PREPARA"));
                return true;
            case OperationActionType.Exit:
                lines.Add(EventRun("MAYORIAS/SALE"));
                return true;
            case OperationActionType.Enter:
                lines.Add(EventRun("MAYORIAS/PREPARA"));
                lines.Add(ItemSetString("Mayorias/LugarTxt1", "TEXT_STRING", snapshot.Circunscripcion.Nombre));

                var partidos = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);
                var topSigla = partidos.Count > 0 ? NormalizeSiglasForScene(partidos[0].Siglas) : string.Empty;

                if (snapshot.ProvinciasAutonomia.Count > 0)
                {
                    foreach (var prov in snapshot.ProvinciasAutonomia)
                    {
                        var color = !string.IsNullOrWhiteSpace(prov.SiglaGanador)
                            ? NormalizeSiglasForScene(prov.SiglaGanador)
                            : topSigla;
                        lines.Add(ItemSetString($"Mayorias/{prov.Nombre}", "MAT_LIST_COLOR", color));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(snapshot.Circunscripcion.Nombre) && !string.IsNullOrWhiteSpace(topSigla))
                {
                    lines.Add(ItemSetString($"Mayorias/{snapshot.Circunscripcion.Nombre}", "MAT_LIST_COLOR", topSigla));
                }

                for (int i = 0; i < partidos.Count; i++)
                {
                    var sigla = NormalizeSiglasForScene(partidos[i].Siglas);
                    lines.Add(ItemSetNumeric($"Mapa_Mayorias/Fichas/{sigla}", "OBJ_DISPLACEMENT[2]", i * -100));
                    lines.Add(ItemGoNumeric($"Mapa_Mayorias/Fichas/{sigla}", "OBJ_CULL", 0, 0, 0.5));
                }

                var pagesToShow = Math.Clamp((int)Math.Ceiling(partidos.Count / 6.0), 1, 3);
                for (int page = 1; page <= 3; page++)
                {
                    lines.Add(ItemGoNumeric($"Mapa_Mayorias/Paginas/Pagina{page}", "OBJ_CULL", page <= pagesToShow ? 0 : 1, 0.3, 0));
                }

                lines.Add(EventRun("MAYORIAS/ENTRA"));
                return true;
            default:
                return false;
        }
    }

    private bool ComposeCartonCarrusel(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        var modeFolder = request.Oficiales ? "Oficiales" : "Sondeo";
        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var partidos = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);

        lock (_stateLock)
        {
            var state = GetOrCreateCartonState(request.Module);

            switch (request.Action)
            {
                case OperationActionType.Prepare:
                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    lines.Add(EventRun("CARRUSEL/PREPARA"));
                    return true;

                case OperationActionType.Exit:
                    lines.Add(EventRun("CARRUSEL/SALE"));
                    return true;

                case OperationActionType.Enter:
                    lines.Add(EventRun(request.Oficiales ? "CARRUSEL/OFICIAL" : "CARRUSEL/SONDEO"));
                    foreach (var p in partidos)
                    {
                        var sigla = NormalizeSiglasForScene(p.Siglas);
                        lines.Add(ItemSetNumeric($"Carton_Carrusel/{modeFolder}/{sigla}", "OBJ_DISPLACEMENT[0]", 1920));
                    }

                    var selectedParty = TryResolveParty(snapshot, request.CommandValue) ?? partidos.FirstOrDefault();
                    if (selectedParty is not null)
                    {
                        var siglaSel = NormalizeSiglasForScene(selectedParty.Siglas);
                        state.CurrentCarruselSigla = siglaSel;
                        lines.Add(ItemGoNumeric($"Carton_Carrusel/{modeFolder}/{siglaSel}", "OBJ_DISPLACEMENT[0]", 0, 0.5, 0));
                    }

                    lines.Add(EventRun("CARRUSEL/ENTRA"));
                    return true;

                case OperationActionType.Update:
                    foreach (var p in partidos)
                    {
                        var sigla = NormalizeSiglasForScene(p.Siglas);
                        if (!string.Equals(sigla, state.CurrentCarruselSigla, StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(ItemSetNumeric($"Carton_Carrusel/{modeFolder}/{sigla}", "OBJ_DISPLACEMENT[0]", 1920));
                        }
                    }

                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    return true;

                default:
                    return false;
            }
        }
    }

    private IEnumerable<string> ResolveCarruselEncadena(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var modeFolder = request.Oficiales ? "Oficiales" : "Sondeo";
        var partidos = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);
        if (partidos.Count == 0)
        {
            return [];
        }

        lock (_stateLock)
        {
            var state = GetOrCreateCartonState(request.Module);
            var lines = new List<string>();
            var prevSigla = state.CurrentCarruselSigla;

            var nextParty = TryResolveParty(snapshot, request.CommandValue);
            if (nextParty is null)
            {
                var currentIndex = partidos.FindIndex(p =>
                    string.Equals(NormalizeSiglasForScene(p.Siglas), prevSigla, StringComparison.OrdinalIgnoreCase));
                var nextIndex = currentIndex >= 0 ? (currentIndex + 1) % partidos.Count : 0;
                nextParty = partidos[nextIndex];
            }

            var nextSigla = NormalizeSiglasForScene(nextParty.Siglas);
            if (!string.IsNullOrWhiteSpace(prevSigla) && !string.Equals(prevSigla, nextSigla, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(ItemGoNumeric($"Carton_Carrusel/{modeFolder}/{prevSigla}", "OBJ_DISPLACEMENT[0]", -1920, 0.5, 0));
            }

            lines.Add(ItemGoNumeric($"Carton_Carrusel/{modeFolder}/{nextSigla}", "OBJ_DISPLACEMENT[0]", 0, 0.5, 0));
            state.CurrentCarruselSigla = nextSigla;
            return lines;
        }
    }

    private static bool ComposeCartonPartidos(OperationRequest request, BrainStormSnapshot snapshot, List<string> lines)
    {
        switch (request.Action)
        {
            case OperationActionType.Prepare:
                lines.Add(EventRun("CARTON_PARTIDOS/PREPARA"));
                return true;

            case OperationActionType.Exit:
                lines.Add(EventRun("CARTON_PARTIDOS/SALE"));
                return true;

            case OperationActionType.Enter:
            case OperationActionType.Update:
                var isEnter = request.Action == OperationActionType.Enter;
                var partidos = GetOrderedPartiesForSnapshot(snapshot, request.Oficiales);

                for (int i = 0; i < partidos.Count; i++)
                {
                    var sigla = NormalizeSiglasForScene(partidos[i].Siglas);
                    var x = i < 7 ? 0 : 918;
                    var y = (i == 0 ? 0.0 : -0.2 * i).ToString("0.0", CultureInfo.InvariantCulture);
                    var z = i < 7 ? (-100 * i) : (100 - (100 * (i - 7)));

                    lines.Add(isEnter
                        ? $"itemset('<{{BD}}>Carton_Partidos/Fichas/{sigla}','OBJ_DISPLACEMENT',({x},{y},{z}));"
                        : $"itemgo('<{{BD}}>Carton_Partidos/Fichas/{sigla}','OBJ_DISPLACEMENT',({x},{y},{z}),0.5,0);");
                }

                var dotCulture = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
                dotCulture.NumberGroupSeparator = ".";

                foreach (var p in partidos)
                {
                    var sigla = NormalizeSiglasForScene(p.Siglas);
                    var escanios = ResolveEscanos(p, request.Oficiales);
                    var pct = p.PorcentajeVoto.ToString("0.##", CultureInfo.InvariantCulture);
                    var votos = p.Votos.ToString("#,##0", dotCulture);

                    lines.Add(ItemSetString($"Mayorias1/{sigla}/Partido_Escanos1", "TEXT_STRING", escanios.ToString(CultureInfo.InvariantCulture)));
                    lines.Add($"itemset('<{{BD}}>Mayorias1/{sigla}/Partido_Porcentaje1','TEXT_STRING','{pct}\\\\f<Light>%');");
                    lines.Add($"itemset('<{{BD}}>Mayorias1/{sigla}/Partido_Votos1','TEXT_STRING','{votos}\\\\f<Light> votos');");
                }

                var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
                lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                lines.Add(ItemSetNoValue("UltimoEscanoNDatos/CSV", "MAP_LLSTRING_LOAD"));

                if (isEnter)
                {
                    lines.Add(EventRun("CARTON_PARTIDOS/ENTRA"));
                }
                return true;

            default:
                return false;
        }
    }

    private static bool ComposeCartonUltimoEscano(OperationRequest request, List<string> lines)
    {
        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        switch (request.Action)
        {
            case OperationActionType.Prepare:
                lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                lines.Add(ItemSetNoValue("UltimoEscanoNDatos/CSV", "MAP_LLSTRING_LOAD"));
                lines.Add(EventRun("UltimoEscanoN/PREPARA"));
                return true;
            case OperationActionType.Enter:
                lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                lines.Add(ItemSetNoValue("UltimoEscanoNDatos/CSV", "MAP_LLSTRING_LOAD"));
                lines.Add(EventRun("UltimoEscanoN/ENTRA"));
                return true;
            case OperationActionType.Exit:
                lines.Add(EventRun("UltimoEscanoN/SALE"));
                return true;
            default:
                return false;
        }
    }

    private bool ShouldEmitPrimerosResultados(OperationRequest request, string scene)
    {
        if (request.Action != OperationActionType.Enter)
        {
            return false;
        }

        if (!string.Equals(scene, "TICKER", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lock (_stateLock)
        {
            return _animacionPrimerosEnabled && (request.Oficiales || _animacionSondeoEnabled);
        }
    }

    private void ClearModuleState(GraphicModule module)
    {
        lock (_stateLock)
        {
            _pactosStates.Remove(module);
            _sedesStates.Remove(module);
            _ultimoEscanoStates.Remove(module);
            _faldonStates.Remove(module);
            _cartonStates.Remove(module);
        }
    }

    private CartonState GetOrCreateCartonState(GraphicModule module)
    {
        if (_cartonStates.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new CartonState();
        _cartonStates[module] = state;
        return state;
    }

    private FaldonState GetOrCreateFaldonState(GraphicModule module)
    {
        if (_faldonStates.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new FaldonState();
        _faldonStates[module] = state;
        return state;
    }

    private PactosState GetOrCreatePactosState(GraphicModule module)
    {
        if (_pactosStates.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new PactosState();
        _pactosStates[module] = state;
        return state;
    }

    private SedesState GetOrCreateSedesState(GraphicModule module)
    {
        if (_sedesStates.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new SedesState();
        _sedesStates[module] = state;
        return state;
    }

    private UltimoEscanoState GetOrCreateUltimoState(GraphicModule module)
    {
        if (_ultimoEscanoStates.TryGetValue(module, out var state))
        {
            return state;
        }

        state = new UltimoEscanoState();
        _ultimoEscanoStates[module] = state;
        return state;
    }

    private static string EventRun(string path)
    {
        return ItemSetNoValue(path, "EVENT_RUN");
    }

    private static string MapString(string path, string value)
    {
        return ItemSetString(path, "MAP_STRING_PAR", value);
    }

    private static string ItemSetNoValue(string path, string property)
    {
        return $"itemset('<{{BD}}>{path}','{property}');";
    }

    private static string ItemSetString(string path, string property, string value)
    {
        return $"itemset('<{{BD}}>{path}','{property}','{EscapeValue(value)}');";
    }

    private static string ItemGoString(string path, string property, string value, double animTime, double delay)
    {
        return $"itemgo('<{{BD}}>{path}','{property}','{EscapeValue(value)}',{FormatNumber(animTime)},{FormatNumber(delay)});";
    }

    private static string ItemSetNumeric(string path, string property, double value)
    {
        return $"itemset('<{{BD}}>{path}','{property}',{FormatNumber(value)});";
    }

    private static string ItemGoNumeric(string path, string property, double value, double animTime, double delay)
    {
        return $"itemgo('<{{BD}}>{path}','{property}',{FormatNumber(value)},{FormatNumber(animTime)},{FormatNumber(delay)});";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###############", CultureInfo.InvariantCulture);
    }

    private static string EscapeValue(string value)
    {
        return value
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private sealed class FaldonState
    {
        public List<string> PartidosExpandidos { get; } = [];
        public List<string> LastTickerTdActiveSiglas { get; set; } = [];
    }

    private sealed class PactosState
    {
        public List<string> PartidosIzquierda { get; } = [];
        public List<string> PartidosDerecha { get; } = [];

        public int EscaniosIzquierdaDesde { get; set; }
        public int EscaniosIzquierdaHasta { get; set; }
        public int EscaniosDerechaDesde { get; set; }
        public int EscaniosDerechaHasta { get; set; }
        public double AnchoIzquierdaDesde { get; set; }
        public double AnchoIzquierdaHasta { get; set; }
        public double AnchoDerechaDesde { get; set; }
        public double AnchoDerechaHasta { get; set; }
    }

    private sealed class CartonState
    {
        public string CurrentCarruselSigla { get; set; } = string.Empty;
    }

    private sealed class SedesState
    {
        public string CurrentSlot { get; set; } = "01";
        public bool IsSuperfaldonSedeOnAir { get; set; }
    }

    private sealed class UltimoEscanoState
    {
        public List<UltimoEntry> PartidosIzquierda { get; } = [];
        public List<UltimoEntry> PartidosDerecha { get; } = [];

        public int AnchoAcumuladoIzquierda { get; set; }
        public int AnchoAcumuladoDerecha { get; set; }
        public int EscaniosAcumuladosIzquierda { get; set; }
        public int EscaniosAcumuladosDerecha { get; set; }
        public bool IsUltimoOnAir { get; set; }
        public string UltimoSnapshot { get; set; } = string.Empty;
        public bool UltimoSnapshotInicializado { get; set; }
        public bool UltimoCambioDetectado { get; set; }

        public void ResetPartidos()
        {
            PartidosIzquierda.Clear();
            PartidosDerecha.Clear();
            AnchoAcumuladoIzquierda = 0;
            AnchoAcumuladoDerecha = 0;
            EscaniosAcumuladosIzquierda = 0;
            EscaniosAcumuladosDerecha = 0;
        }
    }

    private sealed record UltimoEntry(string Codigo, string Siglas, int Escanios, int Ancho);
}
