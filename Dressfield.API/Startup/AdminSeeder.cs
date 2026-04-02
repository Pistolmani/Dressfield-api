using Dressfield.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Serilog;

namespace Dressfield.API.Startup;

public static class AdminSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await EnsureRolesAsync(roleManager);
            await EnsureAdminUserAsync(app, userManager);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Skipping seed: database is unavailable.");
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = ["Admin", "Customer"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task EnsureAdminUserAsync(WebApplication app, UserManager<ApplicationUser> userManager)
    {
        var adminEmail = app.Configuration["Admin:Email"] ?? "admin@dressfield.ge";
        var adminPassword = app.Configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            if (app.Environment.IsDevelopment())
            {
                Log.Warning("Admin:Password not set in appsettings.Development.json -- admin user will not be seeded.");
                return;
            }

            throw new InvalidOperationException(
                "Admin:Password must be set in production via Azure environment variable Admin__Password.");
        }

        if (await userManager.FindByEmailAsync(adminEmail) != null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Admin",
            LastName = "Dressfield",
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
            return;
        }

        Log.Error("Failed to create admin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
