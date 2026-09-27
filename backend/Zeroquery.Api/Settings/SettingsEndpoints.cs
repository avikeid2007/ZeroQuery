using Zeroquery.Core.Orchestration;

namespace Zeroquery.Api.Settings;

/// <summary>
/// Maps the LLM settings endpoints, letting a user configure the OpenRouter API key/model
/// from the UI instead of environment variables or appsettings.json (doc/Plan.md Section 8).
/// </summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/llm", (ILlmSettingsStore store) =>
                Results.Ok(LlmSettingsResponse.From(store.GetMasked())))
            .WithName("GetLlmSettings")
            .WithSummary("Returns the current LLM provider settings. The API key is never returned in full — only a masked suffix.")
            .Produces<LlmSettingsResponse>(StatusCodes.Status200OK);

        group.MapPut("/llm", (UpdateLlmSettingsRequest request, ILlmSettingsStore store) =>
            {
                store.Update(request.ApiKey, request.ModelId);
                return Results.Ok(LlmSettingsResponse.From(store.GetMasked()));
            })
            .WithName("UpdateLlmSettings")
            .WithSummary("Updates the LLM provider settings in-memory for this running backend instance (does not persist across restarts).")
            .Produces<LlmSettingsResponse>(StatusCodes.Status200OK);

        return app;
    }
}
