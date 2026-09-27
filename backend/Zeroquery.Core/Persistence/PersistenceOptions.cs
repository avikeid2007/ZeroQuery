namespace Zeroquery.Core.Persistence;

/// <summary>
/// Configuration options for connection and settings persistence (doc/Plan.md Section 4 &amp; Phase 7).
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// Persistence mode: "save" (persists to local disk) or "session-only" (in-memory only).
    /// Default: "save". Env var: <c>MCP_PERSISTENCE_MODE</c>.
    /// </summary>
    public string Mode { get; set; } = "save";

    /// <summary>
    /// Directory where saved connections and settings are stored when Mode is "save".
    /// Defaults to a dedicated folder in the user's AppData/home directory.
    /// Env var: <c>MCP_PERSISTENCE_DIR</c>.
    /// </summary>
    public string StorageDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zeroquery");

    /// <summary>
    /// Whether persistent saving to disk is enabled.
    /// </summary>
    public bool IsSaveMode => string.Equals(Mode, "save", StringComparison.OrdinalIgnoreCase);
}
