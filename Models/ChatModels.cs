namespace NinjaTrader_Xen.Models;

public sealed record ChatRequest(
    string Prompt,
    string Task,
    string Model,
    IReadOnlyList<ChatTurn>? History);

public sealed record ChatTurn(string Role, string Content);

public sealed class ModelPricing
{
    public decimal InputPer1M { get; init; }

    public decimal OutputPer1M { get; init; }
}
