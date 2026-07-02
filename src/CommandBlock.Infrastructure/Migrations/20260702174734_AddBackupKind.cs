using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "BackupEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "BackupEntries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BackupEntries");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "BackupEntries");
        }
    }
}
