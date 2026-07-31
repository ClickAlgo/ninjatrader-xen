using NinjaTrader_Xen.Memory;
using NinjaTrader_Xen.Models;

namespace NinjaTrader_Xen.Tests;

public sealed class ProjectMemoryTests
{
    [Fact]
    public void CompressHistory_RetainsOnlyLatestFiveCompletedTurns()
    {
        var history = Enumerable.Range(1, 7)
            .SelectMany(index => new[]
            {
                new ChatTurn("user", $"Request {index}"),
                new ChatTurn("assistant", $"Response {index}")
            })
            .ToList();

        var turns = SqlProjectMemoryStore.CompressHistory(
            "build-strategy",
            history,
            5);

        Assert.Equal(5, turns.Count);
        Assert.Equal("Request 3", turns[0].UserSummary);
        Assert.Equal("Request 7", turns[^1].UserSummary);
    }

    [Theory]
    [InlineData("Repair the latest complete NinjaScript source so it passes the NinjaTrader build check.")]
    [InlineData("Repair the latest complete NinjaScript source to address only the missing requirements.")]
    public void CompressHistory_ExcludesAutomatedRepairTurns(string prompt)
    {
        var history = new[]
        {
            new ChatTurn("user", "Build an RSI strategy"),
            new ChatTurn("assistant", "Created the initial strategy."),
            new ChatTurn("user", prompt),
            new ChatTurn("assistant", "Compiler repair completed.")
        };

        var turns = SqlProjectMemoryStore.CompressHistory(
            "build-strategy",
            history,
            5);

        var turn = Assert.Single(turns);
        Assert.Equal("Build an RSI strategy", turn.UserSummary);
    }

    [Fact]
    public void CompressHistory_RemovesCodeAndPresentationNoise()
    {
        var history = new[]
        {
            new ChatTurn("user", "Use this code:\n```csharp\nclass OldCode {}\n```"),
            new ChatTurn(
                "assistant",
                "## Overview\nImplemented the requested strategy.\n" +
                "```csharp\nclass NewCode {}\n```\n## Next step\nCompile it.")
        };

        var turn = Assert.Single(SqlProjectMemoryStore.CompressHistory(
            "build-strategy",
            history,
            5));

        Assert.Contains("[code omitted]", turn.UserSummary);
        Assert.DoesNotContain("OldCode", turn.UserSummary);
        Assert.DoesNotContain("NewCode", turn.AssistantSummary);
        Assert.DoesNotContain("Next step", turn.AssistantSummary);
        Assert.True(turn.GeneratedCode);
    }

    [Fact]
    public void ExtractLatestCode_ReturnsNewestCodeBlock()
    {
        var history = new[]
        {
            new ChatTurn("assistant", "```csharp\nclass VersionOne {}\n```"),
            new ChatTurn("user", "Add a stop loss"),
            new ChatTurn("assistant", "```cs\nclass VersionTwo {}\n```")
        };

        var code = SqlProjectMemoryStore.ExtractLatestCode(history);

        Assert.Equal("class VersionTwo {}", code);
    }

    [Fact]
    public void CurrentImplementationBlock_DeclaresCodeAuthoritative()
    {
        var block = PromptContextBuilder.BuildCurrentImplementationBlock(
            "class CurrentStrategy {}");

        Assert.Contains("CURRENT NINJASCRIPT IMPLEMENTATION START", block);
        Assert.Contains("class CurrentStrategy {}", block);
        Assert.Contains("authoritative current implementation", block);
    }
}
