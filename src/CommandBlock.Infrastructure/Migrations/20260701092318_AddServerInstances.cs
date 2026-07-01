using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerType = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: true),
                    PreviousVersion = table.Column<string>(type: "text", nullable: true),
                    ModpackRef = table.Column<string>(type: "text", nullable: true),
                    Memory = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Hostname = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    ContainerName = table.Column<string>(type: "text", nullable: true),
                    ContainerId = table.Column<string>(type: "text", nullable: true),
                    IsManaged = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsConfigManaged = table.Column<bool>(type: "boolean", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
