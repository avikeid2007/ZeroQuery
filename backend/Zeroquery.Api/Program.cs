using System.Text.Json.Serialization;
using Zeroquery.Api.ConfigGeneration;
using Zeroquery.Api.Connections;
using Zeroquery.Api.Instances;
using Zeroquery.Api.Introspection;
using Zeroquery.Api.Mutations;
using Zeroquery.Api.Query;
using Zeroquery.Api.Security;
using Zeroquery.Api.Settings;
using Zeroquery.Core.Audit;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Mutations;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendDev";

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDataProtection();
builder.Services.AddZeroquerySecurity(options =>
{
    if (bool.TryParse(builder.Configuration["MCP_BLOCK_PRIVATE_NETWORKS"], out var blockPrivate))
    {
        options.BlockPrivateNetworks = blockPrivate;
    }
});
builder.Services.AddZeroqueryPersistence(options =>
{
    if (builder.Configuration["MCP_PERSISTENCE_MODE"] is { Length: > 0 } mode)
    {
        options.Mode = mode;
    }
    if (builder.Configuration["MCP_PERSISTENCE_DIR"] is { Length: > 0 } dir)
    {
        options.StorageDirectory = dir;
    }
});
builder.Services.AddZeroqueryAudit();
builder.Services.AddZeroqueryMutations();
builder.Services.AddZeroqueryRateLimiting(builder.Configuration);
builder.Services.AddSchemaIntrospection();
builder.Services.AddDabConfigGeneration();

// Deployment-configurable settings (doc/Plan.md Section 4 & Phase 6) — documented env var names take
// precedence over the .NET-conventional DabProcessManager__* binding so self-hosters can use
// either style.
builder.Services.AddDabProcessManagement(options =>
{
    builder.Configuration.GetSection("DabProcessManager").Bind(options);

    if (int.TryParse(builder.Configuration["MCP_MAX_CONCURRENT_INSTANCES"], out var maxInstances))
    {
        options.MaxConcurrentInstances = maxInstances;
    }

    if (int.TryParse(builder.Configuration["MCP_IDLE_TIMEOUT_MINUTES"], out var idleTimeout))
    {
        options.IdleTimeoutMinutes = idleTimeout;
    }

    if (int.TryParse(builder.Configuration["MCP_INSTANCE_MAX_MEMORY_MB"], out var maxMemoryMb))
    {
        options.MaxMemoryMegabytes = maxMemoryMb;
    }

    if (int.TryParse(builder.Configuration["MCP_INSTANCE_CPU_LIMIT_PERCENT"], out var cpuLimitPercent))
    {
        options.CpuLimitPercent = cpuLimitPercent;
    }

    if (builder.Configuration["MCP_SUBPROCESS_USER"] is { Length: > 0 } user)
    {
        options.SubprocessUserName = user;
    }

    if (builder.Configuration["MCP_SUBPROCESS_PASSWORD"] is { Length: > 0 } pass)
    {
        options.SubprocessPassword = pass;
    }

    if (builder.Configuration["MCP_SUBPROCESS_DOMAIN"] is { Length: > 0 } domain)
    {
        options.SubprocessDomain = domain;
    }

    if (int.TryParse(builder.Configuration["MCP_MAX_INSTANCES_PER_IP"], out var maxPerIp))
    {
        options.MaxInstancesPerIp = maxPerIp;
    }
});

// Phase 4 orchestration: LLM tool-calling loop against a running instance's MCP tools.
// LLM_MODEL_ID / OPENROUTER_API_KEY are the documented env var names (doc/Plan.md Section 8);
// the OpenRouter__* binding is also honored for consistency with the process manager options.
builder.Services.AddZeroqueryOrchestration(configureOpenRouter: options =>
{
    builder.Configuration.GetSection("OpenRouter").Bind(options);

    if (builder.Configuration["OPENROUTER_API_KEY"] is { Length: > 0 } apiKey)
    {
        options.ApiKey = apiKey;
    }

    if (builder.Configuration["LLM_MODEL_ID"] is { Length: > 0 } modelId)
    {
        options.ModelId = modelId;
    }
});

// Serialize enums (DatabaseProvider) as their string names (e.g. "SqlServer") rather than
// numeric indices, so the frontend can send/receive human-readable provider values.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    // Phase 1 dev-only policy: allow the local Next.js dev server to call the API.
    // Tighten/replace this before any non-local deployment.
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();

app.MapIntrospectionEndpoints();
app.MapConfigGenerationEndpoints();
app.MapInstanceEndpoints();
app.MapConnectionEndpoints();
app.MapQueryEndpoints();
app.MapSettingsEndpoints();
app.MapMutationEndpoints();

// Graceful shutdown: terminate all child DAB processes cleanly on app restart/deploy
// (doc/Plan.md Section 2.4). DabIdleReaperService.StopAsync also calls StopAll(), but this
// covers the case where the reaper service itself never got a chance to run.
app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<DabProcessManager>().StopAll();
});

app.Run();


