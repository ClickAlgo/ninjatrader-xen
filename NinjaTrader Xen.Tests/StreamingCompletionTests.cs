using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class StreamingCompletionTests
{
    [Fact]
    public async Task Claude_DisablesThinkingSoCodeGetsTheOutputBudget()
    {
        var factory = new StubFactory("data: {\"type\":\"message_stop\"}\n");
        var client = new ClaudeStreamingClient(
            factory, new ConfigurationBuilder().Build());

        await foreach (var _ in client.StreamAsync(
            "claude-opus-5", "system", [], "prompt", null, 32000, default))
        {
        }

        using var payload = JsonDocument.Parse(Assert.IsType<string>(factory.LastRequestBody));
        Assert.Equal("disabled",
            payload.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(32000, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Theory]
    [InlineData("end_turn", false)]
    [InlineData("stop_sequence", false)]
    [InlineData("max_tokens", true)]
    [InlineData("refusal", true)]
    public async Task Claude_PreservesUsageAndStopReason(string reason, bool incomplete)
    {
        var stream = """
            data: {"type":"message_start","message":{"usage":{"input_tokens":120}}}
            data: {"type":"content_block_delta","delta":{"text":"partial code"}}
            """ + "\n" +
            $$$"""data: {"type":"message_delta","delta":{"stop_reason":"{{{reason}}}"},"usage":{"output_tokens":45}}""" +
            "\ndata: {\"type\":\"message_stop\"}\n";
        var events = await Read("claude", stream);
        Assert.Equal("partial code", events[0].Delta);
        AssertTerminal(events[^1], reason, incomplete, 120, 45);
    }

    [Fact]
    public async Task Claude_EofWithoutTerminalIsIncomplete()
    {
        var events = await Read("claude", "data: {}\n");
        AssertTerminal(Assert.Single(events), "stream_ended", true, 0, 0);
    }

    private static void AssertTerminal(AiStreamEvent item, string reason,
        bool incomplete, int input, int output)
    {
        Assert.True(item.Completed);
        Assert.Equal(reason, item.StopReason);
        Assert.Equal(incomplete, item.Incomplete);
        Assert.Equal(input, item.InputTokens);
        Assert.Equal(output, item.OutputTokens);
    }

    private static async Task<List<AiStreamEvent>> Read(string provider, string sse)
    {
        var factory = new StubFactory(sse);
        var config = new ConfigurationBuilder().Build();
        IAiStreamingProvider client = new ClaudeStreamingClient(factory, config);
        var result = new List<AiStreamEvent>();
        await foreach (var item in client.StreamAsync("test", "", [], "", null, 10000, default))
            result.Add(item);
        return result;
    }

    private sealed class StubFactory(string sse) : IHttpClientFactory
    {
        public string? LastRequestBody { get; private set; }

        public HttpClient CreateClient(string name) =>
            new(new StubHandler(sse, body => LastRequestBody = body))
            { BaseAddress = new Uri("https://example.invalid/") };
    }

    private sealed class StubHandler(string sse, Action<string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
