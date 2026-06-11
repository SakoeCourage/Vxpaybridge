using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VxPayBridge.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAndWithdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudID",
                table: "PaymentTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AudType",
                table: "PaymentTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "PaymentTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientAppID = table.Column<Guid>(type: "uuid", nullable: false),
                    AudType = table.Column<string>(type: "text", nullable: false),
                    AudID = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    PendingBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_ClientApps_ClientAppID",
                        column: x => x.ClientAppID,
                        principalTable: "ClientApps",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientAppID = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientReference = table.Column<string>(type: "text", nullable: false),
                    AudType = table.Column<string>(type: "text", nullable: false),
                    AudID = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ProviderCode = table.Column<string>(type: "text", nullable: false),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    AccountNumber = table.Column<string>(type: "text", nullable: false),
                    AccountName = table.Column<string>(type: "text", nullable: false),
                    RecipientCode = table.Column<string>(type: "text", nullable: true),
                    TransferCode = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.ID);
                    table.ForeignKey(
                        name: "FK_Withdrawals_ClientApps_ClientAppID",
                        column: x => x.ClientAppID,
                        principalTable: "ClientApps",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Withdrawals_LedgerAccounts_LedgerAccountID",
                        column: x => x.LedgerAccountID,
                        principalTable: "LedgerAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientAppID = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountID = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentTransactionID = table.Column<Guid>(type: "uuid", nullable: true),
                    WithdrawalID = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_ClientApps_ClientAppID",
                        column: x => x.ClientAppID,
                        principalTable: "ClientApps",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerAccounts_LedgerAccountID",
                        column: x => x.LedgerAccountID,
                        principalTable: "LedgerAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_PaymentTransactions_PaymentTransactionID",
                        column: x => x.PaymentTransactionID,
                        principalTable: "PaymentTransactions",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Withdrawals_WithdrawalID",
                        column: x => x.WithdrawalID,
                        principalTable: "Withdrawals",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ClientAppID_AudType_AudID",
                table: "PaymentTransactions",
                columns: new[] { "ClientAppID", "AudType", "AudID" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_ClientAppID_AudType_AudID_Currency",
                table: "LedgerAccounts",
                columns: new[] { "ClientAppID", "AudType", "AudID", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ClientAppID_Reference",
                table: "LedgerEntries",
                columns: new[] { "ClientAppID", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_LedgerAccountID",
                table: "LedgerEntries",
                column: "LedgerAccountID");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentTransactionID",
                table: "LedgerEntries",
                column: "PaymentTransactionID");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_WithdrawalID",
                table: "LedgerEntries",
                column: "WithdrawalID");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ClientAppID_AudType_AudID",
                table: "Withdrawals",
                columns: new[] { "ClientAppID", "AudType", "AudID" });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ClientAppID_ClientReference",
                table: "Withdrawals",
                columns: new[] { "ClientAppID", "ClientReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_LedgerAccountID",
                table: "Withdrawals",
                column: "LedgerAccountID");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_TransferCode",
                table: "Withdrawals",
                column: "TransferCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "Withdrawals");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_ClientAppID_AudType_AudID",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "AudID",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "AudType",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "PaymentTransactions");
        }
    }
}
