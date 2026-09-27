using Microsoft.Extensions.Options;
using Zeroquery.Core.Orchestration;

namespace Zeroquery.Tests.Orchestration;

public class LlmSettingsStoreTests
{
    private static LlmSettingsStore CreateStore(string apiKey = "", string modelId = "openrouter/auto") =>
        new(Options.Create(new OpenRouterOptions { ApiKey = apiKey, ModelId = modelId }));

    [Fact]
    public void Current_SeedsFromOpenRouterOptions_AtConstruction()
    {
        var store = CreateStore(apiKey: "sk-or-v1-abcdef1234", modelId: "openai/gpt-4o");

        Assert.Equal("sk-or-v1-abcdef1234", store.Current.ApiKey);
        Assert.Equal("openai/gpt-4o", store.Current.ModelId);
    }

    [Fact]
    public void GetMasked_NeverExposesRawApiKey()
    {
        var store = CreateStore(apiKey: "sk-or-v1-abcdef1234");

        var masked = store.GetMasked();

        Assert.True(masked.IsApiKeyConfigured);
        Assert.NotNull(masked.ApiKeyMasked);
        Assert.DoesNotContain("sk-or-v1-abcdef1234", masked.ApiKeyMasked);
        Assert.EndsWith("1234", masked.ApiKeyMasked);
        Assert.True(masked.ApiKeyMasked!.Length < "sk-or-v1-abcdef1234".Length);
    }

    [Fact]
    public void GetMasked_ReportsNotConfigured_WhenApiKeyEmpty()
    {
        var store = CreateStore(apiKey: "");

        var masked = store.GetMasked();

        Assert.False(masked.IsApiKeyConfigured);
        Assert.Null(masked.ApiKeyMasked);
    }

    [Fact]
    public void Update_ChangesApiKey_WhenNonNullProvided()
    {
        var store = CreateStore(apiKey: "old-key");

        store.Update(apiKey: "new-key", modelId: null);

        Assert.Equal("new-key", store.Current.ApiKey);
    }

    [Fact]
    public void Update_LeavesApiKeyUnchanged_WhenNullProvided()
    {
        var store = CreateStore(apiKey: "keep-me");

        store.Update(apiKey: null, modelId: "some/model");

        Assert.Equal("keep-me", store.Current.ApiKey);
        Assert.Equal("some/model", store.Current.ModelId);
    }

    [Fact]
    public void Update_ClearsApiKey_WhenEmptyStringProvided()
    {
        var store = CreateStore(apiKey: "will-be-cleared");

        store.Update(apiKey: "", modelId: null);

        Assert.Equal(string.Empty, store.Current.ApiKey);
        Assert.False(store.GetMasked().IsApiKeyConfigured);
    }

    [Fact]
    public void Update_LeavesModelIdUnchanged_WhenNullOrEmptyProvided()
    {
        var store = CreateStore(modelId: "original/model");

        store.Update(apiKey: null, modelId: "");

        Assert.Equal("original/model", store.Current.ModelId);
    }

    [Fact]
    public void Update_ChangesModelId_WhenNonEmptyProvided()
    {
        var store = CreateStore(modelId: "original/model");

        store.Update(apiKey: null, modelId: "updated/model");

        Assert.Equal("updated/model", store.Current.ModelId);
    }

    [Fact]
    public void Update_ChangesSystemPrompt_WhenProvided()
    {
        var store = CreateStore();
        Assert.Null(store.Current.SystemPrompt);

        store.Update(apiKey: null, modelId: null, systemPrompt: "Custom prompt here");

        Assert.Equal("Custom prompt here", store.Current.SystemPrompt);
        Assert.Equal("Custom prompt here", store.GetMasked().SystemPrompt);
    }

    [Fact]
    public void Update_ResetsSystemPrompt_WhenEmptyOrResetKeywordProvided()
    {
        var store = CreateStore();
        store.Update(apiKey: null, modelId: null, systemPrompt: "Custom prompt");
        Assert.Equal("Custom prompt", store.Current.SystemPrompt);

        store.Update(apiKey: null, modelId: null, systemPrompt: "");
        Assert.Null(store.Current.SystemPrompt);

        store.Update(apiKey: null, modelId: null, systemPrompt: "Another prompt");
        store.Update(apiKey: null, modelId: null, systemPrompt: "__RESET__");
        Assert.Null(store.Current.SystemPrompt);
    }
}
