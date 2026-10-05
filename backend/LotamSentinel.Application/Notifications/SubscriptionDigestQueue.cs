using System.Threading.Channels;

namespace LotamSentinel.Application.Notifications;

/// <summary>One "send this subscription's digest now" request, queued by
/// ProfileController.SendDigest (the Notification Settings tab's "Send
/// digest now" button) and drained one at a time by
/// SubscriptionDigestWorker.</summary>
public record SubscriptionDigestJob(long SubscriptionId);

/// <summary>In-memory FIFO queue for subscription-digest requests.
/// Deliberately a queue rather than Task.Run/fire-and-forget per
/// subscription: a click on "Send digest now" can enqueue anywhere from one
/// to dozens of jobs at once (one per subscription the user has), each of
/// which builds a report AND sends one email per recipient. Firing all of
/// that in parallel would open a burst of simultaneous SMTP connections,
/// which most providers (Gmail included) throttle or reject outright. A
/// single background reader draining this queue keeps every report build
/// and every email send strictly sequential - slower in total, but
/// reliable, and it keeps the HTTP request that enqueued the jobs fast
/// (the controller returns as soon as the jobs are queued, not once
/// they're all sent).
///
/// In-memory means queued jobs are lost if the API restarts before they're
/// processed - acceptable here since "Send digest now" is a manual,
/// re-triggerable action, not a durable audit record.</summary>
public class SubscriptionDigestQueue
{
    private readonly Channel<SubscriptionDigestJob> _channel =
        Channel.CreateUnbounded<SubscriptionDigestJob>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<SubscriptionDigestJob> Reader => _channel.Reader;

    public void Enqueue(SubscriptionDigestJob job) => _channel.Writer.TryWrite(job);
}
