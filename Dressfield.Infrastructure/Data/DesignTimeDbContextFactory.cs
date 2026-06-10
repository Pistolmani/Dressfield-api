using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dressfield.Infrastructure.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build a DbContext without spinning up the full
/// host (which fails locally due to validator DI scanning + missing prod secrets).
/// Only used at design time - the runtime uses Program.cs's registration.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DressfieldDbContext>
{
    public DressfieldDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DressfieldDbContext>()
            // Connection string is irrelevant for `migrations add`; it only needs the provider
            // so it can emit provider-specific column types.
            .UseMySql("Server=localhost;Database=dressfield_design_time;User=root;Password=root;",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;

        return new DressfieldDbContext(options);
    }
}
