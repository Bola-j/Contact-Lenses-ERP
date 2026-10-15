using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Reporting.Migrations;

public partial class AddDocumentExportJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "document_export_jobs",
            schema: "reporting",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                document_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                requested_by_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                row_count = table.Column<int>(type: "integer", nullable: false),
                snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                content = table.Column<byte[]>(type: "bytea", nullable: true),
                content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("document_export_jobs_pkey", x => x.id));

        migrationBuilder.CreateIndex(name: "ix_document_export_jobs_queue", schema: "reporting", table: "document_export_jobs", columns: new[] { "status", "created_at" });
        migrationBuilder.CreateIndex(name: "ux_document_export_jobs_requester_idempotency", schema: "reporting", table: "document_export_jobs", columns: new[] { "requested_by", "idempotency_key" }, unique: true, filter: "idempotency_key is not null");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "document_export_jobs", schema: "reporting");
}
