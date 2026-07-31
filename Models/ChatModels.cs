namespace NinjaTrader_Xen.Models;

public sealed record ChatRequest(
    string Prompt,
    string Task,
    string Model,
    IReadOnlyList<ChatTurn>? History,
    ChatImageRequest? Image = null,
    Guid? ProjectId = null,
    string? RetrievalPrompt = null);

public sealed record ChatTurn(string Role, string Content);

public sealed record ChatImageRequest(
    string Name,
    string Type,
    string Data);

public sealed record AiImage(
    string Name,
    string MediaType,
    string Base64Data);

public sealed class ModelPricing
{
    public decimal InputPer1M { get; init; }

    public decimal? CachedInputPer1M { get; init; }

    public decimal OutputPer1M { get; init; }
}
