using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnApprovalDecisionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_rejection_reason",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_submission_reason",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "return_submitted_at",
                schema: "operations",
                table: "operation_logs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_submitted_by",
                schema: "operations",
                table: "operation_logs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_rejection_reason",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_submission_reason",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_submitted_at",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_submitted_by",
                schema: "operations",
                table: "operation_logs");
        }
    }
}
