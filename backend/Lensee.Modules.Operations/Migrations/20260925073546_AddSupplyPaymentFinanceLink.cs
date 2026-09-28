using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyPaymentFinanceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "supply_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_supply_receiving_open_session",
                schema: "operations",
                table: "supply_receiving_sessions",
                column: "shipment_id",
                unique: true,
                filter: "(status = 'Draft')");

            migrationBuilder.CreateIndex(
                name: "idx_supply_payments_finance_log",
                schema: "operations",
                table: "supply_payments",
                column: "supply_finance_log_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_supply_receiving_open_session",
                schema: "operations",
                table: "supply_receiving_sessions");

            migrationBuilder.DropIndex(
                name: "idx_supply_payments_finance_log",
                schema: "operations",
                table: "supply_payments");

            migrationBuilder.DropColumn(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "supply_payments");
        }
    }
}
