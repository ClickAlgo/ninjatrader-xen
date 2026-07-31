using Microsoft.Extensions.Logging;
using NinjaTrader_Xen.Logging;

namespace NinjaTrader_Xen.Tests;

public sealed class RollingFileLoggerTests
{
    [Fact]
    public void DisabledLogging_DoesNotCreateDirectoryOrFile()
    {
        var directory = CreateTemporaryDirectoryPath();
        try
        {
            using var provider = CreateProvider(directory, enabled: false);
            provider.CreateLogger("Test").LogError("Failure");

            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void MinimumLevel_WritesErrorsButNotInformation()
    {
        var directory = CreateTemporaryDirectoryPath();
        try
        {
            using var provider = CreateProvider(directory, enabled: true);
            var logger = provider.CreateLogger("RegressionTest");
            logger.LogInformation("This must not be written");
            logger.LogError("Expected test failure");

            var file = Assert.Single(Directory.GetFiles(directory, "*.log"));
            var content = File.ReadAllText(file);
            Assert.Contains("Expected test failure", content);
            Assert.DoesNotContain("This must not be written", content);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Redaction_RemovesCommonCredentials()
    {
        const string apiKey = "sk-proj-abcdefghijklmnop123456";
        const string bearer = "Bearer abc.def.ghi";
        const string password = "Password=TopSecret123";

        var result = RollingFileLoggerProvider.RedactSecrets(
            $"{apiKey} {bearer} {password}");

        Assert.DoesNotContain(apiKey, result);
        Assert.DoesNotContain("abc.def.ghi", result);
        Assert.DoesNotContain("TopSecret123", result);
        Assert.Contains("[REDACTED", result);
    }

    [Fact]
    public void Retention_RemovesOldestFiles()
    {
        var directory = CreateTemporaryDirectoryPath();
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "TestXen-20200101-p1.log");
            var second = Path.Combine(directory, "TestXen-20200102-p1.log");
            File.WriteAllText(first, "oldest");
            File.WriteAllText(second, "older");
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddDays(-1));

            using var provider = CreateProvider(
                directory,
                enabled: true,
                retainedFileCount: 2);
            provider.CreateLogger("Test").LogError("Current failure");

            var files = Directory.GetFiles(directory, "*.log");
            Assert.Equal(2, files.Length);
            Assert.DoesNotContain(first, files);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static RollingFileLoggerProvider CreateProvider(
        string directory,
        bool enabled,
        int retainedFileCount = 30) =>
        new(new FileLoggingOptions
        {
            Enabled = enabled,
            Path = Path.Combine(directory, "TestXen-.log"),
            MinimumLevel = "Error",
            FileSizeLimitMb = 1,
            RetainedFileCount = retainedFileCount
        });

    private static string CreateTemporaryDirectoryPath() =>
        Path.Combine(
            Path.GetTempPath(),
            "ninjatrader-xen-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
