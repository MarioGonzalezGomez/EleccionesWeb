using System.Globalization;
using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed class DefaultSignalComposer : ISignalComposer
{
    private const double PactosTotalWidth = 1748d;

    private const int UltimoMaxFichaWidth = 1756;
    private const int UltimoPosInicialIzq = 90;
    private const int UltimoPosInicialDch = 1844;
    private const int UltimoAnchoPequenoThreshold = 126;

    private static readonly string[] UltimoBarrasIzq = ["Barra_Izq", "Barra_Izq1", "Barra_Izq2", "Barra_Izq3"];
    private static readonly string[] UltimoBarrasDch = ["Barra_Dch", "Barra_Dch1", "Barra_Dch2", "Barra_Dch3"];

    private readonly object _stateLock = new();
    private readonly Dictionary<GraphicModule, PactosState> _pactosStates = new();
    private readonly Dictionary<GraphicModule, SedesState> _sedesStates = new();
    private readonly Dictionary<GraphicModule, UltimoEscanoState> _ultimoEscanoStates = new();

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
            ClearModuleState(request.Module);
            lines.Add(EventRun("RESET"));
            return string.Join("\n", lines);
        }

        if (request.Command != GraphicCommand.None)
        {
            lines.AddRange(ResolveCommandLines(request, snapshot));
            return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        var codeFile = request.Oficiales ? "Oficial_Codigo" : "Sondeo_Codigo";
        var eventPath = ResolveEventPath(scene, request.Action);

        if (NeedsCodeReload(scene, request.Action))
        {
            lines.Add(ItemSetNoValue(codeFile, "MAP_LLSTRING_LOAD"));
        }

        if (NeedsUltimoCsvReload(scene, request.Action))
        {
            lines.Add(ItemSetNoValue("UltimoEscanoCSV", "MAP_LLSTRING_LOAD"));
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

            GraphicCommand.SedesEntra => request.Module == GraphicModule.Superfaldon
                ? [EventRun("SEDES/ENTRA")]
                : ResolveFaldonSedesEntra(request, snapshot),
            GraphicCommand.SedesEncadena => request.Module == GraphicModule.Superfaldon
                ? [EventRun("SEDES/ENCADENA")]
                : ResolveFaldonSedesEncadena(request, snapshot),
            GraphicCommand.SedesSale => request.Module == GraphicModule.Superfaldon
                ? [EventRun("SEDES/SALE")]
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

            _ => []
        };
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
        lock (_stateLock)
        {
            _pactosStates.Remove(request.Module);
        }

        return
        [
            EventRun("Pactometro/Sale"),
            EventRun("Pactometro/reinicioPactometroIzq"),
            EventRun("Pactometro/reinicioPactometroDer")
        ];
    }

    private IEnumerable<string> ResolvePactosReinicio(OperationRequest request)
    {
        lock (_stateLock)
        {
            _pactosStates.Remove(request.Module);
        }

        return
        [
            EventRun("Pactometro/reinicioPactometroIzq"),
            EventRun("Pactometro/reinicioPactometroDer")
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

            if (!list.Any(x => string.Equals(x, partido.Codigo, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(partido.Codigo);
            }

            RecalculatePactosState(state, snapshot, request.Oficiales);

            var seatCount = izquierda ? state.EscaniosIzquierda : state.EscaniosDerecha;
            var width = izquierda ? state.AnchoIzquierda : state.AnchoDerecha;
            var isFirst = list.Count == 1;
            var logoId = ResolvePartySlotId(snapshot, partido.Codigo);
            var logoSlot = list.Count.ToString("D2", CultureInfo.InvariantCulture);

            var lines = new List<string>();

            if (izquierda)
            {
                lines.Add(ItemSetNumeric("Pactometro_IzqVALOR", "MAP_INT_PAR", seatCount));
                lines.Add(ItemGoNumeric("BarraIzquierdas", "PRIM_RECGLO_LEN[0]", width, 0.3, 0));
            }
            else
            {
                lines.Add(ItemSetNumeric("Pactometro_DerVALOR", "MAP_INT_PAR", seatCount));
                lines.Add(ItemGoNumeric("BarraDerechas", "PRIM_RECGLO_LEN[0]", width, 0.3, 0));
            }

            if (snapshot.MayoriaAbsoluta > 0 && seatCount >= snapshot.MayoriaAbsoluta)
            {
                lines.Add(EventRun("Pactometro/PasaMayoria"));
            }

            if (izquierda)
            {
                lines.Add(ItemSetString($"Graficos/Pactometro/Izq/LogosIzq/Logo{logoSlot}", "OBJ_OVERMAT", $"Logos/Logo{logoId}"));
                if (isFirst)
                {
                    lines.Add(ItemSetNoValue("Graficos/Pactometro/Izq/LogosIzq", "OBJ_GRID_JUMP_TO_END"));
                    lines.Add(EventRun("Pactometro/lanzaPactometroIzq"));
                    lines.Add(ItemSetString("Graficos/Pactometro/Izq/BarraIzquierdas", "OBJ_OVERMAT", logoId));
                }
                else
                {
                    lines.Add(ItemSetNoValue("Graficos/Pactometro/Izq/LogosIzq", "OBJ_GRID_JUMP_NEXT"));
                }
            }
            else
            {
                lines.Add(ItemSetString($"Graficos/Pactometro/Der/LogosDer/Logo{logoSlot}", "OBJ_OVERMAT", $"Logos/Logo{logoId}"));
                if (isFirst)
                {
                    lines.Add(ItemSetNoValue("Graficos/Pactometro/Der/LogosDer", "OBJ_GRID_JUMP_TO_END"));
                    lines.Add(EventRun("Pactometro/lanzaPactometroDer"));
                    lines.Add(ItemSetString("Graficos/Pactometro/Der/BarraDerechas", "OBJ_OVERMAT", logoId));
                }
                else
                {
                    lines.Add(ItemSetNoValue("Graficos/Pactometro/Der/LogosDer", "OBJ_GRID_JUMP_NEXT"));
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

            if (list.Count == 0)
            {
                return [];
            }

            list.RemoveAt(list.Count - 1);
            RecalculatePactosState(state, snapshot, request.Oficiales);

            if (izquierda)
            {
                return
                [
                    ItemSetNumeric("Pactometro_IzqVALOR", "MAP_INT_PAR", state.EscaniosIzquierda),
                    ItemGoNumeric("BarraIzquierdas", "PRIM_RECGLO_LEN[0]", state.AnchoIzquierda, 0.3, 0),
                    ItemSetNoValue("Graficos/Pactometro/Izq/LogosIzq", "OBJ_GRID_JUMP_PREV")
                ];
            }

            return
            [
                ItemSetNumeric("Pactometro_DerVALOR", "MAP_INT_PAR", state.EscaniosDerecha),
                ItemGoNumeric("BarraDerechas", "PRIM_RECGLO_LEN[0]", state.AnchoDerecha, 0.3, 0),
                ItemSetNoValue("Graficos/Pactometro/Der/LogosDer", "OBJ_GRID_JUMP_PREV")
            ];
        }
    }

    private IEnumerable<string> ResolveUltimoLimpiaPartidos(OperationRequest request)
    {
        lock (_stateLock)
        {
            _ultimoEscanoStates.Remove(request.Module);
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
            if (sideIndex >= 4)
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
        state.EscaniosIzquierda = 0;
        state.EscaniosDerecha = 0;
        state.AnchoIzquierda = 0;
        state.AnchoDerecha = 0;

        var totalEscanios = Math.Max(1, snapshot.EscaniosTotales);
        var partidosPorCodigo = snapshot.Partidos.ToDictionary(x => x.Codigo, StringComparer.OrdinalIgnoreCase);

        foreach (var codigo in state.PartidosIzquierda)
        {
            if (!partidosPorCodigo.TryGetValue(codigo, out var partido))
            {
                continue;
            }

            var escanios = Math.Max(0, ResolveEscanos(partido, oficiales));
            state.EscaniosIzquierda += escanios;
            state.AnchoIzquierda += escanios * PactosTotalWidth / totalEscanios;
        }

        foreach (var codigo in state.PartidosDerecha)
        {
            if (!partidosPorCodigo.TryGetValue(codigo, out var partido))
            {
                continue;
            }

            var escanios = Math.Max(0, ResolveEscanos(partido, oficiales));
            state.EscaniosDerecha += escanios;
            state.AnchoDerecha += escanios * PactosTotalWidth / totalEscanios;
        }
    }

    private static string? TryResolvePartySlotId(BrainStormSnapshot snapshot, string commandValue)
    {
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

    private static int ResolveEscanos(PartidoSnapshot partido, bool oficiales)
    {
        return oficiales ? partido.Escanios : partido.EscaniosHastaSondeo;
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

        public int EscaniosIzquierda { get; set; }
        public int EscaniosDerecha { get; set; }
        public double AnchoIzquierda { get; set; }
        public double AnchoDerecha { get; set; }
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
    }

    private sealed record UltimoEntry(string Codigo, string Siglas, int Escanios, int Ancho);
}
