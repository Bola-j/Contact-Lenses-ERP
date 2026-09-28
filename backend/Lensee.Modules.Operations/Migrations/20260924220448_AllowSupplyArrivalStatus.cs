using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AllowSupplyArrivalStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.AddCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments",
                sql: "status in ('Draft','Arrived','PartiallyReceived','Received','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.AddCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments",
                sql: "status in ('Draft','Received','Cancelled')");
        }
    }
}
