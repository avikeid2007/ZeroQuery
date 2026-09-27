using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>
/// Validates a generated dab-config.json document against DAB's own published JSON schema
/// (embedded at build time — see ConfigGeneration/Resources/dab.draft.schema.json), so
/// Zeroquery fails fast with a clear error instead of handing DAB a config that crashes its
/// subprocess at startup (see doc/Plan.md Section 2.3).
/// </summary>
public sealed class DabConfigValidator
{
    private const string EmbeddedResourceName = "Zeroquery.Core.ConfigGeneration.Resources.dab.draft.schema.json";

    // JsonSchema.Net registers a parsed schema globally by its $id; parsing the same embedded
    // resource more than once per process throws "Overwriting registered schemas is not
    // permitted." A process-wide Lazy<T> ensures it's only ever built once, regardless of how
    // many DabConfigValidator instances are constructed (e.g. once per test, or once as a
    // singleton in DI).
    private static readonly Lazy<JsonSchema> SharedSchema = new(LoadEmbeddedSchema);

    private readonly JsonSchema _schema;

    public DabConfigValidator()
    {
        _schema = SharedSchema.Value;
    }

    /// <summary>
    /// Validates <paramref name="configJson"/> against the DAB schema. Returns an empty error
    /// list when valid.
    /// </summary>
    public IReadOnlyList<string> Validate(string configJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configJson);
        }
        catch (JsonException ex)
        {
            return new[] { $"Generated config is not valid JSON: {ex.Message}" };
        }

        using (document)
        {
            var result = _schema.Evaluate(document.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });

            if (result.IsValid)
            {
                return Array.Empty<string>();
            }

            return (result.Details ?? Enumerable.Empty<EvaluationResults>())
                .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
                .Distinct()
                .ToList();
        }
    }

    private static JsonSchema LoadEmbeddedSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded DAB schema resource '{EmbeddedResourceName}' was not found. " +
                "Verify the EmbeddedResource entry in Zeroquery.Core.csproj.");

        using var reader = new StreamReader(stream);
        var schemaText = reader.ReadToEnd();

        return JsonSchema.FromText(schemaText);
    }
}
