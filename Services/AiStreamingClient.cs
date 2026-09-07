using NinjaTrader_Xen.Models;

namespace NinjaTrader_Xen.Services;

public sealed class AiStreamingClient(
    IEnumerable<IAiStreamingProvider> providers)
{
    private readonly IReadOnlyList<IAiStreamingProvider> _providers =
        providers.ToList();

    public bool IsConfigured(string model) =>
        FindProvider(model)?.IsConfigured ?? false;

    public bool SupportsImages(string model) =>
        FindProvider(model)?.SupportsImages(model) ?? false;

    public IAsyncEnumerable<AiStreamEvent> StreamAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string prompt,
        AiImage? image,
        int maximumOutputTokens,
        CancellationToken cancellationToken)
    {
        var provider = FindProvider(model)
            ?? throw new InvalidOperationException(
                $"No AI provider supports model '{model}'.");

        if (!provider.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{provider.DisplayName} is not configured.");
        }

        return provider.StreamAsync(
            model,
            systemPrompt,
            history,
            prompt,
            image,
            maximumOutputTokens,
            cancellationToken);
    }

    private IAiStreamingProvider? FindProvider(string model) =>
        _providers.FirstOrDefault(provider => provider.Supports(model));
}

public interface IAiStreamingProvider
{
    string DisplayName { get; }
    bool IsConfigured { get; }
    bool Supports(string model);
    bool SupportsImages(string model);

    IAsyncEnumerable<AiStreamEvent> StreamAsync(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string prompt,
        AiImage? image,
        int maximumOutputTokens,
        CancellationToken cancellationToken);
}

public sealed record AiStreamEvent(
    string? Delta,
    int InputTokens,
    int OutputTokens,
    bool Completed,
    string? StopReason = null,
    bool Incomplete = false);
