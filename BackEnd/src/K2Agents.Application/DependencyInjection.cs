using FluentValidation;
using K2Agents.Application.Agents;
using K2Agents.Application.Auth;
using K2Agents.Application.Automations;
using K2Agents.Application.Scheduling;
using K2Agents.Application.Channels;
using K2Agents.Application.Contacts;
using K2Agents.Application.Notifications;
using K2Agents.Application.WebChat;
using K2Agents.Application.WhatsApp;
using K2Agents.Application.Companies;
using K2Agents.Application.Conversations;
using K2Agents.Application.Knowledge;
using K2Agents.Application.Playground;
using K2Agents.Application.Privacy;
using K2Agents.Application.Reports;
using K2Agents.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace K2Agents.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<ISimulatedResponder, SimulatedResponder>();
        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IPlaygroundService, PlaygroundService>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IWebChatService, WebChatService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<IContactCaptureService, ContactCaptureService>();
        services.AddScoped<IHandoffService, HandoffService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAutomationEngine, AutomationEngine>();
        services.AddScoped<IAutomationService, AutomationService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IAgentTools, AgentTools>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IRetentionService, RetentionService>();
        services.AddScoped<IWhatsAppWebhookService, WhatsAppWebhookService>();
        services.AddScoped<IWhatsAppMessageProcessor, WhatsAppMessageProcessor>();
        services.AddScoped<IChannelDelivery, WhatsAppDelivery>();
        return services;
    }
}
