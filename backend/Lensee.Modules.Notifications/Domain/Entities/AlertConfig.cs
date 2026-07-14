using System;
using System.Collections.Generic;

namespace Lensee.Modules.Notifications.Data;

public partial class AlertConfig
{
    public Guid Id { get; set; }

    public string AlertType { get; set; } = null!;

    public int? ThresholdValue { get; set; }

    public string? ThresholdUnit { get; set; }

    public bool IsActive { get; set; }
}
