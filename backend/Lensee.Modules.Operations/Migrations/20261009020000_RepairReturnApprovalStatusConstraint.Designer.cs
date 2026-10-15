using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Lensee.Modules.Operations.Data;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20261009020000_RepairReturnApprovalStatusConstraint")]
partial class RepairReturnApprovalStatusConstraint
{
}
