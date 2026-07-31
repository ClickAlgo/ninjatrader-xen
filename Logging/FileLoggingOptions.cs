namespace NinjaTrader_Xen.Logging;

public sealed class FileLoggingOptions
{
    public const string SectionName = "FileLogging";

    public bool Enabled { get; init; }

    public string Path { get; init; } =
        @"C:\XenLogs\NinjaTraderXen-.log";

    public string MinimumLevel { get; init; } = "Error";

    public int FileSizeLimitMb { get; init; } = 20;

    public int RetainedFileCount { get; init; } = 30;
}
