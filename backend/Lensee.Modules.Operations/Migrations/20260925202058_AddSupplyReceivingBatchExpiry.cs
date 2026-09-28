using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyReceivingBatchExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiry_date",
                schema: "operations",
                table: "supply_receiving_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lot_number",
                schema: "operations",
                table: "supply_receiving_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_date",
                schema: "operations",
                table: "supply_receiving_lines");

            migrationBuilder.DropColumn(
                name: "lot_number",
                schema: "operations",
                table: "supply_receiving_lines");
        }
    }
}
