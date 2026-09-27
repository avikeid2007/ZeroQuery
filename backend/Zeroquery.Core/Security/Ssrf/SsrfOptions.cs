namespace Zeroquery.Core.Security.Ssrf;

/// <summary>
/// Configuration options for SSRF mitigation (doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
public sealed class SsrfOptions
{
    /// <summary>
    /// When false (default), database connection strings targeting loopback, private networks,
    /// or local addresses are permitted. Set to true in hosted or multi-tenant deployments
    /// to prevent SSRF against internal infrastructure.
    /// Env var: <c>MCP_BLOCK_PRIVATE_NETWORKS</c>.
    /// </summary>
    public bool BlockPrivateNetworks { get; set; } = false;
}
