using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Reports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIBillingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AmountUsd = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIBillingEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AIBillingEntries_Month_Provider",
                table: "AIBillingEntries",
                columns: new[] { "Month", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIBillingEntries");
        }
    }
}
