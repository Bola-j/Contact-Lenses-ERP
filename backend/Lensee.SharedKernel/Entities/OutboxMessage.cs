using System;

namespace Lensee.SharedKernel.Data;

public partial class OutboxMessage
{
    public Guid Id { get; set; }

    public string EventType { get; set; } = null!;

    public int EventVersion { get; set; } = 1;

    public string? CorrelationId { get; set; }

    public string? CausationId { get; set; }

    public string Payload { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int Attempts { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}
