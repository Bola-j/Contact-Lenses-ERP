using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

public partial class AddReplenishmentRunsAndAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "automation_type", table: "operation_logs", schema: "operations", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.CreateTable(name: "replenishment_runs", schema: "operations", columns: table => new
        {
            id = table.Column<Guid>("uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
            run_key = table.Column<string>("character varying(40)", maxLength: 40, nullable: false),
            cairo_date = table.Column<DateOnly>("date", nullable: false),
            trigger = table.Column<string>("character varying(20)", maxLength: 20, nullable: false),
            status = table.Column<string>("character varying(20)", maxLength: 20, nullable: false),
            started_at = table.Column<DateTime>("timestamp without time zone", nullable: false),
            completed_at = table.Column<DateTime>("timestamp without time zone", nullable: true),
            created_operations = table.Column<int>("integer", nullable: false),
            uncovered_quantity = table.Column<int>("integer", nullable: false)
        }, constraints: table => table.PrimaryKey("replenishment_runs_pkey", x => x.id));
        migrationBuilder.CreateIndex("uq_replenishment_runs_run_key", "replenishment_runs", "run_key", "operations", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("replenishment_runs", "operations");
        migrationBuilder.DropColumn(name: "automation_type", table: "operation_logs", schema: "operations");
    }
}
