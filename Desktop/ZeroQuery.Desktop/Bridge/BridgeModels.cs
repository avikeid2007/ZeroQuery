using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;

namespace ZeroQuery.Desktop.Bridge;

public sealed class IpcRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

public sealed class IpcResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public static IpcResponse Ok(string id, string channel, object? data) => new()
    {
        Id = id,
        Channel = channel,
        Success = true,
        Data = data
    };

    public static IpcResponse Fail(string id, string channel, string error) => new()
    {
        Id = id,
        Channel = channel,
        Success = false,
        Error = error
    };
}

public sealed class IpcStreamMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// DTOs for Introspection
public sealed record IntrospectRequestDto(DatabaseProvider Provider, string ConnectionString);

public sealed record ColumnDto(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, int? MaxLength)
{
    public static ColumnDto From(ColumnInfo c) => new(c.Name, c.DataType, c.IsNullable, c.IsPrimaryKey, c.MaxLength);
}

public sealed record TableDto(string Schema, string Name, bool IsView, IReadOnlyList<ColumnDto> Columns)
{
    public static TableDto From(TableInfo t) => new(t.Schema, t.Name, t.IsView, t.Columns.Select(ColumnDto.From).ToList());
}

public sealed record ForeignKeyDto(string ConstraintName, string FromTable, string FromColumn, string ToTable, string ToColumn)
{
    public static ForeignKeyDto From(ForeignKeyInfo f) => new(f.ConstraintName, f.FromTable, f.FromColumn, f.ToTable, f.ToColumn);
}

public sealed record IntrospectResponseDto(
    DatabaseProvider Provider,
    IReadOnlyList<TableDto> Tables,
    IReadOnlyList<ForeignKeyDto> ForeignKeys)
{
    public static IntrospectResponseDto From(SchemaInfo schema) => new(
        schema.Provider,
        schema.Tables.Select(TableDto.From).ToList(),
        schema.ForeignKeys.Select(ForeignKeyDto.From).ToList());
}

// DTOs for Config Generation
public sealed record ColumnSelectionDto(string Name, bool Include, bool IsPrimaryKey, string? Description);

public sealed record EntitySelectionDto(
    string Schema,
    string TableName,
    bool IsView,
    IReadOnlyList<ColumnSelectionDto> Columns,
    string? EntityName,
    string? Description,
    IReadOnlyList<string>? WriteActions);

public sealed record GenerateConfigRequestDto(
    DatabaseProvider Provider,
    string ConnectionStringEnvVarName,
    IReadOnlyList<EntitySelectionDto> Entities,
    bool? EnableRest = true,
    bool? EnableGraphql = true);

public sealed record GenerateConfigResponseDto(
    string ConfigJson,
    bool IsValid,
    IReadOnlyList<string> ValidationErrors);

// DTOs for Instances
public sealed record StartInstanceRequestDto(
    string ConfigJson,
    string ConnectionStringEnvVarName,
    string ConnectionString);

public sealed record InstanceStatusResponseDto(
    string Id,
    string Status,
    int Port,
    string BaseUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    string? LastError,
    string? RestUrl = null,
    string? GraphqlUrl = null,
    string? HealthUrl = null)
{
    public static InstanceStatusResponseDto From(DabInstance instance, DabInstanceStatus status) => new(
        instance.Id,
        status.ToString(),
        instance.Port,
        instance.BaseUrl,
        instance.StartedAt,
        instance.LastUsedAt,
        instance.LastError,
        instance.RestUrl,
        instance.GraphqlUrl,
        instance.HealthUrl);
}

// DTOs for Queries
public sealed record QueryRequestDto(string Prompt);

// DTOs for Settings
public sealed record LlmSettingsResponseDto(
    string ModelId,
    bool IsApiKeyConfigured,
    string? ApiKeyMasked,
    string? SystemPrompt,
    string? DefaultSystemPrompt,
    string? BaseUrl,
    string? DefaultBaseUrl)
{
    public static LlmSettingsResponseDto From(LlmSettingsView view) => new(
        view.ModelId,
        view.IsApiKeyConfigured,
        view.ApiKeyMasked,
        view.SystemPrompt,
        view.DefaultSystemPrompt,
        view.BaseUrl,
        view.DefaultBaseUrl);
}

public sealed record UpdateLlmSettingsRequestDto(string? ApiKey, string? ModelId, string? SystemPrompt, string? BaseUrl);

// DTOs for Saved Connections
public sealed record SaveConnectionRequestDto(
    string Name,
    DatabaseProvider Provider,
    string ConnectionString,
    string ConfigJson);
