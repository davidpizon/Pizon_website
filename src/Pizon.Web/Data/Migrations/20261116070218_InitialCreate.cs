using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pizon.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261116070218_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DiscordId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DiscordName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                Handle = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                HandleKey = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Banned = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Messages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ParentId = table.Column<long>(type: "INTEGER", nullable: true),
                UserId = table.Column<long>(type: "INTEGER", nullable: false),
                Body = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_Messages_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Scores",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Door = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                UserId = table.Column<long>(type: "INTEGER", nullable: false),
                Day = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                Value = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Scores", x => x.Id);
                table.ForeignKey(
                    name: "FK_Scores_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SysopPages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<long>(type: "INTEGER", nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                Read = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SysopPages", x => x.Id);
                table.ForeignKey(
                    name: "FK_SysopPages_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Messages_Status_PostedAt",
            table: "Messages",
            columns: new[] { "Status", "PostedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Messages_UserId",
            table: "Messages",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Scores_Door_UserId_Day",
            table: "Scores",
            columns: new[] { "Door", "UserId", "Day" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Scores_UserId",
            table: "Scores",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_SysopPages_UserId",
            table: "SysopPages",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_DiscordId",
            table: "Users",
            column: "DiscordId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_HandleKey",
            table: "Users",
            column: "HandleKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Messages");

        migrationBuilder.DropTable(
            name: "Scores");

        migrationBuilder.DropTable(
            name: "SysopPages");

        migrationBuilder.DropTable(
            name: "Users");
    }
}
