using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookCaptureCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookCaptures_IntegrationId",
                table: "WebhookCaptures");

            migrationBuilder.DropIndex(
                name: "IX_WebhookCaptures_ServiceId",
                table: "WebhookCaptures");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookCaptures_IntegrationId_CapturedAt",
                table: "WebhookCaptures",
                columns: new[] { "IntegrationId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookCaptures_ServiceId_CapturedAt",
                table: "WebhookCaptures",
                columns: new[] { "ServiceId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookCaptures_IntegrationId_CapturedAt",
                table: "WebhookCaptures");

            migrationBuilder.DropIndex(
                name: "IX_WebhookCaptures_ServiceId_CapturedAt",
                table: "WebhookCaptures");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookCaptures_IntegrationId",
                table: "WebhookCaptures",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookCaptures_ServiceId",
                table: "WebhookCaptures",
                column: "ServiceId");
        }
    }
}
