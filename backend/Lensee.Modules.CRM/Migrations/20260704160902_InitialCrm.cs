using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.CRM.Migrations
{
    /// <inheritdoc />
    public partial class InitialCrm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crm");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "merchants",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    business_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    contact_person_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone_numbers = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    business_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Active'::character varying"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("merchants_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "representatives",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone_numbers = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'External'::character varying"),
                    linked_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Active'::character varying"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("representatives_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_notes",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("merchant_notes_pkey", x => x.id);
                    table.ForeignKey(
                        name: "merchant_notes_merchant_id_fkey",
                        column: x => x.merchant_id,
                        principalSchema: "crm",
                        principalTable: "merchants",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_merchant_notes_merchant",
                schema: "crm",
                table: "merchant_notes",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "idx_merchants_status",
                schema: "crm",
                table: "merchants",
                column: "status",
                filter: "(is_deleted = false)");

            migrationBuilder.CreateIndex(
                name: "idx_representatives_status",
                schema: "crm",
                table: "representatives",
                column: "status",
                filter: "(is_deleted = false)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_notes",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "representatives",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "merchants",
                schema: "crm");
        }
    }
}
