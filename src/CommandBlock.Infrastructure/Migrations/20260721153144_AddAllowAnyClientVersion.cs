using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowAnyClientVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAnyClientVersion",
                table: "ServerInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAnyClientVersion",
                table: "ServerInstances");
        }
    }
}
