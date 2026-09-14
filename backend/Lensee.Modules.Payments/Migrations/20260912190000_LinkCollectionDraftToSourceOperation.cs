using Microsoft.EntityFrameworkCore.Migrations;
using Lensee.Modules.Payments.Data;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

/// <summary>Links automatically created collection work to its originating operation.</summary>
public partial class LinkCollectionDraftToSourceOperation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "source_operation_id",
            schema: "payments",
            table: "merchant_account_collection_drafts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_merchant_account_collection_drafts_source_operation_id",
            schema: "payments",
            table: "merchant_account_collection_drafts",
            column: "source_operation_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_merchant_account_collection_drafts_source_operation_id",
            schema: "payments",
            table: "merchant_account_collection_drafts");

        migrationBuilder.DropColumn(
            name: "source_operation_id",
            schema: "payments",
            table: "merchant_account_collection_drafts");
    }
}
