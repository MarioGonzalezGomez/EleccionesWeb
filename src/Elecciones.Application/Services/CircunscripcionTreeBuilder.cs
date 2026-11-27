using Elecciones.Application.Models;

namespace Elecciones.Application.Services;

public sealed record CircunscripcionTreeNode(
    string Codigo,
    string Nombre,
    int Level,
    IReadOnlyList<CircunscripcionTreeNode> Children)
{
    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// Construye un árbol jerárquico (General -> Comunidades -> Provincias -> Ciudades/Municipios)
/// a partir de los códigos estándar de 7 dígitos <c>CCPPMMM</c> de circunscripciones electorales.
/// </summary>
public static class CircunscripcionTreeBuilder
{
    public static IReadOnlyList<CircunscripcionTreeNode> Build(IReadOnlyList<CircunscripcionSummary> flatList)
    {
        if (flatList is null || flatList.Count == 0)
        {
            return [];
        }

        var uniqueItems = flatList
            .Where(x => !string.IsNullOrWhiteSpace(x.Codigo))
            .GroupBy(x => x.Codigo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CircunscripcionSummary(g.Key, g.First().Nombre?.Trim() ?? g.Key))
            .ToList();

        var generals = new List<CircunscripcionSummary>();
        var communitiesByCc = new Dictionary<string, CircunscripcionSummary>(StringComparer.OrdinalIgnoreCase);
        var provincesByCcPp = new Dictionary<string, CircunscripcionSummary>(StringComparer.OrdinalIgnoreCase);
        var cities = new List<(string Cc, string Pp, CircunscripcionSummary Item)>();
        var uncategorized = new List<CircunscripcionSummary>();

        foreach (var item in uniqueItems)
        {
            if (!TrySplitSevenDigitCode(item.Codigo, out var cc, out var pp, out var mmm))
            {
                uncategorized.Add(item);
                continue;
            }

            if (string.Equals(cc, "99", StringComparison.Ordinal))
            {
                generals.Add(item);
            }
            else if (string.Equals(pp, "00", StringComparison.Ordinal) && string.Equals(mmm, "000", StringComparison.Ordinal))
            {
                communitiesByCc[cc] = item;
            }
            else if (!string.Equals(pp, "00", StringComparison.Ordinal) && string.Equals(mmm, "000", StringComparison.Ordinal))
            {
                provincesByCcPp[$"{cc}{pp}"] = item;
            }
            else
            {
                cities.Add((cc, pp, item));
            }
        }

        // Agrupar ciudades bajo su provincia (CCPP) o directamente bajo su comunidad (CC) si no existe fila de provincia
        var citiesByProvinceKey = new Dictionary<string, List<CircunscripcionSummary>>(StringComparer.OrdinalIgnoreCase);
        var directCitiesByCc = new Dictionary<string, List<CircunscripcionSummary>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (cc, pp, city) in cities)
        {
            var provKey = $"{cc}{pp}";
            if (!string.Equals(pp, "00", StringComparison.Ordinal) && provincesByCcPp.ContainsKey(provKey))
            {
                if (!citiesByProvinceKey.TryGetValue(provKey, out var provCities))
                {
                    provCities = [];
                    citiesByProvinceKey[provKey] = provCities;
                }
                provCities.Add(city);
            }
            else if (communitiesByCc.ContainsKey(cc))
            {
                if (!directCitiesByCc.TryGetValue(cc, out var commCities))
                {
                    commCities = [];
                    directCitiesByCc[cc] = commCities;
                }
                commCities.Add(city);
            }
            else
            {
                uncategorized.Add(city);
            }
        }

        // Construir nodos de provincia y agruparlos por comunidad (CC)
        var provinceNodesByCc = new Dictionary<string, List<CircunscripcionTreeNode>>(StringComparer.OrdinalIgnoreCase);
        var orphanProvinceNodes = new List<CircunscripcionTreeNode>();

        foreach (var (provKey, provItem) in provincesByCcPp)
        {
            var cc = provKey[..2];
            var childCityNodes = citiesByProvinceKey.TryGetValue(provKey, out var provCities)
                ? provCities
                    .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => new CircunscripcionTreeNode(c.Codigo, c.Nombre, Level: 2, Children: []))
                    .ToList()
                : [];

            if (communitiesByCc.ContainsKey(cc))
            {
                if (!provinceNodesByCc.TryGetValue(cc, out var list))
                {
                    list = [];
                    provinceNodesByCc[cc] = list;
                }
                list.Add(new CircunscripcionTreeNode(provItem.Codigo, provItem.Nombre, Level: 1, childCityNodes));
            }
            else
            {
                // Si una BD provincial o demo no trae fila CC00000, la provincia sube a nivel raíz para no ocultarse
                orphanProvinceNodes.Add(new CircunscripcionTreeNode(provItem.Codigo, provItem.Nombre, Level: 0, childCityNodes));
            }
        }

        var result = new List<CircunscripcionTreeNode>();

        // 1. Total General (9900000 España) siempre primero
        foreach (var gen in generals.OrderBy(g => g.Codigo, StringComparer.Ordinal))
        {
            result.Add(new CircunscripcionTreeNode(gen.Codigo, gen.Nombre, Level: 0, Children: []));
        }

        // 2. Comunidades Autónomas ordenadas alfabéticamente con sus provincias/ciudades hijas
        var communityNodes = new List<CircunscripcionTreeNode>();
        foreach (var (cc, commItem) in communitiesByCc)
        {
            var children = new List<CircunscripcionTreeNode>();
            if (provinceNodesByCc.TryGetValue(cc, out var provNodes))
            {
                children.AddRange(provNodes.OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase));
            }

            if (directCitiesByCc.TryGetValue(cc, out var commCities))
            {
                children.AddRange(commCities
                    .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                    .Select(c => new CircunscripcionTreeNode(c.Codigo, c.Nombre, Level: 1, Children: [])));
            }

            communityNodes.Add(new CircunscripcionTreeNode(commItem.Codigo, commItem.Nombre, Level: 0, children));
        }

        communityNodes.AddRange(orphanProvinceNodes);
        communityNodes.AddRange(uncategorized.Select(u => new CircunscripcionTreeNode(u.Codigo, u.Nombre, Level: 0, Children: [])));

        result.AddRange(communityNodes.OrderBy(n => n.Nombre, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }

    public static IReadOnlyList<string> FindAncestorCodes(
        IReadOnlyList<CircunscripcionTreeNode> nodes,
        string? selectedCode)
    {
        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            return [];
        }

        var target = selectedCode.Trim();
        var path = new List<string>();
        foreach (var node in nodes)
        {
            if (FindAncestorsRecursive(node, target, path))
            {
                return path;
            }
        }

        return [];
    }

    private static bool FindAncestorsRecursive(
        CircunscripcionTreeNode current,
        string target,
        List<string> ancestors)
    {
        if (string.Equals(current.Codigo, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            ancestors.Add(current.Codigo);
            if (FindAncestorsRecursive(child, target, ancestors))
            {
                return true;
            }
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        return false;
    }

    private static bool TrySplitSevenDigitCode(string raw, out string cc, out string pp, out string mmm)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 7 && trimmed.All(char.IsAsciiDigit))
        {
            cc = trimmed[..2];
            pp = trimmed[2..4];
            mmm = trimmed[4..7];
            return true;
        }

        cc = string.Empty;
        pp = string.Empty;
        mmm = string.Empty;
        return false;
    }
}
