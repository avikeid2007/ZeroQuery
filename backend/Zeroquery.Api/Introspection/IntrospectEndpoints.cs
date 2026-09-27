using Zeroquery.Api.Security;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Security.DataProtection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Api.Introspection;

/// <summary>
/// Maps the schema introspection endpoint(s) used by the Phase 1 setup wizard
/// (connection string -> table/column picker). Protected by Phase 6 SSRF validation
/// and rate limiting.
/// </summary>
public static class IntrospectEndpoints
{
    public static IEndpointRouteBuilder MapIntrospectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/introspect", HandleIntrospectAsync)
            .WithName("IntrospectDatabase")
            .WithSummary("Reads table/column/foreign-key metadata for a connection string. Never reads row data.")
            .RequireRateLimiting(RateLimitingExtensions.IntrospectPolicy)
            .Produces<IntrospectResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> HandleIntrospectAsync(
        IntrospectRequest request,
        SchemaIntrospectorFactory factory,
        ISsrfValidator ssrfValidator,
        IConnectionStringProtector connectionStringProtector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            return Results.BadRequest(new ErrorResponse("connectionString is required."));
        }

        var connectionString = connectionStringProtector.Unprotect(request.ConnectionString);

        try
        {
            await ssrfValidator.ValidateConnectionStringAsync(request.Provider, connectionString, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SsrfException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        ISchemaIntrospector introspector;
        try
        {
            introspector = factory.Resolve(request.Provider);
        }
        catch (SchemaIntrospectionException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        try
        {
            var schema = await introspector.GetSchemaAsync(connectionString, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(IntrospectResponse.From(schema));
        }
        catch (SchemaIntrospectionException ex)
        {
            // Connection/auth/permission failures against the user's own database -> Bad Gateway,
            // since Zeroquery itself is fine, it's the upstream (user-provided) DB that failed.
            return Results.Json(new ErrorResponse(ex.Message), statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
