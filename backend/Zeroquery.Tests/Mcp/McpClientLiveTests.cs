using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Zeroquery.Core.Mcp;

namespace Zeroquery.Tests.Mcp;

/// <summary>
/// Integration tests that spawn a real `dab start` subprocess against LocalDB Northwinds and
/// exercise <see cref="McpClient"/> against it end-to-end. These lock in the live protocol
/// behavior discovered while building Phase 4 (session header, SSE envelope, tool call
/// shape) — see repo memory notes. Skipped automatically if `dab` or LocalDB/Northwinds
/// aren't available in the current environment (e.g. CI), so they don't fail unrelated runs.
/// </summary>
public class McpClientLiveTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=True;Persist Security Info=False;" +
        "Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Database=Northwinds";

    private Process? _dabProcess;
    private string? _configPath;
    private int _port;
    private bool _environmentAvailable;

    public async Task InitializeAsync()
    {
        _environmentAvailable = await TryStartDabAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dabProcess is { HasExited: false })
        {
            try
            {
                _dabProcess.Kill(entireProcessTree: true);
                _dabProcess.WaitForExit(5000);
            }
            catch
            {
                // best-effort cleanup
            }
        }
        _dabProcess?.Dispose();

        if (_configPath is not null && File.Exists(_configPath))
        {
            File.Delete(_configPath);
        }

        await Task.CompletedTask;
    }

    private async Task<bool> TryStartDabAsync()
    {
        _port = GetFreePort();
        _configPath = Path.Combine(Path.GetTempPath(), $"zq-mcp-test-{Guid.NewGuid():n}.json");

        var config = """
            {
              "$schema": "https://github.com/Azure/data-api-builder/releases/latest/download/dab.draft.schema.json",
              "data-source": { "database-type": "mssql", "connection-string": "@env('ZQ_TEST_CONN')" },
              "runtime": { "mcp": { "enabled": true } },
              "entities": {
                "Products": {
                  "description": "Product catalog",
                  "source": { "object": "dbo.Products", "type": "table" },
                  "fields": [
                    { "name": "ProductID", "primary-key": true },
                    { "name": "ProductName" }
                  ],
                  "permissions": [
                    { "role": "anonymous", "actions": [{ "action": "read", "fields": { "include": ["ProductID", "ProductName"] } }] }
                  ]
                }
              }
            }
            """;
        await File.WriteAllTextAsync(_configPath, config);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dab",
            Arguments = $"start --config \"{_configPath}\" --no-https-redirect",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{_port}";
        startInfo.Environment["ZQ_TEST_CONN"] = ConnectionString;

        try
        {
            _dabProcess = Process.Start(startInfo);
        }
        catch
        {
            return false; // `dab` not on PATH in this environment
        }

        if (_dabProcess is null)
        {
            return false;
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (_dabProcess.HasExited)
            {
                return false; // e.g. LocalDB/Northwinds not available in this environment
            }

            if (await CanConnectAsync(_port))
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [SkippableFact]
    public async Task InitializeAsync_ObtainsSessionAndListsTools()
    {
        Skip.IfNot(_environmentAvailable, "dab CLI / LocalDB Northwinds not available in this environment.");

        await using var client = new McpClient($"http://localhost:{_port}");
        await client.InitializeAsync("zeroquery-tests", "0.1.0");

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "read_records");
        Assert.Contains(tools, t => t.Name == "describe_entities");
    }

    [SkippableFact]
    public async Task CallToolAsync_ReadRecords_ReturnsRealProductData()
    {
        Skip.IfNot(_environmentAvailable, "dab CLI / LocalDB Northwinds not available in this environment.");

        await using var client = new McpClient($"http://localhost:{_port}");
        await client.InitializeAsync("zeroquery-tests", "0.1.0");

        var result = await client.CallToolAsync("read_records", new JsonObject
        {
            ["entity"] = "Products",
            ["first"] = 2
        });

        Assert.False(result.IsError);
        Assert.Contains("ProductName", result.Text);
    }

    [SkippableFact]
    public async Task CallToolAsync_DescribeEntities_ReturnsConfiguredEntity()
    {
        Skip.IfNot(_environmentAvailable, "dab CLI / LocalDB Northwinds not available in this environment.");

        await using var client = new McpClient($"http://localhost:{_port}");
        await client.InitializeAsync("zeroquery-tests", "0.1.0");

        var result = await client.CallToolAsync("describe_entities", new JsonObject());

        Assert.False(result.IsError);
        Assert.Contains("Products", result.Text);
    }
}
