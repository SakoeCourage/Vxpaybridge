using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VxPayBridge.API.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "WebhookEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccessCode",
                table: "PaymentTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationUrl",
                table: "PaymentTransactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventType",
                table: "WebhookEvents");

            migrationBuilder.DropColumn(
                name: "AccessCode",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "AuthorizationUrl",
                table: "PaymentTransactions");
        }
    }
}
