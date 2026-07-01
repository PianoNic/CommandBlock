using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommandBlock.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSecretsVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Secrets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Secrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Nonce = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Tag = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Secrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Secrets_Name",
                table: "Secrets",
                column: "Name",
                unique: true);
        }
    }
}
