using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Persistence;

namespace Zeroquery.Core.Audit;

/// <summary>
/// Thread-safe file-backed implementation of <see cref="IWriteAuditStore"/>.
/// Maintains a persistent audit trail in 'audit-log.json' with atomic file writes.
/// </summary>
public sealed class FileWriteAuditStore : IWriteAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IOptions<PersistenceOptions> _options;
    private readonly ILogger<FileWriteAuditStore> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly List<WriteAuditEntry> _inMemoryEntries = new();
    private bool _loaded;

    public FileWriteAuditStore(
        IOptions<PersistenceOptions> options,
        ILogger<FileWriteAuditStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task RecordAsync(WriteAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            _inMemoryEntries.Add(entry);

            if (!_options.Value.IsSaveMode)
            {
                return;
            }

            await FlushToFileAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<WriteAuditEntry>> GetRecentAsync(
        int limit = 50,
        string? entity = null,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var query = _inMemoryEntries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(entity))
            {
                query = query.Where(e => string.Equals(e.Entity, entity, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        if (!_options.Value.IsSaveMode)
        {
            return;
        }

        var filePath = GetAuditFilePath();
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<List<WriteAuditEntry>>(json, JsonOptions);
                if (loaded is not null)
                {
                    _inMemoryEntries.Clear();
                    _inMemoryEntries.AddRange(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load write audit log from '{FilePath}'", filePath);
        }
    }

    private async Task FlushToFileAsync(CancellationToken cancellationToken)
    {
        var dir = _options.Value.StorageDirectory;
        Directory.CreateDirectory(dir);

        var filePath = GetAuditFilePath();
        var tempPath = Path.Combine(dir, $"audit-log.{Guid.NewGuid():n}.tmp");

        var json = JsonSerializer.Serialize(_inMemoryEntries, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);

        File.Move(tempPath, filePath, overwrite: true);
    }

    private string GetAuditFilePath() =>
        Path.Combine(_options.Value.StorageDirectory, "audit-log.json");
}
