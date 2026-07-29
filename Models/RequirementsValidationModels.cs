namespace NinjaTrader_Xen.Models;

public sealed record RequirementsValidationRequest(
    string Requirements,
    string Code,
    string Task,
    string Model,
    Guid? ProjectId);

public sealed record RequirementsValidationResult(
    string Message,
    string Model,
    int InputTokens,
    int OutputTokens);
