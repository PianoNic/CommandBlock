using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddServerInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerType = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    PreviousVersion = table.Column<string>(type: "TEXT", nullable: true),
                    ModpackRef = table.Column<string>(type: "TEXT", nullable: true),
                    Memory = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: true),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: true),
                    IsManaged = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsConfigManaged = table.Column<bool>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerInstances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerInstances_ContainerName",
                table: "ServerInstances",
                column: "ContainerName",
                unique: true,
                filter: "\"ContainerName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServerInstances_Hostname",
                table: "ServerInstances",
                column: "Hostname",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerInstances");
        }
    }
}
