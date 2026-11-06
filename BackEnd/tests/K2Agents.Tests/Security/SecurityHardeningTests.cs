using System.Security.Cryptography;
using K2Agents.Application.Privacy;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Security;
using K2Agents.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace K2Agents.Tests.Security;

public class SecretProtectorTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void RoundTrips_WithFreshNonce_AndDetectsTampering()
    {
        var protector = new AesGcmSecretProtector(Key);
        var a = protector.Protect("EAAG-token-secreto");
        var b = protector.Protect("EAAG-token-secreto");

        Assert.StartsWith("v1:", a);
        Assert.NotEqual(a, b); // nonce aleatório: o mesmo segredo nunca gera o mesmo texto cifrado
        Assert.DoesNotContain("EAAG", a);
        Assert.Equal("EAAG-token-secreto", protector.Unprotect(a));

        var bytes = Convert.FromBase64String(a[3..]);
        bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("v1:" + Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() => new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)).Unprotect(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-base64!!")]
    [InlineData("c2hvcnQ=")] // 5 bytes
    public void InvalidKeys_FailWithAClearMessage(string? key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AesGcmSecretProtector.ParseKey(key));
        Assert.Contains("SECRETS_ENCRYPTION_KEY", ex.Message.Replace("Secrets:EncryptionKey", "SECRETS_ENCRYPTION_KEY"));
    }
}

public class RetentionTests
{
    [Fact]
    public async Task PurgesOnlyExpiredConversations_KeepsHumanWork_AndCompaniesWithoutPolicy()
    {
        var db = TestDb.Create(nameof(RetentionTests), FakeCurrentUser.System());
        var now = DateTime.UtcNow;
        var withPolicy = new Company { Name = "Com retenção", ConversationRetentionDays = 30 };
        var withoutPolicy = new Company { Name = "Sem retenção" };
        db.Companies.AddRange(withPolicy, withoutPolicy);

        Conversation Conv(Company c, int daysAgo, ConversationStatus status)
        {
            var conv = new Conversation { CompanyId = c.Id, AgentId = Guid.NewGuid(), Status = status, LastMessageAt = now.AddDays(-daysAgo), StartedAt = now.AddDays(-daysAgo) };
            db.Conversations.Add(conv);
            db.ConversationMessages.Add(new ConversationMessage { CompanyId = c.Id, ConversationId = conv.Id, SenderType = SenderType.Contact, Content = "dado pessoal" });
            return conv;
        }
        var expired = Conv(withPolicy, 40, ConversationStatus.Closed);
        var expiredOpen = Conv(withPolicy, 45, ConversationStatus.Open);
        var recent = Conv(withPolicy, 5, ConversationStatus.Closed);
        var inHumanService = Conv(withPolicy, 60, ConversationStatus.HumanActive);
        var otherCompany = Conv(withoutPolicy, 900, ConversationStatus.Closed);
        var appointment = new Appointment { CompanyId = withPolicy.Id, ServiceName = "Corte", ConversationId = expired.Id, StartDateTime = now, EndDateTime = now.AddHours(1) };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var result = await new RetentionService(db, new AuditService(db, FakeCurrentUser.System()), NullLogger<RetentionService>.Instance)
            .PurgeAsync(now, CancellationToken.None);

        Assert.Equal(new RetentionResult(1, 2, 2), result);
        var remaining = await db.Conversations.Select(c => c.Id).ToListAsync();
        Assert.DoesNotContain(expired.Id, remaining);
        Assert.DoesNotContain(expiredOpen.Id, remaining);
        Assert.Contains(recent.Id, remaining);
        Assert.Contains(inHumanService.Id, remaining);
        Assert.Contains(otherCompany.Id, remaining);
        Assert.False(await db.ConversationMessages.AnyAsync(m => m.ConversationId == expired.Id));
        Assert.Null((await db.Appointments.FirstAsync(a => a.Id == appointment.Id)).ConversationId); // agendamento mantido
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "retention.purged" && a.CompanyId == withPolicy.Id));
    }
}

/// <summary>Fora de Development/Testing, a API não sobe com configuração insegura.</summary>
public class ProductionStartupTests
{
    private sealed class ProductionFactory(string? encryptionKey, string? adminPassword) : K2ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("Secrets:EncryptionKey", encryptionKey ?? "");
            builder.UseSetting("Seed:AdminPassword", adminPassword ?? "");
            builder.UseSetting("Seed:DemoPassword", adminPassword ?? "");
        }
    }

    [Fact]
    public void WithoutEncryptionKey_StartupFails()
    {
        using var factory = new ProductionFactory(null, "Senha@Forte2026");
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("SECRETS_ENCRYPTION_KEY", Flatten(ex));
    }

    [Fact]
    public void WithoutSeedPasswords_TheSeedRefusesToCreateUsersWithThePublicDefault()
    {
        using var factory = new ProductionFactory(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), null);
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("SEED_ADMIN_PASSWORD", Flatten(ex));
    }

    [Fact]
    public async Task WithProperConfiguration_StartsAndUsesTheConfiguredPassword()
    {
        using var factory = new ProductionFactory(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "Senha@Forte2026");
        var client = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.LoginAsync(Demo.AdminEmail, "Senha@Forte2026")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.LoginAsync(Demo.AdminEmail, Demo.AdminPassword)).StatusCode);
    }

    private static string Flatten(Exception ex) => ex.InnerException is null ? ex.Message : ex.Message + " | " + Flatten(ex.InnerException);
}
