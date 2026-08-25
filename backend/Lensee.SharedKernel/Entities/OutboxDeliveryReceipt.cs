using System;

namespace Lensee.SharedKernel.Data;

public partial class OutboxDeliveryReceipt
{
    public Guid Id { get; set; }

    public Guid OutboxMessageId { get; set; }

    public string HandlerName { get; set; } = null!;

    public DateTime ProcessedAt { get; set; }

    public virtual OutboxMessage OutboxMessage { get; set; } = null!;
}
