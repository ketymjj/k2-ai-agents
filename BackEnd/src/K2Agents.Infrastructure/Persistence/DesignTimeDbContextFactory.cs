using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace K2Agents.Infrastructure.Persistence;

/// <summary>Usada apenas por `dotnet ef` para gerar migrations sem subir a API.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5436;Database=k2agents;Username=k2agents;Password=design_time_only";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options, new SystemUser());
    }

    private sealed class SystemUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? CompanyId => null;
        public UserRole? Role => null;
        public bool IsSuperAdmin => false;
        public string? IpAddress => null;
    }
}
