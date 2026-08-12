using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManualActionInFlightIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_ManualInFlight",
                table: "WebhookDeliveries",
                columns: new[] { "IncidentId", "AckType" },
                unique: true,
                filter: "\"Status\" = 'Pending' AND \"ActionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookDeliveries_ManualInFlight",
                table: "WebhookDeliveries");
        }
    }
}
