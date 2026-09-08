using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NinjaTrader_Xen.Services;

namespace NinjaTrader_Xen.Tests;

public sealed class RegistrationNetworkMetadataQueueTests
{
    [Fact]
    public async Task QueuePreservesSubscriberAndRegistrationIp()
    {
        var queue = new RegistrationNetworkMetadataQueue();
        var expected = new RegistrationNetworkMetadataWorkItem(
            417,
            IPAddress.Parse("68.32.16.41"));

        Assert.True(queue.TryEnqueue(expected));
        var actual = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(expected.SubscriberId, actual.SubscriberId);
        Assert.Equal(expected.RegistrationIp, actual.RegistrationIp);
    }

    [Fact]
    public void FullQueueRejectsImmediately()
    {
        var queue = new RegistrationNetworkMetadataQueue();
        for (var index = 0; index < RegistrationNetworkMetadataQueue.Capacity; index++)
        {
            Assert.True(queue.TryEnqueue(
                new RegistrationNetworkMetadataWorkItem(index, IPAddress.Loopback)));
        }

        var stopwatch = Stopwatch.StartNew();
        var accepted = queue.TryEnqueue(
            new RegistrationNetworkMetadataWorkItem(2_000, IPAddress.Loopback));
        stopwatch.Stop();

        Assert.False(accepted);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WorkerContinuesAfterRecorderFailure()
    {
        var queue = new RegistrationNetworkMetadataQueue();
        var recorder = new ThrowThenRecordRecorder();
        var services = new ServiceCollection()
            .AddScoped<IRegistrationNetworkMetadataRecorder>(_ => recorder)
            .BuildServiceProvider();
        var worker = new RegistrationNetworkMetadataWorker(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RegistrationNetworkMetadataWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        Assert.True(queue.TryEnqueue(new(1, IPAddress.Parse("192.0.2.1"))));
        Assert.True(queue.TryEnqueue(new(2, IPAddress.Parse("192.0.2.2"))));
        var processed = await recorder.Processed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);
        await services.DisposeAsync();

        Assert.Equal(2, processed.SubscriberId);
        Assert.Equal(IPAddress.Parse("192.0.2.2"), processed.RegistrationIp);
    }

    [Fact]
    public void RegistrationEnqueuesOnlyAfterCommitAndDoesNotCallRecorder()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "Endpoints", "AuthEndpoints.cs"));
        var commit = source.IndexOf("await transaction.CommitAsync();", StringComparison.Ordinal);
        var enqueue = source.IndexOf("registrationNetworkQueue.TryEnqueue", StringComparison.Ordinal);

        Assert.True(commit >= 0);
        Assert.True(enqueue > commit);
        Assert.DoesNotContain("RecordRegistrationNetworkAsync", source);
        Assert.Contains("queue was full; metadata skipped for subscriber", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NinjaTrader Xen.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class ThrowThenRecordRecorder : IRegistrationNetworkMetadataRecorder
    {
        private int calls;
        public TaskCompletionSource<RegistrationNetworkMetadataWorkItem> Processed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordRegistrationNetworkAsync(int subscriberId, IPAddress? remoteIp,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("Synthetic provider or storage failure.");
            Processed.TrySetResult(new(subscriberId, remoteIp));
            return Task.CompletedTask;
        }
    }
}
