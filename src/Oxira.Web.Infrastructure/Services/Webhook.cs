using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oxira.Web.Application.Common;

namespace Oxira.Web.Infrastructure.Services;

/// <summary>Team events (lead, order_paid, ticket) posted as {event, at, data} to Site:NotifyWebhook when it is set.</summary>
public class WebhookQueue(IOptions<SiteOptions> site) : IOutboundWebhook
{
    private readonly Channel<(string Event, object Data)> _channel = Channel.CreateBounded<(string, object)>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    public ChannelReader<(string Event, object Data)> Reader => _channel.Reader;
    public void Enqueue(string evt, object data)
    {
        if (!string.IsNullOrWhiteSpace(site.Value.NotifyWebhook)) _channel.Writer.TryWrite((evt, data));
    }
}

public class WebhookWorker(WebhookQueue queue, IHttpClientFactory factory, IOptions<SiteOptions> site, ILogger<WebhookWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var (evt, data) in queue.Reader.ReadAllAsync(stop))
        {
            try
            {
                using var res = await factory.CreateClient("webhook").PostAsJsonAsync(site.Value.NotifyWebhook, new { @event = evt, at = DateTimeOffset.UtcNow, data }, stop);
                if (!res.IsSuccessStatusCode) log.LogWarning("Webhook {Event}: {Status}", evt, (int)res.StatusCode);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Webhook {Event} failed", evt); }
        }
    }
}
