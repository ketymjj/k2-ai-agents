using FluentValidation;
using K2Agents.Application.Automations;
using K2Agents.Application.Common;
using K2Agents.Application.Scheduling;
using K2Agents.Domain.Entities;
using K2Agents.Infrastructure.Security;
using K2Agents.Tests.Knowledge;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Scheduling;

/// <summary>Reservas simultâneas no mesmo horário contra PostgreSQL real: só uma pode vencer.</summary>
public class PostgresBookingConcurrencyTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private sealed class NoAutomations : IAutomationEngine
    {
        public Task PublishAsync(AutomationEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    private AppointmentService NewService(Guid companyId)
    {
        var user = FakeCurrentUser.Of(companyId, Domain.Enums.UserRole.Attendant);
        // Mesma configuração da produção: com retry, transações manuais precisam passar pela estratégia de execução.
        var db = new Infrastructure.Persistence.AppDbContext(
            new DbContextOptionsBuilder<Infrastructure.Persistence.AppDbContext>()
                .UseNpgsql(pg.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)).Options, user);
        return new AppointmentService(db, user, new AuditService(db, user), new NoAutomations(),
            new SchedulingSettingsValidator(), new SaveAppointmentRequestValidator());
    }

    [Fact]
    public async Task ParallelBookingsForTheSameSlot_OnlyOneSucceeds()
    {
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(15);
        var attempts = Enumerable.Range(0, 8).Select(async i =>
        {
            try
            {
                await NewService(pg.CompanyA).CreateAsync(new SaveAppointmentRequest(null, null, null, $"Serviço {i}", start, 60, null), CancellationToken.None);
                return true;
            }
            catch (AppException ex) when (ex.Code == "slot_unavailable")
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r));
        await using var db = pg.CreateDb();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.CompanyId == pg.CompanyA && a.StartDateTime == start));
    }
}
