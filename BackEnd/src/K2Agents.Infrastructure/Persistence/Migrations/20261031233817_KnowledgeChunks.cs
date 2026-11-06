using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnowledgeChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KnowledgeSources",
                table: "ConversationMessages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KnowledgeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('portuguese', \"Content\")", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeChunks_KnowledgeDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "KnowledgeDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_CompanyId_AgentId",
                table: "KnowledgeChunks",
                columns: new[] { "CompanyId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_DocumentId_ChunkIndex",
                table: "KnowledgeChunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_SearchVector",
                table: "KnowledgeChunks",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeChunks");

            migrationBuilder.DropColumn(
                name: "KnowledgeSources",
                table: "ConversationMessages");
        }
    }
}
