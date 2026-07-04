using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLimboSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LimboSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<int>(type: "integer", nullable: false),
                    VersionName = table.Column<string>(type: "text", nullable: true),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeepAliveId = table.Column<int>(type: "integer", nullable: false),
                    TransferId = table.Column<int>(type: "integer", nullable: false),
                    BossBarId = table.Column<int>(type: "integer", nullable: true),
                    TitleTextId = table.Column<int>(type: "integer", nullable: true),
                    SubtitleId = table.Column<int>(type: "integer", nullable: true),
                    TitleTimesId = table.Column<int>(type: "integer", nullable: true),
                    SystemChatId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LimboSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LimboSnapshots_Protocol",
                table: "LimboSnapshots",
                column: "Protocol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LimboSnapshots");
        }
    }
}
