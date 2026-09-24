using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceRequestIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                schema: "finance",
                table: "finance_transfers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_finance_transfers_client_request_id",
                schema: "finance",
                table: "finance_transfers",
                column: "client_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawal_repayments_client_request_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "client_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_finance_transfers_client_request_id",
                schema: "finance",
                table: "finance_transfers");

            migrationBuilder.DropIndex(
                name: "IX_c_level_withdrawal_repayments_client_request_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                schema: "finance",
                table: "finance_transfers");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments");
        }
    }
}
