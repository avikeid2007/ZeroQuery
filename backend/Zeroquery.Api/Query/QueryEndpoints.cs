using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using Zeroquery.Api.Introspection;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Api.Query;

/// <summary>
/// Maps the Phase 4/5 query endpoints: prompt -> orchestration loop against a running DAB
/// instance -> UI Spec (doc/Plan.md Section 1, "Query flow"). The "/stream" sibling adds
/// Server-Sent Events progress reporting (doc/Plan.md Phase 5, via .NET 10's
/// <see cref="TypedResults.ServerSentEvents{T}(IAsyncEnumerable{SseItem{T}})"/>) so the
/// frontend can show what the tool-calling loop is doing instead of a static spinner.
/// </summary>
public static class QueryEndpoints
{
    /// <summary>
    /// Camel-cased to match the rest of the JSON API (configured globally via
    /// <c>ConfigureHttpJsonOptions</c> in Program.cs for normal <see cref="IResult"/>
    /// responses) — SSE data payloads are serialized manually here rather than going through
    /// that pipeline, so the naming policy must be applied explicitly.
    /// </summary>
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/instances/{id}/query", HandleQueryAsync)
            .WithName("QueryDabInstance")
            .WithSummary("Runs a natural-language query against a running DAB instance's MCP tools and returns a UI Spec.")
            .Produces<UiSpecResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status502BadGateway);

        app.MapPost("/api/instances/{id}/query/stream", HandleQueryStreamAsync)
            .WithName("QueryDabInstanceStream")
            .WithSummary(
                "Same as POST /api/instances/{id}/query, but streams 'progress' Server-Sent " +
                "Events while the tool-calling loop runs, followed by a final 'result' (or " +
                "'error') event instead of a single blocking JSON response.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleQueryAsync(
        string id,
        QueryRequest request,
        DabProcessManager processManager,
        OrchestrationService orchestrationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Results.BadRequest(new ErrorResponse("prompt is required."));
        }

        var instance = processManager.GetInstance(id);
        if (instance is null)
        {
            return Results.NotFound(new ErrorResponse($"No instance found with id '{id}'."));
        }

        var status = processManager.RefreshStatus(instance);
        if (status is not (DabInstanceStatus.Running or DabInstanceStatus.Idle))
        {
            return Results.Conflict(new ErrorResponse(
                $"Instance '{id}' is not ready to query (status: {status}). Wait for it to reach Running before querying."));
        }

        try
        {
            var uiSpec = await orchestrationService.RunQueryAsync(instance.BaseUrl, request.Prompt, cancellationToken)
                .ConfigureAwait(false);

            // A successful query is a real use of the instance — reset its idle-timeout clock.
            processManager.TouchInstance(id);

            return Results.Ok(UiSpecResponse.From(uiSpec));
        }
        catch (Exception ex)
        {
            // Covers McpClientException (upstream DAB instance misbehaving) and any LLM
            // provider failure — both are "something downstream failed", not a Zeroquery bug,
            // so 502 rather than 500.
            return Results.Json(new ErrorResponse(ex.Message), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// SSE sibling of <see cref="HandleQueryAsync"/>. Pre-flight validation (empty prompt,
    /// unknown instance, instance not ready) still returns a normal JSON error response with
    /// the matching status code, exactly like the non-streaming endpoint — the response only
    /// switches to <c>text/event-stream</c> once we're committed to actually running the
    /// orchestration loop, so callers can rely on ordinary HTTP status codes for those
    /// "never started" failure cases.
    /// </summary>
    private static async Task<IResult> HandleQueryStreamAsync(
        string id,
        QueryRequest request,
        DabProcessManager processManager,
        OrchestrationService orchestrationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Results.BadRequest(new ErrorResponse("prompt is required."));
        }

        var instance = processManager.GetInstance(id);
        if (instance is null)
        {
            return Results.NotFound(new ErrorResponse($"No instance found with id '{id}'."));
        }

        var status = processManager.RefreshStatus(instance);
        if (status is not (DabInstanceStatus.Running or DabInstanceStatus.Idle))
        {
            return Results.Conflict(new ErrorResponse(
                $"Instance '{id}' is not ready to query (status: {status}). Wait for it to reach Running before querying."));
        }

        return TypedResults.ServerSentEvents(
            StreamQueryEventsAsync(instance.BaseUrl, request.Prompt, id, processManager, orchestrationService, cancellationToken));
    }

    /// <summary>
    /// Bridges <see cref="OrchestrationService.RunQueryAsync(string, string, OrchestrationProgressCallback?, CancellationToken)"/>'s
    /// callback-style progress reporting into an <see cref="IAsyncEnumerable{T}"/> of SSE
    /// items, since <c>yield</c> can't cross a delegate boundary. A bounded channel buffers
    /// progress events emitted by the orchestration loop (running as a background task)
    /// while the enumerator on the other end drains them into the actual HTTP response.
    /// </summary>
    private static async IAsyncEnumerable<SseItem<string>> StreamQueryEventsAsync(
        string mcpBaseUrl,
        string prompt,
        string instanceId,
        DabProcessManager processManager,
        OrchestrationService orchestrationService,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SseItem<string>>();

        var runTask = Task.Run(async () =>
        {
            try
            {
                var uiSpec = await orchestrationService
                    .RunQueryAsync(
                        mcpBaseUrl,
                        prompt,
                        onProgress: (progressEvent, ct) => WriteAsync(channel, "progress", OrchestrationProgressDto.From(progressEvent), ct).AsTask(),
                        cancellationToken)
                    .ConfigureAwait(false);

                // A successful query is a real use of the instance — reset its idle-timeout clock.
                processManager.TouchInstance(instanceId);

                await WriteAsync(channel, "result", UiSpecResponse.From(uiSpec), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or the request was cancelled — nothing left to write.
            }
            catch (Exception ex)
            {
                // Mirrors the 502 semantics of the non-streaming endpoint (McpClientException
                // or LLM provider failure), but as a final SSE event since headers are
                // already committed once streaming starts.
                await WriteAsync(channel, "error", new ErrorResponse(ex.Message), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }

        await runTask.ConfigureAwait(false);
    }

    private static ValueTask WriteAsync(Channel<SseItem<string>> channel, string eventType, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        return channel.Writer.WriteAsync(new SseItem<string>(json, eventType), cancellationToken);
    }
}
