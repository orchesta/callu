using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationListeningCaptures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "WebhookCaptures",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "IntegrationId",
                table: "WebhookCaptures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ListeningMode",
                table: "Integrations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookCaptures_IntegrationId",
                table: "WebhookCaptures",
                column: "IntegrationId");

            migrationBuilder.AddForeignKey(
                name: "FK_WebhookCaptures_Integrations_IntegrationId",
                table: "WebhookCaptures",
                column: "IntegrationId",
                principalTable: "Integrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebhookCaptures_Integrations_IntegrationId",
                table: "WebhookCaptures");

            migrationBuilder.DropIndex(
                name: "IX_WebhookCaptures_IntegrationId",
                table: "WebhookCaptures");

            migrationBuilder.DropColumn(
                name: "IntegrationId",
                table: "WebhookCaptures");

            migrationBuilder.DropColumn(
                name: "ListeningMode",
                table: "Integrations");

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "WebhookCaptures",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
