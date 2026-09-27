using Zeroquery.Api.Introspection;
using Zeroquery.Api.Security;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security.DataProtection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Api.Instances;

/// <summary>
/// Maps the Phase 3 instance management endpoints: start a DAB subprocess for a generated
/// config, poll its status, and disconnect (stop) it on demand (doc/Plan.md Section 2.4).
/// Hardened with Phase 6 security controls (SSRF protection, rate limiting, and per-client instance capping).
/// </summary>
public static class InstanceEndpoints
{
    // Config files are written per-instance under a dedicated temp folder rather than a
    // fixed shared path, so concurrent instances never collide and cleanup is trivial on
    // StopInstance.
    private static readonly string ConfigDirectory = Path.Combine(Path.GetTempPath(), "zeroquery-dab-configs");

    public static IEndpointRouteBuilder MapInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instances").WithTags("Instances");

        group.MapPost("/", HandleStartAsync)
            .WithName("StartDabInstance")
            .WithSummary("Writes the given dab-config.json to disk and starts a DAB subprocess for it.")
            .RequireRateLimiting(RateLimitingExtensions.InstancesPolicy)
            .Produces<InstanceStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id}/status", HandleGetStatus)
            .WithName("GetDabInstanceStatus")
            .WithSummary("Returns the current lifecycle status of a running DAB instance.")
            .Produces<InstanceStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", HandleStop)
            .WithName("StopDabInstance")
            .WithSummary("Stops and removes a running DAB instance (manual disconnect).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/dab-status", async (DabProcessManager processManager, CancellationToken ct) =>
        {
            var status = await processManager.GetDabStatusAsync(ct);
            return Results.Ok(status);
        })
        .WithName("GetDabStatus")
        .WithSummary("Checks if Microsoft Data API builder (dab) CLI is installed and available.")
        .Produces<DabStatusInfo>(StatusCodes.Status200OK);

        group.MapPost("/install-dab", async (DabProcessManager processManager, CancellationToken ct) =>
        {
            var result = await processManager.InstallDabAsync(ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        })
        .WithName("InstallDab")
        .WithSummary("Installs or updates Microsoft.DataApiBuilder globally.")
        .Produces<DabInstallResult>(StatusCodes.Status200OK)
        .Produces<DabInstallResult>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> HandleStartAsync(
        StartInstanceRequest request,
        DabProcessManager processManager,
        ISsrfValidator ssrfValidator,
        IConnectionStringProtector connectionStringProtector,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigJson))
        {
            return Results.BadRequest(new ErrorResponse("configJson is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionStringEnvVarName))
        {
            return Results.BadRequest(new ErrorResponse("connectionStringEnvVarName is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Results.BadRequest(new ErrorResponse("connectionString is required."));
        }

        var connectionString = connectionStringProtector.Unprotect(request.ConnectionString);

        try
        {
            await ssrfValidator.ValidateConnectionStringAsync(connectionString, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SsrfException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        Directory.CreateDirectory(ConfigDirectory);
        var configPath = Path.Combine(ConfigDirectory, $"{Guid.NewGuid():n}.json");
        await File.WriteAllTextAsync(configPath, request.ConfigJson, cancellationToken);

        var envVars = new Dictionary<string, string>
        {
            [request.ConnectionStringEnvVarName] = connectionString
        };

        var clientIp = RateLimitingExtensions.ResolveClientIp(httpContext);

        try
        {
            var instance = processManager.StartInstance(configPath, envVars, clientIp);
            var status = processManager.RefreshStatus(instance);
            return Results.Ok(InstanceStatusResponse.From(instance, status));
        }
        catch (DabProcessManagerException ex)
        {
            // Concurrency cap reached is the one case worth a distinct status code so the
            // frontend can tell "try again later" apart from "your input was invalid".
            var statusCode = ex.Message.Contains("Maximum concurrent", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;
            return Results.Json(new ErrorResponse(ex.Message), statusCode: statusCode);
        }
    }

    private static IResult HandleGetStatus(string id, DabProcessManager processManager)
    {
        var instance = processManager.GetInstance(id);
        if (instance is null)
        {
            return Results.NotFound(new ErrorResponse($"No instance found with id '{id}'."));
        }

        var status = processManager.RefreshStatus(instance);
        return Results.Ok(InstanceStatusResponse.From(instance, status));
    }

    private static IResult HandleStop(string id, DabProcessManager processManager)
    {
        var instance = processManager.GetInstance(id);
        if (instance is null)
        {
            return Results.NotFound(new ErrorResponse($"No instance found with id '{id}'."));
        }

        processManager.StopInstance(id);

        // Best-effort cleanup of the temp config file; not fatal if it's already gone.
        try
        {
            if (File.Exists(instance.ConfigPath))
            {
                File.Delete(instance.ConfigPath);
            }
        }
        catch (IOException)
        {
            // Ignore — the reaper/manual disconnect path already succeeded from the caller's
            // perspective; a leftover temp file is a minor cleanup nit, not a failure.
        }

        return Results.NoContent();
    }
}
