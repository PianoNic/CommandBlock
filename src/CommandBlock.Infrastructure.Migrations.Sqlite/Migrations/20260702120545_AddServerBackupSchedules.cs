using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddServerBackupSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastStatus = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupSchedules");
        }
    }
}
