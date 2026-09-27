using Zeroquery.Core.ConfigGeneration;

namespace Zeroquery.Api.ConfigGeneration;

/// <summary>
/// Maps the Phase 2 config generation endpoint (picker selection -> dab-config.json), used
/// after a user confirms their table/column choices in the setup wizard.
/// </summary>
public static class GenerateConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/config/generate", HandleGenerateAsync)
            .WithName("GenerateDabConfig")
            .WithSummary("Converts a table/column picker selection into a dab-config.json, validated against DAB's published schema.")
            .Produces<GenerateConfigResponse>(StatusCodes.Status200OK)
            .Produces<Introspection.ErrorResponse>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static Task<IResult> HandleGenerateAsync(GenerateConfigRequest request, DabConfigService service)
    {
        if (request.Entities.Count == 0)
        {
            return Task.FromResult(Results.BadRequest(new Introspection.ErrorResponse("At least one entity must be selected.")));
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionStringEnvVarName))
        {
            return Task.FromResult(Results.BadRequest(new Introspection.ErrorResponse("connectionStringEnvVarName is required.")));
        }

        ConfigGenerationResult result;
        try
        {
            var coreRequest = new ConfigGenerationRequest(
                request.Provider,
                request.ConnectionStringEnvVarName,
                request.Entities.Select(e => e.ToCoreRequest()).ToList(),
                request.EnableRest ?? true,
                request.EnableGraphQL ?? true);

            result = service.GenerateAndValidate(coreRequest);
        }
        catch (InvalidOperationException ex)
        {
            // Thrown by the generator for caller-fixable input problems (no columns selected,
            // unsupported write action, etc.) — surface as 400, not a 500.
            return Task.FromResult(Results.BadRequest(new Introspection.ErrorResponse(ex.Message)));
        }

        return Task.FromResult(Results.Ok(GenerateConfigResponse.From(result)));
    }
}
