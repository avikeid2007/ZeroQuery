using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Zeroquery.Core.Audit;
using Zeroquery.Core.Mcp;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Core.Mutations;

public sealed class MutationService : IMutationService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DabProcessManager _processManager;
    private readonly IMcpClientFactory _mcpClientFactory;
    private readonly IWriteAuditStore _auditStore;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MutationService> _logger;

    public MutationService(
        DabProcessManager processManager,
        IMcpClientFactory mcpClientFactory,
        IWriteAuditStore auditStore,
        HttpClient? httpClient,
        ILogger<MutationService> logger)
    {
        _processManager = processManager;
        _mcpClientFactory = mcpClientFactory;
        _auditStore = auditStore;
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public async Task<MutationResult> ExecuteAsync(
        string instanceId,
        ExecuteMutationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(command);

        var instance = _processManager.GetInstance(instanceId);
        if (instance is null)
        {
            return new MutationResult(false, $"Instance '{instanceId}' not found.");
        }

        var status = _processManager.RefreshStatus(instance);
        if (status is not (DabInstanceStatus.Running or DabInstanceStatus.Idle))
        {
            return new MutationResult(false, $"Instance '{instanceId}' is not in a ready state (current: {status}).");
        }

        var operation = command.Operation.Trim().ToLowerInvariant();
        if (operation is not ("create" or "update" or "delete"))
        {
            return new MutationResult(false, $"Unsupported operation '{command.Operation}'. Allowed: create, update, delete.");
        }

        // 1. Permission check against dab-config.json
        var permissionError = CheckEntityPermission(instance.ConfigPath, command.Entity, operation);
        if (permissionError is not null)
        {
            await RecordAuditAsync(instanceId, command, success: false, errorMessage: permissionError, cancellationToken).ConfigureAwait(false);
            return new MutationResult(false, permissionError, IsForbidden: true);
        }

        // 2. Execute via MCP or REST fallback
        MutationResult executionResult;
        try
        {
            executionResult = await ExecuteOnDabAsync(instance, command, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute mutation {Operation} on entity {Entity} for instance {InstanceId}",
                operation, command.Entity, instanceId);
            executionResult = new MutationResult(false, $"Execution failed: {ex.Message}");
        }

        // 3. Record in audit trail
        await RecordAuditAsync(
            instanceId,
            command,
            executionResult.Success,
            executionResult.Success ? null : executionResult.Message,
            cancellationToken).ConfigureAwait(false);

        // 4. Touch instance to reset idle timer
        _processManager.TouchInstance(instanceId);

        return executionResult;
    }

    private static string? CheckEntityPermission(string configPath, string entityName, string operation)
    {
        if (!File.Exists(configPath))
        {
            return $"DAB configuration file not found at '{configPath}'.";
        }

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("entities", out var entitiesElement))
            {
                return "Malformed DAB config: 'entities' section missing.";
            }

            // Find entity case-insensitively
            JsonProperty? entityProp = null;
            foreach (var prop in entitiesElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    entityProp = prop;
                    break;
                }
            }

            if (entityProp is null)
            {
                return $"Entity '{entityName}' is not defined in this instance's DAB configuration.";
            }

            if (!entityProp.Value.Value.TryGetProperty("permissions", out var permissionsElement))
            {
                return $"Entity '{entityName}' has no permissions defined.";
            }

            var permitted = false;
            foreach (var perm in permissionsElement.EnumerateArray())
            {
                if (perm.TryGetProperty("actions", out var actionsElement))
                {
                    foreach (var act in actionsElement.EnumerateArray())
                    {
                        var actionName = act.ValueKind == JsonValueKind.Object && act.TryGetProperty("action", out var a)
                            ? a.GetString()
                            : act.GetString();

                        if (string.Equals(actionName, operation, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(actionName, "*", StringComparison.OrdinalIgnoreCase))
                        {
                            permitted = true;
                            break;
                        }
                    }
                }
                if (permitted) break;
            }

            if (!permitted)
            {
                return $"Write action '{operation}' is not permitted on entity '{entityName}'. This table was configured as read-only or does not grant '{operation}' permission.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Error validating permissions for '{entityName}': {ex.Message}";
        }
    }

    private async Task<MutationResult> ExecuteOnDabAsync(
        DabInstance instance,
        ExecuteMutationCommand command,
        string operation,
        CancellationToken cancellationToken)
    {
        // Try MCP tools first
        try
        {
            var mcpClient = _mcpClientFactory.Create(instance.BaseUrl);
            await using var _ = mcpClient;
            await mcpClient.InitializeAsync("zeroquery-mutation-runner", "0.1.0", cancellationToken).ConfigureAwait(false);

            var tools = await mcpClient.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            var matchingTool = FindMatchingMcpTool(tools, command.Entity, operation);

            if (matchingTool is not null)
            {
                var args = new JsonObject
                {
                    ["entity"] = command.Entity
                };

                if (command.PrimaryKey is { Count: > 0 })
                {
                    var pkObj = new JsonObject();
                    foreach (var (k, v) in command.PrimaryKey)
                    {
                        pkObj[k] = v switch
                        {
                            null => null,
                            string s => s,
                            double d => d,
                            int i => i,
                            long l => l,
                            bool b => b,
                            _ => v.ToString()
                        };
                    }
                    args["key"] = pkObj;
                }

                if (operation is "create" or "update" && command.Values is { Count: > 0 })
                {
                    var fieldsObj = new JsonObject();
                    foreach (var (k, v) in command.Values)
                    {
                        fieldsObj[k] = v switch
                        {
                            null => null,
                            string s => s,
                            double d => d,
                            int i => i,
                            long l => l,
                            bool b => b,
                            _ => v.ToString()
                        };
                    }
                    args["fields"] = fieldsObj;
                }

                var toolResult = await mcpClient.CallToolAsync(matchingTool.Name, args, cancellationToken).ConfigureAwait(false);
                if (toolResult.IsError)
                {
                    return new MutationResult(false, toolResult.Text);
                }

                Dictionary<string, object?>? record = null;
                try
                {
                    record = JsonSerializer.Deserialize<Dictionary<string, object?>>(toolResult.Text, JsonOpts);
                }
                catch
                {
                    // toolResult text may be plain string or acknowledgment
                }

                return new MutationResult(true, "Operation executed successfully via MCP.", record);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MCP tool dispatch unavailable, attempting DAB REST fallback for entity {Entity}", command.Entity);
        }

        // Direct DAB REST fallback
        return await ExecuteViaDabRestAsync(instance, command, operation, cancellationToken).ConfigureAwait(false);
    }

    private static McpTool? FindMatchingMcpTool(IReadOnlyList<McpTool> tools, string entity, string operation)
    {
        var targetNames = operation switch
        {
            "create" => new[] { "create_record", "insert_record", $"{entity}_create", $"create_{entity}" },
            "update" => new[] { "update_record", $"{entity}_update", $"update_{entity}" },
            "delete" => new[] { "delete_record", $"{entity}_delete", $"delete_{entity}" },
            _ => Array.Empty<string>()
        };

        return tools.FirstOrDefault(t => targetNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<MutationResult> ExecuteViaDabRestAsync(
        DabInstance instance,
        ExecuteMutationCommand command,
        string operation,
        CancellationToken cancellationToken)
    {
        var relativeUrl = BuildRestUrl(command.Entity, command.PrimaryKey);
        var requestUri = new Uri(new Uri(instance.BaseUrl), relativeUrl);

        using var request = new HttpRequestMessage();
        request.RequestUri = requestUri;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        switch (operation)
        {
            case "create":
                request.Method = HttpMethod.Post;
                request.Content = new StringContent(
                    JsonSerializer.Serialize(command.Values, JsonOpts),
                    Encoding.UTF8,
                    "application/json");
                break;

            case "update":
                request.Method = HttpMethod.Patch;
                request.Content = new StringContent(
                    JsonSerializer.Serialize(command.Values, JsonOpts),
                    Encoding.UTF8,
                    "application/json");
                break;

            case "delete":
                request.Method = HttpMethod.Delete;
                break;
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var msg = string.IsNullOrWhiteSpace(responseBody)
                ? $"DAB returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
                : responseBody;
            return new MutationResult(false, msg);
        }

        Dictionary<string, object?>? record = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                {
                    record = JsonSerializer.Deserialize<Dictionary<string, object?>>(val[0].GetRawText(), JsonOpts);
                }
                else
                {
                    record = JsonSerializer.Deserialize<Dictionary<string, object?>>(responseBody, JsonOpts);
                }
            }
            catch
            {
                // Not JSON or empty body (common for 204 No Content deletes)
            }
        }

        return new MutationResult(true, "Operation executed successfully.", record);
    }

    private static string BuildRestUrl(string entity, Dictionary<string, object?>? primaryKey)
    {
        if (primaryKey is null || primaryKey.Count == 0)
        {
            return $"/api/{Uri.EscapeDataString(entity)}";
        }

        var segments = new List<string>();
        foreach (var (k, v) in primaryKey)
        {
            segments.Add(Uri.EscapeDataString(k));
            segments.Add(Uri.EscapeDataString(v?.ToString() ?? string.Empty));
        }

        return $"/api/{Uri.EscapeDataString(entity)}/{string.Join("/", segments)}";
    }

    private Task RecordAuditAsync(
        string instanceId,
        ExecuteMutationCommand command,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var entry = new WriteAuditEntry(
            Id: Guid.NewGuid().ToString("n"),
            Timestamp: DateTimeOffset.UtcNow,
            ClientIp: command.ClientIp ?? "unknown",
            InstanceId: instanceId,
            Entity: command.Entity,
            Operation: command.Operation.ToLowerInvariant(),
            PrimaryKey: command.PrimaryKey,
            PreviousValues: command.PreviousValues,
            NewValues: command.Values,
            Success: success,
            ErrorMessage: errorMessage);

        return _auditStore.RecordAsync(entry, cancellationToken);
    }
}
