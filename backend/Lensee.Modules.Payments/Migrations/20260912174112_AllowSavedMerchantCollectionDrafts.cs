using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AllowSavedMerchantCollectionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_merchant_collection_draft_status",
                schema: "payments",
                table: "merchant_account_collection_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_collection_draft_status",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                sql: "status in ('Draft','PendingAdminReview','Confirmed','Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_merchant_collection_draft_status",
                schema: "payments",
                table: "merchant_account_collection_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_collection_draft_status",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                sql: "status in ('PendingAdminReview','Confirmed','Rejected')");
        }
    }
}
