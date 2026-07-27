namespace NinjaTrader_Xen.Options;

public static class PlatformIds
{
    public const int CTrader = 1;
    public const int NinjaTrader = 2;
}

public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
