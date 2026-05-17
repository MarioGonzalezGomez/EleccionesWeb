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

    private readonly object _stateLock = new();
    private readonly Dictionary<GraphicModule, PactosState> _pactosStates = new();
    private readonly Dictionary<GraphicModule, SedesState> _sedesStates = new();
    private readonly Dictionary<GraphicModule, UltimoEscanoState> _ultimoEscanoStates = new();
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
            OperationActionType.Enter => "ULTIMO/Entra",
            OperationActionType.Exit => "ULTIMO/Sale",
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
            GraphicCommand.TickerTDEntra => [EventRun("FALDON_TD/ENTRA")],
            GraphicCommand.TickerTDActualiza =>
            [
                ItemSetString("NumeroEscrutado", "TEXT_STRING", $"{snapshot.Escrutado.ToString("0.##", CultureInfo.InvariantCulture)}%"),
                EventRun("FALDON_TD/ACTUALIZA")
            ],
            GraphicCommand.TickerTDSale => [EventRun("FALDON_TD/SALE")],

            GraphicCommand.VideoIn => ResolveVideoIn(request, snapshot),
            GraphicCommand.VideoOut => ResolveVideoOut(request, snapshot),
            GraphicCommand.VideoInTodos => ResolveVideoInTodos(snapshot),
            GraphicCommand.VideoOutTodos => ResolveVideoOutTodos(snapshot),

            GraphicCommand.SedesEntra => request.Module == GraphicModule.Superfaldon
                ? ResolveSuperfaldonSedesEntra(request, snapshot)
                : ResolveFaldonSedesEntra(request, snapshot),
            GraphicCommand.SedesEncadena => request.Module == GraphicModule.Superfaldon
                ? ResolveSuperfaldonSedesEncadena(request, snapshot)
                : ResolveFaldonSedesEncadena(request, snapshot),
            GraphicCommand.SedesSale => request.Module == GraphicModule.Superfaldon
                ? [EventRun("Superfaldon/Sedes/RepliegaSede")]
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

    private IEnumerable<string> ResolveVideoIn(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var slotId = TryResolvePartySlotId(snapshot, request.CommandValue);
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return [];
        }

        var mapSuffix = ResolveVideoMapSuffix(snapshot, request.CommandValue, slotId);
        return
        [
            ItemSetNoValue($"TICKER/FNC_Video{mapSuffix}", "MAP_EXE"),
            EventRun($"VIDEOS/{slotId}/ENTRA")
        ];
    }

    private IEnumerable<string> ResolveVideoOut(OperationRequest request, BrainStormSnapshot snapshot)
    {
        var slotId = TryResolvePartySlotId(snapshot, request.CommandValue);
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return [];
        }

        return [EventRun($"VIDEOS/{slotId}/SALE")];
    }

    private IEnumerable<string> ResolveVideoInTodos(BrainStormSnapshot snapshot)
    {
        return snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .Select(p => ItemSetNoValue($"TICKER/FNC_Video{p.Codigo}", "MAP_EXE"))
            .ToList();
    }

    private IEnumerable<string> ResolveVideoOutTodos(BrainStormSnapshot snapshot)
    {
        var lines = snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .Select(p => EventRun($"VIDEOS/{ResolvePartySlotId(snapshot, p.Codigo)}/SALE"))
            .ToList();
        lines.Add(EventRun("Titular/Recuperar"));
        return lines;
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

        return
        [
            MapString("codigoSede", $"'{partyCode}'"),
            EventRun("Superfaldon/Sedes/DespliegaSede")
        ];
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
        var ordered = snapshot.Partidos
            .OrderBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = ordered.FindIndex(x => string.Equals(x.Codigo, partyCode, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            return (index + 1).ToString("D2", CultureInfo.InvariantCulture);
        }

        if (int.TryParse(partyCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed.ToString("D2", CultureInfo.InvariantCulture);
        }

        return "01";
    }

    private static string ResolveVideoMapSuffix(BrainStormSnapshot snapshot, string commandValue, string slotId)
    {
        var party = TryResolveParty(snapshot, commandValue);
        if (party is not null && !string.IsNullOrWhiteSpace(party.Codigo))
        {
            return party.Codigo.Trim();
        }

        return slotId;
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
            var hayCambio = state.UltimoSnapshotInicializado
                && !string.Equals(state.UltimoSnapshot, snapshotActual, StringComparison.Ordinal);

            state.UltimoSnapshot = snapshotActual;
            state.UltimoSnapshotInicializado = true;
            state.UltimoCambioDetectado = hayCambio;

            if (request.Module == GraphicModule.Carton)
            {
                if (hayCambio)
                {
                    lines.Add(EventRun("UltimoEscanoN/Cambio"));
                }
                else
                {
                    var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
                    lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
                    lines.Add(ItemSetNoValue("UltimoEscanoCSV", "MAP_LLSTRING_LOAD"));
                }

                return true;
            }

            if (hayCambio)
            {
                lines.Add(EventRun("UltimoEscanoSF/Cambio"));
            }
            else
            {
                lines.Add(EventRun("ReloadPartidos"));
            }

            return true;
        }
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
        }
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

    private sealed class SedesState
    {
        public string CurrentSlot { get; set; } = "01";
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
