using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VxPayBridge.API.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyAndWebhookSigning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ClientReference",
                table: "PaymentTransactions");

            migrationBuilder.AddColumn<string>(
                name: "WebhookSigningSecret",
                table: "ClientApps",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ClientAppID_ClientReference",
                table: "PaymentTransactions",
                columns: new[] { "ClientAppID", "ClientReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ClientAppID_ClientReference",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "WebhookSigningSecret",
                table: "ClientApps");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ClientReference",
                table: "PaymentTransactions",
                column: "ClientReference",
                unique: true);
        }
    }
}
