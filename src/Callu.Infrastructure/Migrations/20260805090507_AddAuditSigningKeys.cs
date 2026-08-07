using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditSigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedPrivateKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditSigningKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditSigningKeys_IsDeleted",
                table: "AuditSigningKeys",
                column: "IsDeleted",
                unique: true,
                filter: "\"RetiredAt\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AuditSigningKeys_KeyId",
                table: "AuditSigningKeys",
                column: "KeyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditSigningKeys");
        }
    }
}
