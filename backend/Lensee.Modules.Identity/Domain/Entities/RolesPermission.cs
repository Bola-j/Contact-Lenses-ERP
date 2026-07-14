using System;
using System.Collections.Generic;

namespace Lensee.Modules.Identity.Data;

public partial class RolesPermission
{
    public Guid Id { get; set; }

    public string Role { get; set; } = null!;

    public string Permission { get; set; } = null!;
}
