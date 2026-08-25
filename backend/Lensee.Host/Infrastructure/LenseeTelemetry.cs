using System.Diagnostics.Metrics;

namespace Lensee.Host.Infrastructure;

public static class LenseeTelemetry
{
    public static readonly Meter Meter = new("Lensee.Host", "1.0.0");
    public static readonly Counter<long> CorrectionRequests = Meter.CreateCounter<long>("lensee.corrections.requests");
    public static readonly Counter<long> CorrectionFailures = Meter.CreateCounter<long>("lensee.corrections.failures");
    public static readonly Counter<long> OutboxDeadLetters = Meter.CreateCounter<long>("lensee.outbox.dead_letters");
    public static readonly Counter<long> OutboxReplays = Meter.CreateCounter<long>("lensee.outbox.replays");
}
