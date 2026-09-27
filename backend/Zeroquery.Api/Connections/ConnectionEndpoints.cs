using System.Data.Common;
using Zeroquery.Api.Instances;
using Zeroquery.Api.Introspection;
using Zeroquery.Api.Security;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security.DataProtection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Api.Connections;

/// <summary>
/// Maps the Phase 7 connection persistence endpoints: list saved connections, save,
/// reconnect with explicit confirmation, and forget (delete) connection.
/// </summary>
public static class ConnectionEndpoints
{
    private static readonly string ConfigDirectory = Path.Combine(Path.GetTempPath(), "zeroquery-dab-configs");

    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections").WithTags("SavedConnections");

        group.MapGet("/", HandleGetAllAsync)
            .WithName("GetSavedConnections")
            .WithSummary("Returns all saved database connections (metadata only, no raw credentials).")
            .Produces<IReadOnlyList<SavedConnectionSummaryResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", HandleSaveAsync)
            .WithName("SaveConnection")
            .WithSummary("Saves a validated connection profile with connection string encrypted at rest.")
            .RequireRateLimiting(RateLimitingExtensions.InstancesPolicy)
            .Produces<SavedConnectionSummaryResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", HandleGetByIdAsync)
            .WithName("GetSavedConnectionById")
            .WithSummary("Returns details for a saved connection for reconnect confirmation.")
            .Produces<SavedConnectionDetailResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/connect", HandleReconnectAsync)
            .WithName("ReconnectSavedConnection")
            .WithSummary("Explicit confirmation flow: starts a DAB instance from a saved connection profile.")
            .RequireRateLimiting(RateLimitingExtensions.InstancesPolicy)
            .Produces<InstanceStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/{id}", HandleForgetAsync)
            .WithName("ForgetSavedConnection")
            .WithSummary("Deletes a saved connection profile from persistent storage.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleGetAllAsync(ISavedConnectionStore store, CancellationToken cancellationToken)
    {
        var connections = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var summaries = connections.Select(SavedConnectionSummaryResponse.From).ToList();
        return Results.Ok(summaries);
    }

    private static async Task<IResult> HandleGetByIdAsync(string id, ISavedConnectionStore store, CancellationToken cancellationToken)
    {
        var connection = await store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return Results.NotFound(new ErrorResponse($"Saved connection with id '{id}' was not found."));
        }

        return Results.Ok(SavedConnectionDetailResponse.From(connection));
    }

    private static async Task<IResult> HandleSaveAsync(
        SaveConnectionRequest request,
        ISavedConnectionStore store,
        ISsrfValidator ssrfValidator,
        IConnectionStringProtector protector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Results.BadRequest(new ErrorResponse("connectionString is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ConfigJson))
        {
            return Results.BadRequest(new ErrorResponse("configJson is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionStringEnvVarName))
        {
            return Results.BadRequest(new ErrorResponse("connectionStringEnvVarName is required."));
        }

        var rawConn = protector.Unprotect(request.ConnectionString);

        try
        {
            await ssrfValidator.ValidateConnectionStringAsync(request.Provider, rawConn, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SsrfException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        var name = string.IsNullOrWhiteSpace(request.Name)
            ? InferDatabaseName(rawConn, request.Provider)
            : request.Name.Trim();

        var encryptedConn = protector.Protect(rawConn);

        var saved = new SavedConnection
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = name,
            Provider = request.Provider,
            EncryptedConnectionString = encryptedConn,
            ConfigJson = request.ConfigJson,
            ConnectionStringEnvVarName = request.ConnectionStringEnvVarName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(saved, cancellationToken).ConfigureAwait(false);

        return Results.Created($"/api/connections/{saved.Id}", SavedConnectionSummaryResponse.From(saved));
    }

    private static async Task<IResult> HandleReconnectAsync(
        string id,
        ISavedConnectionStore store,
        DabProcessManager processManager,
        ISsrfValidator ssrfValidator,
        IConnectionStringProtector protector,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var saved = await store.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (saved is null)
        {
            return Results.NotFound(new ErrorResponse($"Saved connection with id '{id}' was not found."));
        }

        var rawConn = protector.Unprotect(saved.EncryptedConnectionString);

        try
        {
            await ssrfValidator.ValidateConnectionStringAsync(saved.Provider, rawConn, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SsrfException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        Directory.CreateDirectory(ConfigDirectory);
        var configPath = Path.Combine(ConfigDirectory, $"{Guid.NewGuid():n}.json");
        await File.WriteAllTextAsync(configPath, saved.ConfigJson, cancellationToken);

        var envVars = new Dictionary<string, string>
        {
            [saved.ConnectionStringEnvVarName] = rawConn
        };

        var clientIp = RateLimitingExtensions.ResolveClientIp(httpContext);

        try
        {
            var instance = processManager.StartInstance(configPath, envVars, clientIp);
            var status = processManager.RefreshStatus(instance);

            // Update last connected timestamp
            await store.UpdateLastConnectedAsync(id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

            return Results.Ok(InstanceStatusResponse.From(instance, status));
        }
        catch (DabProcessManagerException ex)
        {
            var statusCode = ex.Message.Contains("Maximum concurrent", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;
            return Results.Json(new ErrorResponse(ex.Message), statusCode: statusCode);
        }
    }

    private static async Task<IResult> HandleForgetAsync(
        string id,
        ISavedConnectionStore store,
        CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return Results.NotFound(new ErrorResponse($"Saved connection with id '{id}' was not found."));
        }

        return Results.NoContent();
    }

    private static string InferDatabaseName(string connectionString, DatabaseProvider provider)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var key in new[] { "Database", "Initial Catalog" })
            {
                if (builder.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }
        }
        catch
        {
            // fallback
        }

        return $"{provider} Database";
    }
}
