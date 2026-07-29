namespace NinjaTrader_Xen.Models;

public sealed record PreflightBuildRequest(
    string Code,
    string Task);

public sealed record PreflightBuildError(
    string? File,
    int? Line,
    int? Column,
    string Code,
    string Message);

public sealed record PreflightBuildResult(
    bool Success,
    IReadOnlyList<PreflightBuildError> Errors,
    long DurationMilliseconds);
