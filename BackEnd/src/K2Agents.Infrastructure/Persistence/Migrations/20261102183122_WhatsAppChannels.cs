using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalAccountId",
                table: "ChannelConfigurations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelConfigurations_ChannelType_ExternalAccountId",
                table: "ChannelConfigurations",
                columns: new[] { "ChannelType", "ExternalAccountId" },
                unique: true,
                filter: "\"ExternalAccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChannelConfigurations_ChannelType_ExternalAccountId",
                table: "ChannelConfigurations");

            migrationBuilder.DropColumn(
                name: "ExternalAccountId",
                table: "ChannelConfigurations");
        }
    }
}
