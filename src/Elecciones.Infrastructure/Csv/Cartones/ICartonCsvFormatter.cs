using System.Globalization;
using System.Text;
using Elecciones.Application.Models;
using Elecciones.Infrastructure.Options;

namespace Elecciones.Infrastructure.Csv.Cartones;

public static class CartonCsvRoles
{
    public const string Unico = "Unico";
    public const string Partidos = "Partidos";
    public const string Provincia = "Provincia";
}

public sealed record CartonCsvDescriptor(
    string LogicalRole,
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows
);

public interface ICartonCsvFormatter
{
    CartonKind Kind { get; }

    IReadOnlyList<CartonCsvDescriptor> BuildDocuments(
        BrainStormSnapshot snapshot,
        SnapshotQuery query,
        CartonCsvFilesOptions fileOptions);
}

internal static class CartonCsvFormattingHelpers
{
    public static string Serialize(CartonCsvDescriptor descriptor, string delimiter = ";")
    {
        var sep = string.IsNullOrEmpty(delimiter) ? ";" : delimiter;
        var sb = new StringBuilder();

        if (descriptor.Headers.Count > 0)
        {
            sb.AppendLine(string.Join(sep, descriptor.Headers.Select( cell => EscapeCell(cell, sep))));
        }

        foreach (var row in descriptor.Rows)
        {
            sb.AppendLine(string.Join(sep, row.Select(cell => EscapeCell(cell, sep))));
        }

        return sb.ToString();
    }

    public static string EnsureCsvFileName(string? configuredName, string fallbackFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredName)
            ? fallbackFileName
            : configuredName.Trim();

        if (!candidate.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            candidate += ".csv";
        }

        return candidate;
    }

    public static IReadOnlyList<PartidoSnapshot> OrderPartiesForDisplay(BrainStormSnapshot snapshot)
    {
        var oficiales = snapshot.Oficiales;
        return snapshot.Partidos
            .Where(p => !string.IsNullOrWhiteSpace(p.Codigo))
            .OrderBy(p => string.Equals(p.Codigo, "99999", StringComparison.Ordinal))
            .ThenByDescending(p => oficiales ? p.Escanios : p.EscaniosHastaSondeo)
            .ThenByDescending(p => oficiales ? p.Escanios : p.EscaniosDesdeSondeo)
            .ThenByDescending(p => p.PorcentajeVoto)
            .ThenByDescending(p => p.Votos)
            .ToList();
    }

    public static int ResolveDisplaySeats(PartidoSnapshot partido, bool oficiales)
        => oficiales ? partido.Escanios : partido.EscaniosHastaSondeo;

    public static (int AbsoluteDiff, int SignedDiff, string Trend) ComputeSeatDelta(PartidoSnapshot partido, bool oficiales)
    {
        var current = ResolveDisplaySeats(partido, oficiales);
        if (partido.EscaniosHistoricos == 0)
        {
            return (current, current, "*");
        }

        var signed = current - partido.EscaniosHistoricos;
        var trend = signed > 0 ? "+" : signed < 0 ? "-" : "=";
        return (Math.Abs(signed), signed, trend);
    }

    public static (int AbsoluteDiff, int SignedDiff, string Trend) ComputeVoteDelta(PartidoSnapshot partido)
    {
        if (partido.VotantesHistoricos == 0)
        {
            return (partido.Votos, partido.Votos, "*");
        }

        var signed = partido.Votos - partido.VotantesHistoricos;
        var trend = signed > 0 ? "+" : signed < 0 ? "-" : "=";
        return (Math.Abs(signed), signed, trend);
    }

    public static string FormatDecimal(double value, int decimals = 2)
        => value.ToString($"F{decimals}", CultureInfo.InvariantCulture);

    public static string FormatTruncatedTenths(double value)
        => (Math.Truncate(value * 10d) / 10d).ToString(CultureInfo.InvariantCulture);

    public static string FormatInt(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCell(string? raw, string delimiter)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var sanitized = raw
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (sanitized.Contains(delimiter, StringComparison.Ordinal) || sanitized.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{sanitized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return sanitized;
    }
}
