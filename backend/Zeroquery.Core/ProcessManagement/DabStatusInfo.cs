namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Status information regarding the local installation of the Microsoft Data API builder (dab) CLI.
/// </summary>
public sealed record DabStatusInfo(
    bool IsInstalled,
    string? Version,
    string ExecutablePath,
    string? Error);

/// <summary>
/// Result of an automated dotnet tool install for Microsoft.DataApiBuilder.
/// </summary>
public sealed record DabInstallResult(
    bool Success,
    string Message);
