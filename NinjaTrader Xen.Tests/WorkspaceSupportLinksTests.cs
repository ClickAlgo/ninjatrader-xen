namespace NinjaTrader_Xen.Tests;

public sealed class WorkspaceSupportLinksTests
{
    [Fact]
    public void Workspace_LinksToNinjaTraderVideoGuidePlaylist()
    {
        var root = GetProjectRoot();
        var html = File.ReadAllText(Path.Combine(root, "wwwroot", "workspace.html"));

        Assert.Contains("https://www.youtube.com/playlist?list=PLJxGn6T5y7iE", html);
        Assert.Contains("<span>Video guides</span>", html);
        Assert.Contains("class=\"workspace-video-icon\"", html);
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
