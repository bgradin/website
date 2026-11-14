using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Gradinware.Migrations
{
    public partial class AddResetTokenExpiry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ResetTokens",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "ResetTokens",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "ResetTokens",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
            migrationBuilder.DropColumn(name: "ExpiresAt", table: "ResetTokens");
        }
    }
}
