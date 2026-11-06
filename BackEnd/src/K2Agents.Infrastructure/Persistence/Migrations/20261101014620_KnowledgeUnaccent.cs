using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Busca insensível a acentos. O PostgreSQL não altera a expressão de uma coluna gerada, e recriá-la
    /// remove o índice GIN — por isso o SQL explícito: função, coluna e índice são recriados em ordem.
    /// </summary>
    public partial class KnowledgeUnaccent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent;

                -- unaccent() não é IMMUTABLE (depende do search_path); colunas geradas exigem função imutável.
                CREATE OR REPLACE FUNCTION public.immutable_unaccent(text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;

                DROP INDEX IF EXISTS "IX_KnowledgeChunks_SearchVector";
                ALTER TABLE "KnowledgeChunks" DROP COLUMN "SearchVector";
                ALTER TABLE "KnowledgeChunks" ADD COLUMN "SearchVector" tsvector
                    GENERATED ALWAYS AS (to_tsvector('portuguese', immutable_unaccent("Content"))) STORED;
                CREATE INDEX "IX_KnowledgeChunks_SearchVector" ON "KnowledgeChunks" USING GIN ("SearchVector");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_KnowledgeChunks_SearchVector";
                ALTER TABLE "KnowledgeChunks" DROP COLUMN "SearchVector";
                ALTER TABLE "KnowledgeChunks" ADD COLUMN "SearchVector" tsvector
                    GENERATED ALWAYS AS (to_tsvector('portuguese', "Content")) STORED;
                CREATE INDEX "IX_KnowledgeChunks_SearchVector" ON "KnowledgeChunks" USING GIN ("SearchVector");
                DROP FUNCTION IF EXISTS public.immutable_unaccent(text);
                """);
        }
    }
}
