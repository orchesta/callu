using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HeadSequence = table.Column<long>(type: "bigint", nullable: false),
                    HeadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventCount = table.Column<long>(type: "bigint", nullable: false),
                    RootHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnchorType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnchorReference = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Document = table.Column<string>(type: "text", nullable: false),
                    RootDocument = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditCheckpoints_Status_HeadSequence",
                table: "AuditCheckpoints",
                columns: new[] { "Status", "HeadSequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditCheckpoints");
        }
    }
}
