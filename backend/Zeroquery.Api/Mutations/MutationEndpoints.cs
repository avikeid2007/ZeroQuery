using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Zeroquery.Api.Introspection;
using Zeroquery.Core.Audit;
using Zeroquery.Core.Mutations;

namespace Zeroquery.Api.Mutations;

public static class MutationEndpoints
{
    public static IEndpointRouteBuilder MapMutationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapPost("/instances/{id}/mutate", HandleMutateAsync)
            .RequireRateLimiting("mutations")
            .Produces<MutationResultDto>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests);

        group.MapGet("/audit", HandleGetAuditLogsAsync)
            .Produces<IReadOnlyList<WriteAuditEntryDto>>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> HandleMutateAsync(
        string id,
        [FromBody] ExecuteMutationRequest? request,
        HttpContext httpContext,
        IMutationService mutationService,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.BadRequest(new ErrorResponse("Request body is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Entity))
        {
            return Results.BadRequest(new ErrorResponse("Field 'entity' is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Operation))
        {
            return Results.BadRequest(new ErrorResponse("Field 'operation' is required."));
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var command = request.ToCommand(clientIp);

        var result = await mutationService.ExecuteAsync(id, command, cancellationToken);

        if (!result.Success)
        {
            if (result.IsForbidden)
            {
                return Results.Json(new ErrorResponse(result.Message), statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.BadRequest(new ErrorResponse(result.Message));
        }

        return Results.Ok(MutationResultDto.From(result));
    }

    private static async Task<IResult> HandleGetAuditLogsAsync(
        [FromQuery] int? limit,
        [FromQuery] string? entity,
        IWriteAuditStore auditStore,
        CancellationToken cancellationToken)
    {
        var effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);
        var entries = await auditStore.GetRecentAsync(effectiveLimit, entity, cancellationToken);
        var dtos = entries.Select(WriteAuditEntryDto.From).ToList();
        return Results.Ok(dtos);
    }
}
