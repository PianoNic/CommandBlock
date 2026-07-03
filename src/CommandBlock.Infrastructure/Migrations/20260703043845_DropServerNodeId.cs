using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropServerNodeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "ServerInstances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NodeId",
                table: "ServerInstances",
                type: "uuid",
                nullable: true);
        }
    }
}
