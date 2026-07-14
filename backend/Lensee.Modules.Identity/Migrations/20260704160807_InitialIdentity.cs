using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "roles_permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("roles_permissions_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    stock_delta_applied = table.Column<int>(type: "integer", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("audit_logs_pkey", x => x.id);
                    table.ForeignKey(
                        name: "audit_logs_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    replaced_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("refresh_tokens_pkey", x => x.id);
                    table.ForeignKey(
                        name: "refresh_tokens_replaced_by_fkey",
                        column: x => x.replaced_by,
                        principalSchema: "identity",
                        principalTable: "refresh_tokens",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "refresh_tokens_user_id_fkey",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_created_at",
                schema: "identity",
                table: "audit_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_entity",
                schema: "identity",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_logs_user",
                schema: "identity",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_hash",
                schema: "identity",
                table: "refresh_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_user",
                schema: "identity",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_replaced_by",
                schema: "identity",
                table: "refresh_tokens",
                column: "replaced_by");

            migrationBuilder.CreateIndex(
                name: "refresh_tokens_token_hash_key",
                schema: "identity",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_role_permission",
                schema: "identity",
                table: "roles_permissions",
                columns: new[] { "role", "permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_users_location",
                schema: "identity",
                table: "users",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "idx_users_role",
                schema: "identity",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "users_username_key",
                schema: "identity",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
