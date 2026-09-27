using System.Text.Json.Serialization;

namespace Zeroquery.Core.Orchestration;

/// <summary>
/// The rendering contract handed back to the frontend after a query (doc/Plan.md Section 5).
/// The frontend renders exclusively from this fixed shape — no arbitrary code execution, no
/// LLM-authored markup. Kept intentionally small/generic for Phase 4; richer types (stat,
/// form) are introduced alongside the features that need them (Phase 8 for "form").
/// </summary>
/// <param name="Type">Which fixed component the frontend should render.</param>
/// <param name="Title">Human-readable title for the result.</param>
/// <param name="Columns">Column definitions for "table"/"chart" types.</param>
/// <param name="Rows">Row data — shape matches <see cref="Columns"/> keys.</param>
/// <param name="ChartType">Chart sub-type, only meaningful when <see cref="Type"/> is "chart".</param>
/// <param name="Meta">Provenance metadata (source entity, generation time) for debugging/trust.</param>
/// <param name="Form">Optional mutation proposal details when <see cref="Type"/> is "form" (Phase 8 CRUD).</param>
public sealed record UiSpec(
    UiSpecType Type,
    string Title,
    IReadOnlyList<UiSpecColumn> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    UiSpecChartType? ChartType,
    UiSpecMeta Meta,
    UiSpecForm? Form = null);

public sealed record UiSpecColumn(string Key, string Label);

public sealed record UiSpecMeta(string SourceEntity, DateTimeOffset GeneratedAt);

public sealed record UiSpecForm(
    string Operation,
    string Entity,
    Dictionary<string, object?>? PrimaryKey,
    IReadOnlyList<UiSpecFormField> Fields);

public sealed record UiSpecFormField(
    string Name,
    string Label,
    object? CurrentValue,
    object? ProposedValue,
    bool IsPrimaryKey = false);

[JsonConverter(typeof(JsonStringEnumConverter<UiSpecType>))]
public enum UiSpecType
{
    Table,
    Chart,
    Card,
    Stat,
    Form
}

[JsonConverter(typeof(JsonStringEnumConverter<UiSpecChartType>))]
public enum UiSpecChartType
{
    Bar,
    Line,
    Pie
}
