using Zeroquery.Core.Orchestration;

namespace Zeroquery.Api.Query;

/// <summary>Request body for POST /api/instances/{id}/query (and its streaming sibling).</summary>
/// <param name="Prompt">The user's natural-language question.</param>
public sealed record QueryRequest(string Prompt);

/// <summary>
/// One SSE "progress" event payload emitted while POST /api/instances/{id}/query/stream is
/// running (doc/Plan.md Phase 5 — SSE streaming), so the frontend can show what the
/// tool-calling loop is doing instead of a static spinner.
/// </summary>
public sealed record OrchestrationProgressDto(string Stage, string? ToolName)
{
    public static OrchestrationProgressDto From(OrchestrationProgressEvent e) => new(e.Stage.ToString(), e.ToolName);
}

public sealed record UiSpecColumnDto(string Key, string Label)
{
    public static UiSpecColumnDto From(UiSpecColumn c) => new(c.Key, c.Label);
}

public sealed record UiSpecMetaDto(string SourceEntity, DateTimeOffset GeneratedAt)
{
    public static UiSpecMetaDto From(UiSpecMeta m) => new(m.SourceEntity, m.GeneratedAt);
}

public sealed record UiSpecFormFieldDto(
    string Name,
    string Label,
    object? CurrentValue,
    object? ProposedValue,
    bool IsPrimaryKey = false)
{
    public static UiSpecFormFieldDto From(UiSpecFormField f) =>
        new(f.Name, f.Label, f.CurrentValue, f.ProposedValue, f.IsPrimaryKey);
}

public sealed record UiSpecFormDto(
    string Operation,
    string Entity,
    Dictionary<string, object?>? PrimaryKey,
    IReadOnlyList<UiSpecFormFieldDto> Fields)
{
    public static UiSpecFormDto From(UiSpecForm f) =>
        new(f.Operation, f.Entity, f.PrimaryKey, f.Fields.Select(UiSpecFormFieldDto.From).ToList());
}

/// <summary>Response body for POST /api/instances/{id}/query — the UI Spec to render.</summary>
public sealed record UiSpecResponse(
    string Type,
    string Title,
    IReadOnlyList<UiSpecColumnDto> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string? ChartType,
    UiSpecMetaDto Meta,
    UiSpecFormDto? Form = null)
{
    public static UiSpecResponse From(UiSpec spec) => new(
        spec.Type.ToString(),
        spec.Title,
        spec.Columns.Select(UiSpecColumnDto.From).ToList(),
        spec.Rows,
        spec.ChartType?.ToString(),
        UiSpecMetaDto.From(spec.Meta),
        spec.Form is null ? null : UiSpecFormDto.From(spec.Form));
}
