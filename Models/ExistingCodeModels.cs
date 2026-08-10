namespace NinjaTrader_Xen.Models;

public sealed record ExistingCodeSource(string Id, string FileName, string Role, string Code);
public sealed record ExistingCodeDecision(string UserText, string AssistantText);
public sealed record ExistingCodeState(IReadOnlyList<ExistingCodeSource> Sources,
    IReadOnlyList<ExistingCodeDecision> Decisions, string? WorkingCode = null)
{
    public static ExistingCodeState Empty { get; } = new([], []);
}
public sealed record SaveExistingCodeStateRequest(string Task,
    IReadOnlyList<ExistingCodeSource> Sources,
    IReadOnlyList<ExistingCodeDecision>? Decisions = null,
    string? WorkingCode = null);
