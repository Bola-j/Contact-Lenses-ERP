using System;
using System.Collections.Generic;

namespace Lensee.Modules.Notifications.Data;

public partial class NotificationLog
{
    public Guid Id { get; set; }

    public string AlertType { get; set; } = null!;

    public string Message { get; set; } = null!;

    public Guid? ReferenceId { get; set; }

    public string? ReferenceType { get; set; }

    public string? ReferenceCode { get; set; }

    public string? ReferenceTitle { get; set; }

    public string? ReferenceContextJson { get; set; }

    public string NotificationNumber { get; set; } = null!;

    public Guid? TargetUserId { get; set; }

    public string? TargetRole { get; set; }

    public string Channel { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
