using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JavaVersion",
                table: "ServerInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JvmArgs",
                table: "ServerInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAikarFlags",
                table: "ServerInstances",
                type: "INTEGER",
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
