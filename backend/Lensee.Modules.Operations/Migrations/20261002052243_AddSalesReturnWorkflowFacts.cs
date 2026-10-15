using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReturnWorkflowFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AddColumn<bool>(
                name: "buyer_contact_verified",
                schema: "operations",
                table: "operation_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "client_request_id",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cod_charge",
                schema: "operations",
                table: "operation_logs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "cod_charge_override_reason",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee",
                schema: "operations",
                table: "operation_logs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "external_payment_provider",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_payment_reference",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "external_payment_verified",
                schema: "operations",
                table: "operation_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_exception_reason",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "return_exception_value",
                schema: "operations",
                table: "operation_logs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "return_normal_value",
                schema: "operations",
                table: "operation_logs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "return_verbal_authorizer",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "exception_quantity",
                schema: "operations",
                table: "operation_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "exception_value",
                schema: "operations",
                table: "operation_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "normal_quantity",
                schema: "operations",
                table: "operation_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "normal_value",
                schema: "operations",
                table: "operation_lines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "pricing_mode",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("update operations.operation_lines set normal_quantity = quantity, normal_value = line_total where normal_quantity = 0 and exception_quantity = 0;");

            migrationBuilder.CreateIndex(
                name: "uq_operation_client_request",
                schema: "operations",
                table: "operation_logs",
                column: "client_request_id",
                unique: true,
                filter: "(client_request_id is not null)");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs",
                sql: "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled','Dispatched','Delivered','FailedDelivery')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_pricing_mode",
                schema: "operations",
                table: "operation_lines",
                sql: "pricing_mode is null or pricing_mode in ('FullPrice','Promotion50','PromotionFree')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_return_split",
                schema: "operations",
                table: "operation_lines",
                sql: "normal_quantity >= 0 and exception_quantity >= 0 and normal_quantity + exception_quantity = quantity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_operation_client_request",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_pricing_mode",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_return_split",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "buyer_contact_verified",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "cod_charge",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "cod_charge_override_reason",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "delivery_fee",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "external_payment_provider",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "external_payment_reference",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "external_payment_verified",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "payment_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_exception_reason",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_exception_value",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_normal_value",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_verbal_authorizer",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "exception_quantity",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "exception_value",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "normal_quantity",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "normal_value",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "pricing_mode",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs",
                sql: "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled')");
        }
    }
}
