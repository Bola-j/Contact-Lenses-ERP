using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalRepaymentCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawal_repayments_reverses_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "reverses_repayment_id",
                unique: true,
                filter: "(reverses_repayment_id is not null)");

            migrationBuilder.AddForeignKey(
                name: "FK_c_level_withdrawal_repayments_c_level_withdrawal_repayments~",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "reverses_repayment_id",
                principalSchema: "finance",
                principalTable: "c_level_withdrawal_repayments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_c_level_withdrawal_repayments_c_level_withdrawal_repayments~",
                schema: "finance",
                table: "c_level_withdrawal_repayments");

            migrationBuilder.DropIndex(
                name: "IX_c_level_withdrawal_repayments_reverses_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments");

            migrationBuilder.DropColumn(
                name: "correction_note",
                schema: "finance",
                table: "c_level_withdrawal_repayments");

            migrationBuilder.DropColumn(
                name: "replaced_by_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments");

            migrationBuilder.DropColumn(
                name: "reverses_repayment_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments");
        }
    }
}
