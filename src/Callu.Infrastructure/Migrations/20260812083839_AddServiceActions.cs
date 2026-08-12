using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActionId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionName",
                table: "WebhookDeliveries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HeadersJson = table.Column<string>(type: "text", nullable: true),
                    PayloadTemplate = table.Column<string>(type: "text", nullable: true),
                    Secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SignatureHeader = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceActions_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_ActionId",
                table: "WebhookDeliveries",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceActions_ServiceId",
                table: "ServiceActions",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceActions_ServiceId_Name",
                table: "ServiceActions",
                columns: new[] { "ServiceId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookDeliveries_ServiceActions_ActionId",
                table: "WebhookDeliveries",
                column: "ActionId",
                principalTable: "ServiceActions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookDeliveries_ServiceActions_ActionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "ServiceActions");

            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_ActionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ActionId",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "ActionName",
                table: "WebhookDeliveries");
        }
    }
}
