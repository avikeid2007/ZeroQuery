using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>
/// Converts Phase 1 picker output into a DAB (Data API builder) <c>dab-config.json</c> document.
///
/// Design notes (see doc/Plan.md Section 2.3 and Section 3):
/// - The raw connection string is never written into the config. DAB reads it from an
///   environment variable at startup via the <c>@env('NAME')</c> function.
/// - Column-level exposure is enforced via <c>permissions[].actions[].fields.include</c>
///   rather than omitting columns from <c>fields</c> — DAB's <c>fields</c> array is metadata
///   only (aliases/descriptions/primary-key flags), it does not restrict exposure by itself.
/// - Every entity defaults to read-only (<c>actions: ["read"]</c> for the <c>anonymous</c>
///   role) unless the caller opts specific tables into write actions — matching the
///   read-only-by-default posture required for a community tool with untrusted connections.
/// - Primary key columns are always included in the exposed field set, even if a caller's
///   selection omitted them, since DAB requires primary keys for pagination cursors and
///   row identification on update/delete.
/// - <c>runtime.mcp.enabled</c> is always set to <c>true</c> — the whole point of Phase 2/3
///   is spinning up a DAB SQL MCP server for the selected entities.
/// </summary>
public static class DabConfigGenerator
{
    private const string SchemaUrl = "https://github.com/Azure/data-api-builder/releases/latest/download/dab.draft.schema.json";

    // Config is written to disk and read by both DAB (a separate process) and humans debugging
    // it, so avoid over-escaping characters like apostrophes (the default encoder emits \u0027,
    // which is valid JSON but noisy — plain "'" is what @env('NAME') actually looks like
    // everywhere in DAB's own docs/examples).
    private static readonly JsonSerializerOptions PrettyPrint = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Builds the full dab-config.json document as pretty-printed JSON text.</summary>
    public static string Generate(ConfigGenerationRequest request)
    {
        if (request.Entities.Count == 0)
        {
            throw new InvalidOperationException("At least one entity must be selected to generate a DAB config.");
        }

        var entityNames = EntityNameResolver.Resolve(request.Entities);

        var root = new JsonObject
        {
            ["$schema"] = SchemaUrl,
            ["data-source"] = BuildDataSource(request),
            ["runtime"] = BuildRuntime(),
            ["entities"] = BuildEntities(request.Entities, entityNames)
        };

        return root.ToJsonString(PrettyPrint);
    }

    private static JsonObject BuildDataSource(ConfigGenerationRequest request) => new()
    {
        ["database-type"] = DabDatabaseType.From(request.Provider),
        ["connection-string"] = $"@env('{request.ConnectionStringEnvVarName}')"
    };

    private static JsonObject BuildRuntime() => new()
    {
        ["rest"] = new JsonObject { ["enabled"] = true },
        ["graphql"] = new JsonObject { ["enabled"] = true },
        ["mcp"] = new JsonObject { ["enabled"] = true }
    };

    private static JsonObject BuildEntities(IReadOnlyList<EntitySelectionRequest> entities, IReadOnlyList<string> entityNames)
    {
        var result = new JsonObject();

        for (var i = 0; i < entities.Count; i++)
        {
            result[entityNames[i]] = BuildEntity(entities[i]);
        }

        return result;
    }

    private static JsonObject BuildEntity(EntitySelectionRequest entity)
    {
        // Primary keys are always exposed regardless of the caller's include flag —
        // DAB needs them for row identification (pagination cursors, update/delete targeting).
        var exposedColumns = entity.Columns
            .Where(c => c.Include || c.IsPrimaryKey)
            .ToList();

        if (exposedColumns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity for table '{entity.QualifiedTableName}' has no included columns.");
        }

        // DAB hard-requires at least one primary-key field per entity and fails the ENTIRE
        // dab start process (not just this one entity) if none is configured — confirmed live:
        // "DataApiBuilderException: Primary key not configured on the given database object
        // <name>". This happens routinely for SQL views with no PK/unique constraint (e.g.
        // Northwinds' "Category Sales for 1997"), since views never carry PK metadata from
        // introspection. Fall back to treating every exposed column as a composite key in that
        // case — it's the same "no natural key" workaround DAB's own docs use for keyless
        // views, and it's far better than silently generating a config that crashes on start.
        var effectivePrimaryKeyNames = exposedColumns.Any(c => c.IsPrimaryKey)
            ? exposedColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : exposedColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var includeNames = exposedColumns.Select(c => c.Name).ToList();

        var entityJson = new JsonObject
        {
            ["source"] = new JsonObject
            {
                ["object"] = entity.QualifiedTableName,
                ["type"] = entity.IsView ? "view" : "table"
            },
            ["fields"] = BuildFields(exposedColumns, effectivePrimaryKeyNames),
            ["permissions"] = BuildPermissions(includeNames, entity.WriteActions)
        };

        if (!string.IsNullOrWhiteSpace(entity.Description))
        {
            entityJson["description"] = entity.Description;
        }

        return entityJson;
    }

    private static JsonArray BuildFields(IReadOnlyList<ColumnSelectionRequest> columns, IReadOnlySet<string> effectivePrimaryKeyNames)
    {
        var fields = new JsonArray();

        foreach (var column in columns)
        {
            var field = new JsonObject { ["name"] = column.Name };

            if (effectivePrimaryKeyNames.Contains(column.Name))
            {
                field["primary-key"] = true;
            }

            if (!string.IsNullOrWhiteSpace(column.Description))
            {
                field["description"] = column.Description;
            }

            fields.Add(field);
        }

        return fields;
    }

    private static JsonArray BuildPermissions(IReadOnlyList<string> includeNames, IReadOnlyList<string>? writeActions)
    {
        // Zeroquery ships a single "anonymous" role in Phase 1-3; per-role authentication is
        // out of scope until DAB's auth provider is wired up (tracked separately from this plan).
        var actions = new JsonArray
        {
            BuildAction("read", includeNames)
        };

        foreach (var action in NormalizeWriteActions(writeActions))
        {
            actions.Add(BuildAction(action, includeNames));
        }

        return new JsonArray
        {
            new JsonObject
            {
                ["role"] = "anonymous",
                ["actions"] = actions
            }
        };
    }

    private static JsonObject BuildAction(string action, IReadOnlyList<string> includeNames) => new()
    {
        ["action"] = action,
        ["fields"] = new JsonObject
        {
            ["include"] = new JsonArray(includeNames.Select(n => (JsonNode)n).ToArray())
        }
    };

    private static IEnumerable<string> NormalizeWriteActions(IReadOnlyList<string>? writeActions)
    {
        if (writeActions is null)
        {
            yield break;
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create", "update", "delete" };

        foreach (var action in writeActions.Select(a => a.ToLowerInvariant()).Distinct())
        {
            if (!allowed.Contains(action))
            {
                throw new InvalidOperationException(
                    $"Unsupported write action '{action}'. Allowed values: create, update, delete.");
            }

            yield return action;
        }
    }
}
