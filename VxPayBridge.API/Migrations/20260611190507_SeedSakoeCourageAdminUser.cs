using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VxPayBridge.API.Migrations
{
    /// <inheritdoc />
    public partial class SeedSakoeCourageAdminUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppUsers",
                columns: new[] { "ID", "CreatedAt", "Email", "IsActive", "PasswordHash", "TelephoneNumber", "UpdatedAt", "UserName" },
                values: new object[] { new Guid("45db66dc-f9ef-47e3-bf08-751d946c07ab"), new DateTime(2026, 6, 11, 0, 0, 0, 0, DateTimeKind.Utc), "akorlicourage@gail.com", true, "40f66729a9551d5fedb4fff19d6416517cc49873e98b848c5b283fd6a38b9b52", "0203843143", null, "Sakoe Courage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppUsers",
                keyColumn: "ID",
                keyValue: new Guid("45db66dc-f9ef-47e3-bf08-751d946c07ab"));
        }
    }
}
