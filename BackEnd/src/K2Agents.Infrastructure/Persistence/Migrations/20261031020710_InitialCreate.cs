using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace K2Agents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Document = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Industry = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConversationRetentionDays = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AIAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BusinessType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    WelcomeMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BehaviorJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIAgents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIAgents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AIUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsSimulated = table.Column<bool>(type: "boolean", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AIUsageRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Configuration = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutionCount = table.Column<int>(type: "integer", nullable: false),
                    LastExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsLead = table.Column<bool>(type: "boolean", nullable: false),
                    Interest = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Link = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SystemPrompt = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    BehaviorJson = table.Column<string>(type: "jsonb", nullable: false),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Temperature = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    WelcomeMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentVersions_AIAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AIAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    EncryptedSecrets = table.Column<string>(type: "text", nullable: true),
                    AllowedOrigins = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelConfigurations_AIAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AIAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelConfigurations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeDocuments_AIAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AIAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_KnowledgeDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_AIAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AIAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Appointments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalSessionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_AIAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AIAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversations_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Conversations_Users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "integer", nullable: true),
                    IsSimulated = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Administração da plataforma", "SUPER_ADMIN" },
                    { 2, "Gestão dos dados da própria empresa", "COMPANY_ADMIN" },
                    { 3, "Configuração dos agentes", "AGENT_MANAGER" },
                    { 4, "Atendimento humano", "ATTENDANT" },
                    { 5, "Consulta autorizada", "VIEWER" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_AgentId_VersionNumber",
                table: "AgentVersions",
                columns: new[] { "AgentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AIAgents_CompanyId_Status",
                table: "AIAgents",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AIUsageRecords_AgentId_CreatedAt",
                table: "AIUsageRecords",
                columns: new[] { "AgentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AIUsageRecords_CompanyId_CreatedAt",
                table: "AIUsageRecords",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AgentId",
                table: "Appointments",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CompanyId_StartDateTime_EndDateTime",
                table: "Appointments",
                columns: new[] { "CompanyId", "StartDateTime", "EndDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ContactId",
                table: "Appointments",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_CompanyId_TriggerType_Enabled",
                table: "AutomationRules",
                columns: new[] { "CompanyId", "TriggerType", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelConfigurations_AgentId",
                table: "ChannelConfigurations",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelConfigurations_CompanyId_ChannelType",
                table: "ChannelConfigurations",
                columns: new[] { "CompanyId", "ChannelType" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelConfigurations_PublicToken",
                table: "ChannelConfigurations",
                column: "PublicToken",
                unique: true,
                filter: "\"PublicToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Document",
                table: "Companies",
                column: "Document",
                unique: true,
                filter: "\"Document\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Name",
                table: "Companies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId_Email",
                table: "Contacts",
                columns: new[] { "CompanyId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId_Phone",
                table: "Contacts",
                columns: new[] { "CompanyId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId_Status",
                table: "Contacts",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_CompanyId_ExternalMessageId",
                table: "ConversationMessages",
                columns: new[] { "CompanyId", "ExternalMessageId" },
                unique: true,
                filter: "\"ExternalMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_CreatedAt",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_AgentId_Channel_ExternalSessionId",
                table: "Conversations",
                columns: new[] { "AgentId", "Channel", "ExternalSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_AssignedUserId",
                table: "Conversations",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_AgentId_StartedAt",
                table: "Conversations",
                columns: new[] { "CompanyId", "AgentId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CompanyId_Status_LastMessageAt",
                table: "Conversations",
                columns: new[] { "CompanyId", "Status", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ContactId",
                table: "Conversations",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_AgentId",
                table: "KnowledgeDocuments",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_CompanyId_AgentId_Status",
                table: "KnowledgeDocuments",
                columns: new[] { "CompanyId", "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CompanyId_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "CompanyId", "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentVersions");

            migrationBuilder.DropTable(
                name: "AIUsageRecords");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "AutomationRules");

            migrationBuilder.DropTable(
                name: "ChannelConfigurations");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "KnowledgeDocuments");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "AIAgents");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
