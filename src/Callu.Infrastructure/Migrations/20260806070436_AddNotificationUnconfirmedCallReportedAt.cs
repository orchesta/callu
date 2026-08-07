using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Callu.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationUnconfirmedCallReportedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UnconfirmedCallReportedAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnconfirmedCallReportedAt",
                table: "Notifications");
        }
    }
}
