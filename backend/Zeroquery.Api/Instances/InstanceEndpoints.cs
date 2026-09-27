using Zeroquery.Api.Introspection;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Api.Instances;

/// <summary>
/// Maps the Phase 3 instance management endpoints: start a DAB subprocess for a generated
/// config, poll its status, and disconnect (stop) it on demand (doc/Plan.md Section 2.4).
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

        return app;
    }

    private static async Task<IResult> HandleStartAsync(StartInstanceRequest request, DabProcessManager processManager)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigJson))
        {
            return Results.BadRequest(new ErrorResponse("configJson is required."));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionStringEnvVarName))
        {
            return Results.BadRequest(new ErrorResponse("connectionStringEnvVarName is required."));
        }

        Directory.CreateDirectory(ConfigDirectory);
        var configPath = Path.Combine(ConfigDirectory, $"{Guid.NewGuid():n}.json");
        await File.WriteAllTextAsync(configPath, request.ConfigJson);

        var envVars = new Dictionary<string, string>
        {
            [request.ConnectionStringEnvVarName] = request.ConnectionString
        };

        try
        {
            var instance = processManager.StartInstance(configPath, envVars);
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
