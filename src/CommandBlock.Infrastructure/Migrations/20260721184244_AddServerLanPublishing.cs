using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServerLanPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LanBindAddress",
                table: "ServerInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LanPort",
                table: "ServerInstances",
                type: "integer",
                nullable: true);

            // Every existing server is reached through the router today, so they must all backfill to true -
            // the scaffolded `false` default would silently take every server off the proxy on upgrade.
            migrationBuilder.AddColumn<bool>(
                name: "RoutedThroughProxy",
                table: "ServerInstances",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LanBindAddress",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "LanPort",
                table: "ServerInstances");

            migrationBuilder.DropColumn(
                name: "RoutedThroughProxy",
                table: "ServerInstances");
        }
    }
}
