using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Dressfield.Tests.TestInfrastructure;

/// <summary>
/// Builds a minimal DI container hosting real ASP.NET Identity (UserManager/RoleManager)
/// over the SQLite-backed test DbContext. EnsureCreated already produced the Identity tables
/// (DressfieldDbContext extends IdentityDbContext). No token providers - refresh tokens are
/// the app's own table, not Identity's.
/// </summary>
public static class IdentityTestHost
{
    public static ServiceProvider Build(DressfieldDbContext db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireDigit = false;
                o.Password.RequiredLength = 6;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<DressfieldDbContext>();

        return services.BuildServiceProvider();
    }
}
