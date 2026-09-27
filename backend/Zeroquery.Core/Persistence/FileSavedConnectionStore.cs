using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zeroquery.Core.Persistence;

/// <summary>
/// File-based implementation of <see cref="ISavedConnectionStore"/>.
/// When <see cref="PersistenceOptions.IsSaveMode"/> is true, persists connections to a local
/// JSON file atomically. When false ("session-only"), keeps connections in-memory only.
/// </summary>
public sealed class FileSavedConnectionStore : ISavedConnectionStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, SavedConnection> _connections = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly PersistenceOptions _options;
    private readonly ILogger<FileSavedConnectionStore> _logger;
    private readonly string _filePath;
    private bool _initialized;

    public FileSavedConnectionStore(IOptions<PersistenceOptions> options, ILogger<FileSavedConnectionStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _filePath = Path.Combine(_options.StorageDirectory, "connections.json");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            if (_options.IsSaveMode && File.Exists(_filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
                    var loaded = JsonSerializer.Deserialize<List<SavedConnection>>(json, JsonOptions);
                    if (loaded is not null)
                    {
                        foreach (var conn in loaded)
                        {
                            _connections[conn.Id] = conn;
                        }
                        _logger.LogInformation("Loaded {Count} saved connections from {FilePath}.", _connections.Count, _filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load saved connections from {FilePath}. Starting empty.", _filePath);
                }
            }

            _initialized = true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<SavedConnection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        return _connections.Values
            .OrderByDescending(c => c.LastConnectedAt ?? c.CreatedAt)
            .ToList();
    }

    public async Task<SavedConnection?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        return _connections.GetValueOrDefault(id);
    }

    public async Task<SavedConnection> SaveAsync(SavedConnection connection, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        _connections[connection.Id] = connection;

        if (_options.IsSaveMode)
        {
            await PersistToDiskAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_connections.TryRemove(id, out _))
        {
            if (_options.IsSaveMode)
            {
                await PersistToDiskAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }

        return false;
    }

    public async Task<SavedConnection?> UpdateLastConnectedAsync(string id, DateTimeOffset connectedAt, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_connections.TryGetValue(id, out var connection))
        {
            connection.LastConnectedAt = connectedAt;
            if (_options.IsSaveMode)
            {
                await PersistToDiskAsync(cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }

        return null;
    }

    private async Task PersistToDiskAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_options.StorageDirectory);

            var list = _connections.Values.ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);

            var tempFilePath = Path.Combine(_options.StorageDirectory, $"{Guid.NewGuid():n}.tmp");
            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, _filePath, overwrite: true);

            _logger.LogDebug("Persisted {Count} saved connections to {FilePath}.", list.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist saved connections to {FilePath}.", _filePath);
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        _fileLock.Dispose();
    }
}
