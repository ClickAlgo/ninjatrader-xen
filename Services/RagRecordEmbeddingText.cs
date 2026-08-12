namespace NinjaTrader_Xen.Services;

/// <summary>
/// Produces the metadata-only text used to create a stored RAG record embedding.
/// Reference code is intentionally excluded so it can be supplied only after a
/// record has been selected by semantic retrieval.
/// </summary>
public static class RagRecordEmbeddingText
{
    public static string Build(
        string? title,
        string? description,
        string? tags) =>
        $"Title: {title ?? ""}\n" +
        $"Description: {description ?? ""}\n" +
        $"Tags: {tags ?? ""}";
}
