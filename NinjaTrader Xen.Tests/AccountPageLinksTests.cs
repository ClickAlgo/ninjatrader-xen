namespace NinjaTrader_Xen.Tests;

public sealed class AccountPageLinksTests
{
    [Fact]
    public void Account_ShowsOtherXenProductsAsExternalLinks()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "account.html"));

        Assert.Contains("Other Xen products", html);
        Assert.Contains("href=\"https://ai.clickalgo.com\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("href=\"https://tradingview.clickalgo.com\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
    }

    private static string GetProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NinjaTrader Xen.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the project root.");
    }
}
