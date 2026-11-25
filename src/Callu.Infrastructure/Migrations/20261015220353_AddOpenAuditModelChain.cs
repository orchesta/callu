using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenAuditModelChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OamEvent",
                table: "AuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OamHash",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChainId",
                table: "AuditChainStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OamLastHash",
                table: "AuditChainStates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OamLastSequence",
                table: "AuditChainStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AuditChainLeaves",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditChainLeaves", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Sequence_PublicChainPending",
                table: "AuditLogs",
                column: "Sequence",
                filter: "\"Sequence\" IS NOT NULL AND \"OamHash\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditChainLeaves");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Sequence_PublicChainPending",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OamEvent",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OamHash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ChainId",
                table: "AuditChainStates");

            migrationBuilder.DropColumn(
                name: "OamLastHash",
                table: "AuditChainStates");

            migrationBuilder.DropColumn(
                name: "OamLastSequence",
                table: "AuditChainStates");
        }
    }
}
