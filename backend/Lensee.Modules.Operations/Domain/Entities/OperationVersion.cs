using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class OperationVersion
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public int VersionNumber { get; set; }

    public string SnapshotData { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public Guid EditedBy { get; set; }

    public DateTime EditedAt { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;

    public virtual ICollection<OperationLog> OperationLogs { get; set; } = new List<OperationLog>();
}
