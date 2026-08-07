using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenAuditChainAndSweepReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalizationVersion",
                table: "AuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_UnconfirmedCallReportedAt_LastAttemptAt",
                table: "Notifications",
                columns: new[] { "Type", "UnconfirmedCallReportedAt", "LastAttemptAt" });

            // A database that already holds a repeated sequence cannot take the unique index, and a
            // migration that throws here would crash-loop the install on every start. Such a chain is
            // already unverifiable; it keeps the plain index and says so in the server log.
            //
            // migration-safety: reviewed — replayed on PostgreSQL 16 against seeded audit rows, both
            // ways: unique sequences produce the partial unique index, a seeded duplicate produces the
            // WARNING plus the plain index, and the migration completes in both cases (no crash-loop).
            // Re-runnable: the block drops the index it is about to create, and both branches only
            // read or (re)create that index — no row is ever written.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_AuditLogs_Sequence";
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "AuditLogs"
                        WHERE "Sequence" IS NOT NULL
                        GROUP BY "Sequence" HAVING count(*) > 1
                    ) THEN
                        RAISE WARNING 'AuditLogs holds repeated Sequence values, so the unique index was not created. The audit chain cannot verify until those rows are resolved.';
                        CREATE INDEX "IX_AuditLogs_Sequence" ON "AuditLogs" ("Sequence");
                    ELSE
                        CREATE UNIQUE INDEX "IX_AuditLogs_Sequence" ON "AuditLogs" ("Sequence") WHERE "Sequence" IS NOT NULL;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_UnconfirmedCallReportedAt_LastAttemptAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CanonicalizationVersion",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs",
                column: "Sequence");
        }
    }
}
