using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogCorrelationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpanId",
                table: "AuditLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceId",
                table: "AuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SpanId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TraceId",
                table: "AuditLogs");
        }
    }
}
