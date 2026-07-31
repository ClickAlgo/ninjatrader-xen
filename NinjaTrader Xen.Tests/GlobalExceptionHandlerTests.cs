using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using NinjaTrader_Xen.Infrastructure;
using System.Text.Json;

namespace NinjaTrader_Xen.Tests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task ApiFailure_ReturnsSafeJsonWithoutExceptionDetails()
    {
        var context = CreateContext("/api/projects");
        var exception = new InvalidOperationException(
            "Sensitive SQL connection details");
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        var body = await ReadBody(context);
        using var json = JsonDocument.Parse(body);
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal("SERVER_ERROR",
            json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(exception.Message, body);
    }

    [Fact]
    public async Task PageFailure_ReturnsGenericPlainText()
    {
        var context = CreateContext("/workspace.html");
        var exception = new InvalidOperationException("Sensitive details");

        var handled = await CreateHandler().TryHandleAsync(
            context,
            exception,
            CancellationToken.None);

        var body = await ReadBody(context);
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.StartsWith("text/plain", context.Response.ContentType);
        Assert.Equal("An unexpected error occurred.", body);
        Assert.DoesNotContain(exception.Message, body);
    }

    [Fact]
    public async Task StartedStreamingResponse_IsNotRewritten()
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var context = new DefaultHttpContext(features);
        context.Request.Path = "/api/chat/stream";

        var handled = await CreateHandler().TryHandleAsync(
            context,
            new InvalidOperationException("Stream failed"),
            CancellationToken.None);

        Assert.False(handled);
    }

    private static GlobalExceptionHandler CreateHandler() =>
        new(NullLogger<GlobalExceptionHandler>.Instance);

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
