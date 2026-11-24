using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pizon.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261124064100_DiscordFeeds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FeedPosts",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Feed = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                MessageId = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                Number = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                Body = table.Column<string>(type: "TEXT", nullable: false),
                Author = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                EditedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FeedPosts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FeedStates",
            columns: table => new
            {
                Feed = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                LastSyncOk = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FeedStates", x => x.Feed);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FeedPosts_Feed_MessageId",
            table: "FeedPosts",
            columns: new[] { "Feed", "MessageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FeedPosts_Feed_Number",
            table: "FeedPosts",
            columns: new[] { "Feed", "Number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FeedPosts");

        migrationBuilder.DropTable(
            name: "FeedStates");
    }
}
