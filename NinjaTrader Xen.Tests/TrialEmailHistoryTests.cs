namespace NinjaTrader_Xen.Tests;

public sealed class TrialEmailHistoryTests
{
    [Fact]
    public void EmailHistoryCoversEveryPlatformAndSpentOrExpiredTrials()
    {
        var source = ReadSource("Services", "FreeTrialService.cs");
        var start = source.IndexOf("SELECT CASE WHEN EXISTS", StringComparison.Ordinal);
        var end = source.IndexOf("THEN 1 ELSE 0 END;", start, StringComparison.Ordinal);
        var query = source[start..end];
        Assert.Contains("previous.Email = @Email", query);
        Assert.Contains("previous.TrialGranted = 1", query);
        Assert.Contains("credit.SubscriberId = previous.SubscriberId", query);
        Assert.Contains("credit.CreditType = 'Trial'", query);
        Assert.Contains("grantHistory.SubscriberId = previous.SubscriberId", query);
        Assert.DoesNotContain("PlatformId", query);
        Assert.DoesNotContain("CreditRemainingGbp", query);
        Assert.DoesNotContain("ExpiresUtc", query);
        Assert.DoesNotContain("Status", query);
        Assert.Contains("UPDLOCK, HOLDLOCK", query);
        Assert.True(start < source.IndexOf("INSERT INTO dbo.FreeTrialGrants", StringComparison.Ordinal));
    }

    [Fact]
    public void EmailDenialIsDurableAndPassedThroughSuccessfulLogin()
    {
        var service = ReadSource("Services", "FreeTrialService.cs");
        Assert.Contains("trialDenialReason == \"EmailTrialAlreadyClaimed\"", service);
        Assert.Contains("\"EmailTrialAlreadyClaimed\", cancellationToken", service);
        Assert.Contains("freeTrialAlreadyClaimed = trial.AlreadyClaimed", ReadSource("Endpoints", "AuthEndpoints.cs"));
        Assert.Contains("Your Xen free trial has already been claimed.", ReadSource("wwwroot", "js", "workspace.js"));
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
                return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }
}
