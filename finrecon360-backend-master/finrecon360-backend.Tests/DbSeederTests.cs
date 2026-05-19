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

            var expectedScopedPermissions = new[]
            {
                "ADMIN.IMPORTS.POS.CREATE",
                "ADMIN.IMPORTS.POS.EDIT",
                "ADMIN.IMPORTS.POS.COMMIT",
                "ADMIN.RECONCILIATION.POS.RESOLVE",
                "ADMIN.IMPORTS.ERP.CREATE",
                "ADMIN.IMPORTS.ERP.EDIT",
                "ADMIN.IMPORTS.ERP.COMMIT",
                "ADMIN.RECONCILIATION.ERP.RESOLVE",
                "ADMIN.IMPORTS.GATEWAY.CREATE",
                "ADMIN.IMPORTS.GATEWAY.EDIT",
                "ADMIN.IMPORTS.GATEWAY.COMMIT",
                "ADMIN.RECONCILIATION.GATEWAY.RESOLVE",
                "ADMIN.IMPORTS.BANK.CREATE",
                "ADMIN.IMPORTS.BANK.EDIT",
                "ADMIN.IMPORTS.BANK.COMMIT",
                "ADMIN.RECONCILIATION.BANK.RESOLVE"
            };

            foreach (var permissionCode in expectedScopedPermissions)
            {
                var permissionId = await db.Permissions
                    .Where(p => p.Code == permissionCode)
                    .Select(p => p.PermissionId)
                    .SingleAsync();

                var isGranted = await db.RolePermissions.AnyAsync(rp => rp.RoleId == adminRoleId && rp.PermissionId == permissionId);
                Assert.True(isGranted);
            }
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
