using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Persistence;

public class TenantIsolationTests
{
    private readonly string _db = $"tenant-{Guid.NewGuid()}";
    private readonly Guid _companyA = Guid.NewGuid();
    private readonly Guid _companyB = Guid.NewGuid();

    private async Task SeedAsync()
    {
        await using var system = TestDb.Create(_db, FakeCurrentUser.System());
        system.Companies.AddRange(
            new Company { Id = _companyA, Name = "Padaria Jardim Andrade" },
            new Company { Id = _companyB, Name = "Studio Bella" });
        system.AIAgents.AddRange(
            new AIAgent { CompanyId = _companyA, Name = "Ana" },
            new AIAgent { CompanyId = _companyB, Name = "Bella" });
        system.Contacts.AddRange(
            new Contact { CompanyId = _companyA, Name = "Cliente A" },
            new Contact { CompanyId = _companyB, Name = "Cliente B" });
        await system.SaveChangesAsync();
    }

    [Fact]
    public async Task CompanyUser_SeesOnlyOwnCompanyData()
    {
        await SeedAsync();
        await using var db = TestDb.Create(_db, FakeCurrentUser.Of(_companyA));

        var agents = await db.AIAgents.Select(a => a.Name).ToListAsync();
        var contacts = await db.Contacts.Select(c => c.Name).ToListAsync();

        Assert.Equal(["Ana"], agents);
        Assert.Equal(["Cliente A"], contacts);
    }

    [Fact]
    public async Task CompanyUser_CannotFindOtherCompanyRecordById()
    {
        await SeedAsync();
        Guid bellaId;
        await using (var system = TestDb.Create(_db, FakeCurrentUser.System()))
            bellaId = (await system.AIAgents.SingleAsync(a => a.Name == "Bella")).Id;

        await using var db = TestDb.Create(_db, FakeCurrentUser.Of(_companyA));
        Assert.Null(await db.AIAgents.FirstOrDefaultAsync(a => a.Id == bellaId));
    }

    [Fact]
    public async Task SuperAdmin_SeesAllCompanies()
    {
        await SeedAsync();
        await using var db = TestDb.Create(_db, FakeCurrentUser.SuperAdmin());
        Assert.Equal(2, await db.AIAgents.CountAsync());
    }

    [Fact]
    public async Task CompanyUser_CannotInsertRecordForAnotherCompany()
    {
        await SeedAsync();
        await using var db = TestDb.Create(_db, FakeCurrentUser.Of(_companyA));
        db.Contacts.Add(new Contact { CompanyId = _companyB, Name = "Intruso" });

        var ex = await Assert.ThrowsAsync<AppException>(() => db.SaveChangesAsync());
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task CompanyUser_InsertWithoutCompanyId_IsStampedWithOwnCompany()
    {
        await SeedAsync();
        await using var db = TestDb.Create(_db, FakeCurrentUser.Of(_companyA));
        var contact = new Contact { Name = "Novo lead" };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        Assert.Equal(_companyA, contact.CompanyId);
    }
}
