using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchedulingJson",
                table: "Companies",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() at time zone 'utc'");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ConversationId",
                table: "Appointments",
                column: "ConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_ConversationId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "SchedulingJson",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Appointments");
        }
    }
}
