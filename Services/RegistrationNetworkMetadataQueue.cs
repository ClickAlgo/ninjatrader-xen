using System.Net;
using System.Threading.Channels;

namespace NinjaTrader_Xen.Services;

public sealed record RegistrationNetworkMetadataWorkItem(
    int SubscriberId,
    IPAddress? RegistrationIp);

public interface IRegistrationNetworkMetadataQueue
{
    bool TryEnqueue(RegistrationNetworkMetadataWorkItem item);

    ValueTask<RegistrationNetworkMetadataWorkItem> DequeueAsync(
        CancellationToken cancellationToken);
}

public sealed class RegistrationNetworkMetadataQueue :
    IRegistrationNetworkMetadataQueue
{
    public const int Capacity = 1_024;

    private readonly Channel<RegistrationNetworkMetadataWorkItem> channel =
        Channel.CreateBounded<RegistrationNetworkMetadataWorkItem>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public bool TryEnqueue(RegistrationNetworkMetadataWorkItem item) =>
        channel.Writer.TryWrite(item);

    public ValueTask<RegistrationNetworkMetadataWorkItem> DequeueAsync(
        CancellationToken cancellationToken) =>
        channel.Reader.ReadAsync(cancellationToken);
}

public interface IRegistrationNetworkMetadataRecorder
{
    Task RecordRegistrationNetworkAsync(
        int subscriberId,
        IPAddress? remoteIp,
        CancellationToken cancellationToken);
}

public sealed class RegistrationNetworkMetadataWorker(
    IRegistrationNetworkMetadataQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<RegistrationNetworkMetadataWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            RegistrationNetworkMetadataWorkItem item;
            try
            {
                item = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Registration network metadata worker could not read its queue.");
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var recorder = scope.ServiceProvider
                    .GetRequiredService<IRegistrationNetworkMetadataRecorder>();
                await recorder.RecordRegistrationNetworkAsync(
                    item.SubscriberId,
                    item.RegistrationIp,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Registration network metadata failed for subscriber {SubscriberId}.",
                    item.SubscriberId);
            }
        }
    }
}
