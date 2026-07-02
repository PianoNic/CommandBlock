using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServerRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraEnv",
                table: "ServerInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JavaVersion",
                table: "ServerInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JvmArgs",
                table: "ServerInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAikarFlags",
                table: "ServerInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraEnv",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "JavaVersion",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "JvmArgs",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "UseAikarFlags",
                table: "ServerInstances");
        }
    }
}
