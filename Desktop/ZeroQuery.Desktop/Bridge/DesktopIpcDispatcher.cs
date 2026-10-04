using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeroquery.Core.Audit;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Mutations;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security.DataProtection;

namespace ZeroQuery.Desktop.Bridge;

public sealed class DesktopIpcDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceProvider _services;
    private readonly SchemaIntrospectorFactory _introspectorFactory;
    private readonly DabConfigService _configService;
    private readonly DabProcessManager _processManager;
    private readonly ILlmSettingsStore _llmSettingsStore;
    private readonly ISavedConnectionStore _persistenceStore;
    private readonly IMutationService _mutationService;
    private readonly IWriteAuditStore _auditStore;
    private readonly IConnectionStringProtector _connectionProtector;
    private readonly ILogger<DesktopIpcDispatcher> _logger;
    private readonly Action<string> _postWebMessage;

    public DesktopIpcDispatcher(
        IServiceProvider services,
        SchemaIntrospectorFactory introspectorFactory,
        DabConfigService configService,
        DabProcessManager processManager,
        ILlmSettingsStore llmSettingsStore,
        ISavedConnectionStore persistenceStore,
        IMutationService mutationService,
        IWriteAuditStore auditStore,
        IConnectionStringProtector connectionProtector,
        ILogger<DesktopIpcDispatcher> logger,
        Action<string> postWebMessage)
    {
        _services = services;
        _introspectorFactory = introspectorFactory;
        _configService = configService;
        _processManager = processManager;
        _llmSettingsStore = llmSettingsStore;
        _persistenceStore = persistenceStore;
        _mutationService = mutationService;
        _auditStore = auditStore;
        _connectionProtector = connectionProtector;
        _logger = logger;
        _postWebMessage = postWebMessage;
    }

    public async Task DispatchMessageAsync(string rawMessage, CancellationToken cancellationToken = default)
    {
        IpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<IpcRequest>(rawMessage, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse IPC message from WebView: {Raw}", rawMessage);
            return;
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Channel))
        {
            return;
        }

        try
        {
            var result = await HandleChannelAsync(request, cancellationToken).ConfigureAwait(false);
            if (result != null)
            {
                SendResponse(IpcResponse.Ok(request.Id, request.Channel, result));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling IPC channel '{Channel}' (id: {Id})", request.Channel, request.Id);
            SendResponse(IpcResponse.Fail(request.Id, request.Channel, ex.Message));
        }
    }

    private async Task<object?> HandleChannelAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Channel)
        {
            case "introspect":
            {
                var req = SafeDeserialize<IntrospectRequestDto>(request.Payload)
                    ?? throw new ArgumentException("Invalid introspect request payload.");

                var introspector = _introspectorFactory.Resolve(req.Provider);
                var schema = await introspector.GetSchemaAsync(req.ConnectionString, cancellationToken)
                    .ConfigureAwait(false);

                return IntrospectResponseDto.From(schema);
            }

            case "config/generate":
            {
                var req = SafeDeserialize<GenerateConfigRequestDto>(request.Payload)
                    ?? throw new ArgumentException("Invalid generate-config request payload.");

                var entities = req.Entities.Select(e => new EntitySelectionRequest(
                    e.Schema,
                    e.TableName,
                    e.IsView,
                    e.Columns.Select(c => new ColumnSelectionRequest(c.Name, c.Include, c.IsPrimaryKey, c.Description)).ToList(),
                    e.EntityName,
                    e.Description,
                    e.WriteActions)).ToList();

                var coreRequest = new ConfigGenerationRequest(
                    req.Provider,
                    req.ConnectionStringEnvVarName,
                    entities,
                    req.EnableRest ?? true,
                    req.EnableGraphql ?? true);

                var generated = _configService.GenerateAndValidate(coreRequest);
                return new GenerateConfigResponseDto(generated.ConfigJson, generated.IsValid, generated.ValidationErrors);
            }

            case "instances/start":
            {
                var req = SafeDeserialize<StartInstanceRequestDto>(request.Payload)
                    ?? throw new ArgumentException("Invalid start-instance request payload.");

                var configDir = Path.Combine(Path.GetTempPath(), "zeroquery-desktop-configs");
                Directory.CreateDirectory(configDir);
                var configPath = Path.Combine(configDir, $"{Guid.NewGuid():n}.dab-config.json");
                await File.WriteAllTextAsync(configPath, req.ConfigJson, cancellationToken).ConfigureAwait(false);

                var envVars = new Dictionary<string, string>
                {
                    [req.ConnectionStringEnvVarName] = req.ConnectionString
                };

                var instance = _processManager.StartInstance(configPath, envVars, "127.0.0.1");
                return InstanceStatusResponseDto.From(instance, instance.Status);
            }

            case "instances/status":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Instance id is required.");

                var instance = _processManager.GetInstance(id)
                    ?? throw new KeyNotFoundException($"No instance found with id '{id}'.");

                var status = _processManager.RefreshStatus(instance);
                return InstanceStatusResponseDto.From(instance, status);
            }

            case "instances/stop":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Instance id is required.");

                _processManager.StopInstance(id);
                return new { stopped = true };
            }

            case "instances/dab-status":
            {
                return await _processManager.GetDabStatusAsync(cancellationToken).ConfigureAwait(false);
            }

            case "instances/install-dab":
            {
                return await _processManager.InstallDabAsync(cancellationToken).ConfigureAwait(false);
            }

            case "instances/query":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Instance id is required.");
                var prompt = GetStringProperty(request.Payload, "prompt")
                    ?? throw new ArgumentException("Prompt is required.");

                var instance = _processManager.GetInstance(id)
                    ?? throw new KeyNotFoundException($"No instance found with id '{id}'.");

                using var scope = _services.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestrationService>();

                var uiSpec = await orchestrator.RunQueryAsync(instance.BaseUrl, prompt, null, cancellationToken)
                    .ConfigureAwait(false);

                _processManager.TouchInstance(id);
                return uiSpec;
            }

            case "instances/query/stream":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Instance id is required.");
                var prompt = GetStringProperty(request.Payload, "prompt")
                    ?? throw new ArgumentException("Prompt is required.");

                var instance = _processManager.GetInstance(id)
                    ?? throw new KeyNotFoundException($"No instance found with id '{id}'.");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _services.CreateScope();
                        var orchestrator = scope.ServiceProvider.GetRequiredService<OrchestrationService>();

                        var uiSpec = await orchestrator.RunQueryAsync(
                            instance.BaseUrl,
                            prompt,
                            (progressEvent, _) =>
                            {
                                SendStreamMessage(new IpcStreamMessage
                                {
                                    Id = request.Id,
                                    Channel = "query/stream",
                                    Event = "progress",
                                    Data = new
                                    {
                                        stage = progressEvent.Stage.ToString().ToLowerInvariant(),
                                        toolName = progressEvent.ToolName
                                    }
                                });
                                return Task.CompletedTask;
                            },
                            cancellationToken).ConfigureAwait(false);

                        _processManager.TouchInstance(id);

                        SendStreamMessage(new IpcStreamMessage
                        {
                            Id = request.Id,
                            Channel = "query/stream",
                            Event = "result",
                            Data = uiSpec
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Query streaming error: {Message}", ex.Message);
                        SendStreamMessage(new IpcStreamMessage
                        {
                            Id = request.Id,
                            Channel = "query/stream",
                            Event = "error",
                            Error = ex.Message
                        });
                    }
                }, cancellationToken);

                return null;
            }

            case "settings/llm/get":
            {
                var masked = _llmSettingsStore.GetMasked();
                return LlmSettingsResponseDto.From(masked);
            }

            case "settings/llm/update":
            {
                var req = SafeDeserialize<UpdateLlmSettingsRequestDto>(request.Payload)
                    ?? throw new ArgumentException("Invalid LLM settings payload.");

                _llmSettingsStore.Update(req.ApiKey, req.ModelId, req.SystemPrompt);
                var updated = _llmSettingsStore.GetMasked();
                return LlmSettingsResponseDto.From(updated);
            }

            case "connections/list":
            {
                var list = await _persistenceStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
                return list.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    provider = c.Provider,
                    createdAt = c.CreatedAt,
                    lastConnectedAt = c.LastConnectedAt
                }).ToList();
            }

            case "connections/get":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Connection id is required.");

                var conn = await _persistenceStore.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Connection not found: {id}");

                return new
                {
                    id = conn.Id,
                    name = conn.Name,
                    provider = conn.Provider,
                    configJson = conn.ConfigJson,
                    connectionStringEnvVarName = conn.ConnectionStringEnvVarName,
                    createdAt = conn.CreatedAt,
                    lastConnectedAt = conn.LastConnectedAt
                };
            }

            case "connections/save":
            {
                var req = SafeDeserialize<SaveConnectionRequestDto>(request.Payload)
                    ?? throw new ArgumentException("Invalid connection save payload.");

                var encryptedConnStr = _connectionProtector.Protect(req.ConnectionString);
                var saved = await _persistenceStore.SaveAsync(new SavedConnection
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Name = req.Name,
                    Provider = req.Provider,
                    EncryptedConnectionString = encryptedConnStr,
                    ConfigJson = req.ConfigJson,
                    ConnectionStringEnvVarName = "ZQ_DB_CONN"
                }, cancellationToken).ConfigureAwait(false);

                return new
                {
                    id = saved.Id,
                    name = saved.Name,
                    provider = saved.Provider,
                    createdAt = saved.CreatedAt,
                    lastConnectedAt = saved.LastConnectedAt
                };
            }

            case "connections/reconnect":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Connection id is required.");

                var saved = await _persistenceStore.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Connection not found: {id}");

                var rawConnStr = _connectionProtector.Unprotect(saved.EncryptedConnectionString);

                var configDir = Path.Combine(Path.GetTempPath(), "zeroquery-desktop-configs");
                Directory.CreateDirectory(configDir);
                var configPath = Path.Combine(configDir, $"{Guid.NewGuid():n}.dab-config.json");
                await File.WriteAllTextAsync(configPath, saved.ConfigJson, cancellationToken).ConfigureAwait(false);

                var envVars = new Dictionary<string, string>
                {
                    [saved.ConnectionStringEnvVarName] = rawConnStr
                };

                var instance = _processManager.StartInstance(configPath, envVars, "127.0.0.1");
                await _persistenceStore.UpdateLastConnectedAsync(id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

                return InstanceStatusResponseDto.From(instance, instance.Status);
            }

            case "connections/delete":
            {
                var id = GetStringProperty(request.Payload, "id")
                    ?? throw new ArgumentException("Connection id is required.");

                await _persistenceStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                return new { deleted = true };
            }

            case "mutations/execute":
            {
                var instanceId = GetStringProperty(request.Payload, "instanceId")
                    ?? throw new ArgumentException("instanceId is required.");

                var entity = GetStringProperty(request.Payload, "entity") ?? "";
                var operation = GetStringProperty(request.Payload, "operation") ?? "";

                Dictionary<string, object?>? primaryKey = null;
                if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("primaryKey", out var pkProp) && pkProp.ValueKind == JsonValueKind.Object)
                {
                    primaryKey = JsonSerializer.Deserialize<Dictionary<string, object?>>(pkProp.GetRawText(), JsonOptions);
                }

                Dictionary<string, object?>? previousValues = null;
                if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("previousValues", out var prevProp) && prevProp.ValueKind == JsonValueKind.Object)
                {
                    previousValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(prevProp.GetRawText(), JsonOptions);
                }

                Dictionary<string, object?> values = new();
                if (request.Payload.ValueKind == JsonValueKind.Object && request.Payload.TryGetProperty("values", out var valProp) && valProp.ValueKind == JsonValueKind.Object)
                {
                    values = JsonSerializer.Deserialize<Dictionary<string, object?>>(valProp.GetRawText(), JsonOptions) ?? new();
                }

                var command = new ExecuteMutationCommand(
                    entity,
                    operation,
                    primaryKey,
                    previousValues,
                    values,
                    "127.0.0.1");

                var result = await _mutationService.ExecuteAsync(instanceId, command, cancellationToken)
                    .ConfigureAwait(false);

                return result;
            }

            case "audit/list":
            {
                int limit = 50;
                string? entity = null;
                if (request.Payload.ValueKind == JsonValueKind.Object)
                {
                    if (request.Payload.TryGetProperty("limit", out var limProp) && limProp.TryGetInt32(out var l)) limit = l;
                    if (request.Payload.TryGetProperty("entity", out var entProp)) entity = entProp.GetString();
                }

                var list = await _auditStore.GetRecentAsync(limit, entity, cancellationToken).ConfigureAwait(false);
                return list;
            }

            default:
                throw new NotSupportedException($"Unsupported channel '{request.Channel}'.");
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    private static T? SafeDeserialize<T>(JsonElement element) where T : class
    {
        if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        try
        {
            return element.Deserialize<T>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SendResponse(IpcResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        _postWebMessage(json);
    }

    private void SendStreamMessage(IpcStreamMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        _postWebMessage(json);
    }
}
