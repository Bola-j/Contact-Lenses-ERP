using System;
using System.Collections.Generic;

namespace Lensee.Modules.Reporting.Data;

public partial class ExportLog
{
    public Guid Id { get; set; }

    public string ReportType { get; set; } = null!;

    public Guid? RequestedBy { get; set; }

    public string? GeneratedUrl { get; set; }

    public DateTime CreatedAt { get; set; }
}
