using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.Security.DataProtection;

namespace Zeroquery.Tests.Persistence;

public class LlmSettingsPersistenceTests : IDisposable
{
    private sealed class FakeDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext)
        {
            var result = new byte[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++) result[i] = (byte)(plaintext[i] ^ 0x33);
            return result;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var result = new byte[protectedData.Length];
            for (var i = 0; i < protectedData.Length; i++) result[i] = (byte)(protectedData[i] ^ 0x33);
            return result;
        }
    }

    private sealed class FakeDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new FakeDataProtector();
    }

    private readonly string _tempDir;
    private readonly IConnectionStringProtector _protector;

    public LlmSettingsPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zeroquery-llm-tests-" + Guid.NewGuid().ToString("n"));
        _protector = new DataProtectionConnectionStringProtector(new FakeDataProtectionProvider());
    }

    [Fact]
    public void Update_PersistsToDisk_AndNewStoreReloadsSettings()
    {
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            Mode = "save",
            StorageDirectory = _tempDir
        });

        var initialOpenRouterOptions = Options.Create(new OpenRouterOptions
        {
            ApiKey = "",
            ModelId = "initial/model"
        });

        // Store 1: updates settings
        var store1 = new LlmSettingsStore(
            initialOpenRouterOptions,
            persistenceOptions,
            _protector,
            NullLogger<LlmSettingsStore>.Instance);

        store1.Update("sk-or-v1-my-secret-key-1234", "anthropic/claude-3.5-sonnet");

        // Verify file was written
        var filePath = Path.Combine(_tempDir, "llm-settings.json");
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.DoesNotContain("sk-or-v1-my-secret-key-1234", content); // Key must be encrypted at rest!
        Assert.Contains(DataProtectionConnectionStringProtector.Prefix, content);

        // Store 2: started later (simulating restart) with empty initial settings
        var store2 = new LlmSettingsStore(
            initialOpenRouterOptions,
            persistenceOptions,
            _protector,
            NullLogger<LlmSettingsStore>.Instance);

        var current = store2.Current;
        Assert.Equal("sk-or-v1-my-secret-key-1234", current.ApiKey);
        Assert.Equal("anthropic/claude-3.5-sonnet", current.ModelId);

        var masked = store2.GetMasked();
        Assert.True(masked.IsApiKeyConfigured);
        Assert.EndsWith("1234", masked.ApiKeyMasked);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
