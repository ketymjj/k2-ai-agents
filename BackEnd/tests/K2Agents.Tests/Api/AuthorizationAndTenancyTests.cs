using System.Net;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class AuthorizationAndTenancyTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    private async Task<Guid> CompanyIdAsync(string name)
    {
        await using var db = factory.SystemDb();
        return (await db.Companies.FirstAsync(c => c.Name == name)).Id;
    }

    [Theory]
    [InlineData("/api/v1/companies")]
    [InlineData("/api/v1/users")]
    public async Task Anonymous_Gets401(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(Demo.PadariaViewer)]
    [InlineData(Demo.PadariaAttendant)]
    public async Task NonAdminRoles_CannotManageUsers(string email)
    {
        var client = await factory.AuthenticatedAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task CompanyAdmin_CannotCreateCompaniesOrChangeStatus()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var padaria = await CompanyIdAsync("Padaria Jardim Andrade");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/v1/companies", new { name = "Nova" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchJsonAsync($"/api/v1/companies/{padaria}/status", new { status = "Inactive" })).StatusCode);
    }

    [Fact]
    public async Task CompanyUser_ListsOnlyOwnCompany_AndCannotReadOthers()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        var bella = await CompanyIdAsync("Studio Bella");

        var list = await (await client.GetAsync("/api/v1/companies")).ReadJsonAsync();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Padaria Jardim Andrade", list.GetProperty("items")[0].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/companies/{bella}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/companies/{bella}/summary")).StatusCode);
    }

    [Fact]
    public async Task CompanyAdmin_CanUpdateOwnCompany_ButNotAnother()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var padaria = await CompanyIdAsync("Padaria Jardim Andrade");
        var bella = await CompanyIdAsync("Studio Bella");
        var body = new { name = "Padaria Jardim Andrade", phone = "(11) 99999-0000", industry = "Alimentação" };

        Assert.Equal(HttpStatusCode.OK, (await client.PutJsonAsync($"/api/v1/companies/{padaria}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutJsonAsync($"/api/v1/companies/{bella}", body)).StatusCode);
    }

    [Fact]
    public async Task CompanyAdmin_SeesAndManagesOnlyOwnUsers()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var bella = await CompanyIdAsync("Studio Bella");

        var users = await (await client.GetAsync("/api/v1/users?pageSize=100")).ReadJsonAsync();
        foreach (var u in users.GetProperty("items").EnumerateArray())
            Assert.Equal("Padaria Jardim Andrade", u.GetProperty("companyName").GetString());

        // Criar usuário em outra empresa: proibido.
        var cross = await client.PostJsonAsync("/api/v1/users",
            new { name = "Intruso", email = "intruso@bella.demo", password = "Senha@123", role = "Attendant", companyId = bella });
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);

        // Editar usuário de outra empresa: não encontrado (não revela existência).
        Guid bellaAttendant;
        await using (var db = factory.SystemDb())
            bellaAttendant = (await db.Users.FirstAsync(u => u.Email == "atendente@studiobella.demo")).Id;
        var update = await client.PutJsonAsync($"/api/v1/users/{bellaAttendant}",
            new { name = "Hack", email = "atendente@studiobella.demo", role = "CompanyAdmin" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task CompanyAdmin_CannotCreateSuperAdmin_NorSeeSuperAdminRole()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var response = await client.PostJsonAsync("/api/v1/users",
            new { name = "Escalada", email = "escalada@padaria.demo", password = "Senha@123", role = "SuperAdmin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var roles = await (await client.GetAsync("/api/v1/users/roles")).ReadJsonAsync();
        Assert.DoesNotContain(roles.EnumerateArray(), r => r.GetProperty("name").GetString() == "SUPER_ADMIN");
    }

    [Fact]
    public async Task CompanyAdmin_CreatesUserInOwnCompanyAutomatically()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/users",
            new { name = "Novo Atendente", email = $"novo-{Guid.NewGuid():N}@padaria.demo", password = "Senha@123", role = "Attendant" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Padaria Jardim Andrade", (await response.ReadJsonAsync()).GetProperty("companyName").GetString());
    }

    [Fact]
    public async Task User_CannotDeactivateSelf_NorChangeOwnRole()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        var id = me.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchJsonAsync($"/api/v1/users/{id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutJsonAsync($"/api/v1/users/{id}",
            new { name = "Admin", email = Demo.PadariaAdmin, role = "Viewer" })).StatusCode);
    }

    [Fact]
    public async Task DuplicateEmail_Returns409()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/users",
            new { name = "Duplicado", email = Demo.PadariaAttendant, password = "Senha@123", role = "Attendant" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CreatesCompany_AndDeactivationBlocksItsUsers()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);

        var created = await admin.PostJsonAsync("/api/v1/companies",
            new { name = "Empresa Teste", document = "98.765.432/0001-10", email = "contato@teste.demo", website = "https://teste.demo" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var company = await created.ReadJsonAsync();
        Assert.Equal("98765432000110", company.GetProperty("document").GetString());
        var companyId = company.GetProperty("id").GetString();

        var duplicate = await admin.PostJsonAsync("/api/v1/companies", new { name = "Outra", document = "98765432000110" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var email = $"user-{Guid.NewGuid():N}@teste.demo";
        (await admin.PostJsonAsync("/api/v1/users", new { name = "Usuário", email, password = "Senha@123", role = "CompanyAdmin", companyId }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().LoginAsync(email, "Senha@123")).StatusCode);

        var deactivate = await admin.PatchJsonAsync($"/api/v1/companies/{companyId}/status", new { status = "Inactive" });
        Assert.Equal("Inactive", (await deactivate.ReadJsonAsync()).GetProperty("status").GetString());

        var blocked = await factory.CreateClient().LoginAsync(email, "Senha@123");
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Equal("company_inactive", (await blocked.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SuperAdmin_SeesAllDemoCompanies_AndSearchWorks()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);

        var all = await (await admin.GetAsync("/api/v1/companies?pageSize=100")).ReadJsonAsync();
        Assert.True(all.GetProperty("total").GetInt32() >= 5);

        var search = await (await admin.GetAsync("/api/v1/companies?search=pet")).ReadJsonAsync();
        Assert.Equal("Clínica Pet Vida", search.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task InvalidCompanyPayload_Returns400WithFieldErrors()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var response = await admin.PostJsonAsync("/api/v1/companies", new { name = "", document = "123", website = "javascript:alert(1)" });

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Document", out _));
        Assert.True(errors.TryGetProperty("Website", out _));
    }
}
