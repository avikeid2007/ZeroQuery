using System.Text;
using System.Text.RegularExpressions;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>
/// Resolves a valid, unique DAB entity name for each selected table/view.
/// DAB entity names become GraphQL type names, so they must be sanitized to valid GraphQL
/// identifiers ([A-Za-z_][A-Za-z0-9_]*) and de-duplicated when two schemas expose tables with
/// the same base name (e.g. "sales.Orders" and "archive.Orders").
/// </summary>
internal static class EntityNameResolver
{
    private static readonly Regex InvalidChars = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    /// <summary>
    /// Computes entity names for every request, in order. Explicit <c>EntityName</c> overrides
    /// are honored (still sanitized) but are the caller's responsibility to keep unique.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IReadOnlyList<EntitySelectionRequest> entities)
    {
        // First pass: sanitized base name candidates.
        var baseNames = entities
            .Select(e => Sanitize(e.EntityName ?? e.TableName))
            .ToList();

        // Count how many times each base name appears (case-insensitive) to know which need
        // schema-prefixing to stay unique.
        var counts = baseNames
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<string>(entities.Count);

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var baseName = baseNames[i];

            var candidate = counts[baseName] > 1 && !string.IsNullOrEmpty(entity.Schema)
                ? Sanitize($"{entity.Schema}_{baseName}")
                : baseName;

            // Fall back to a numeric suffix if it's still colliding (e.g. explicit overrides).
            var finalName = candidate;
            var suffix = 2;
            while (!used.Add(finalName))
            {
                finalName = $"{candidate}_{suffix}";
                suffix++;
            }

            resolved.Add(finalName);
        }

        return resolved;
    }

    private static string Sanitize(string name)
    {
        var cleaned = InvalidChars.Replace(name, "_");
        if (cleaned.Length == 0 || char.IsDigit(cleaned[0]))
        {
            cleaned = "_" + cleaned;
        }
        return cleaned;
    }
}
