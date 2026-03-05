using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Tests;

public class DbSeederTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"Seeder-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_creates_system_admin_and_assigns_ADMIN_role_from_env_credentials()
    {
        var previousEmail = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_EMAIL");
        var previousPassword = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_PASSWORD");
        var previousFirstName = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_FIRST_NAME");
        var previousLastName = Environment.GetEnvironmentVariable("SYSTEM_ADMIN_LAST_NAME");

        Environment.SetEnvironmentVariable("SYSTEM_ADMIN_EMAIL", "seed-admin@test.local");
        Environment.SetEnvironmentVariable("SYSTEM_ADMIN_PASSWORD", "SeedAdmin123!");
        Environment.SetEnvironmentVariable("SYSTEM_ADMIN_FIRST_NAME", "Seed");
        Environment.SetEnvironmentVariable("SYSTEM_ADMIN_LAST_NAME", "Admin");

        try
        {
            await using var db = CreateDbContext();
            await DbSeeder.SeedAsync(db);

            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "seed-admin@test.local");
            Assert.NotNull(user);
            Assert.True(user!.IsSystemAdmin);
            Assert.True(user.IsActive);
            Assert.Equal(UserStatus.Active, user.Status);
            Assert.True(user.EmailConfirmed);
            Assert.Equal("Seed", user.FirstName);
            Assert.Equal("Admin", user.LastName);

            var adminRoleId = await db.Roles
                .Where(r => r.Code == "ADMIN")
                .Select(r => r.RoleId)
                .SingleAsync();

            var hasAdminRole = await db.UserRoles.AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == adminRoleId);
            Assert.True(hasAdminRole);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYSTEM_ADMIN_EMAIL", previousEmail);
            Environment.SetEnvironmentVariable("SYSTEM_ADMIN_PASSWORD", previousPassword);
            Environment.SetEnvironmentVariable("SYSTEM_ADMIN_FIRST_NAME", previousFirstName);
            Environment.SetEnvironmentVariable("SYSTEM_ADMIN_LAST_NAME", previousLastName);
        }
    }
}
