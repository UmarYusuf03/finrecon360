using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public interface ITenantUserDirectoryService
    {
        Task UpsertTenantUserAsync(Guid tenantId, User user, TenantUserRole role, CancellationToken cancellationToken = default);
        Task ReplaceTenantAdminsAsync(Guid tenantId, IReadOnlyCollection<User> admins, CancellationToken cancellationToken = default);
    }

    public class TenantUserDirectoryService : ITenantUserDirectoryService
    {
        private readonly ITenantDbContextFactory _tenantDbContextFactory;

        public TenantUserDirectoryService(ITenantDbContextFactory tenantDbContextFactory)
        {
            _tenantDbContextFactory = tenantDbContextFactory;
        }

        public async Task UpsertTenantUserAsync(Guid tenantId, User user, TenantUserRole role, CancellationToken cancellationToken = default)
        {
            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);
            var record = await tenantDb.TenantUsers.FirstOrDefaultAsync(x => x.UserId == user.UserId, cancellationToken);

            if (record == null)
            {
                record = new TenantScopedUser
                {
                    TenantUserId = Guid.NewGuid(),
                    UserId = user.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                tenantDb.TenantUsers.Add(record);
            }

            record.Email = user.Email;
            record.DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
            record.Role = role.ToString();
            record.Status = user.Status.ToString();
            record.IsActive = user.IsActive;
            record.UpdatedAt = DateTime.UtcNow;

            var roleCode = role == TenantUserRole.TenantAdmin ? "ADMIN" : "USER";
            var roleEntity = await tenantDb.Roles.FirstOrDefaultAsync(r => r.Code == roleCode && r.IsActive, cancellationToken);
            if (roleEntity != null)
            {
                var existingAssignments = await tenantDb.UserRoles
                    .Where(x => x.UserId == user.UserId)
                    .ToListAsync(cancellationToken);

                if (existingAssignments.Count > 0)
                {
                    tenantDb.UserRoles.RemoveRange(existingAssignments);
                }

                tenantDb.UserRoles.Add(new TenantUserRoleAssignment
                {
                    UserId = user.UserId,
                    RoleId = roleEntity.RoleId,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await tenantDb.SaveChangesAsync(cancellationToken);
        }

        public async Task ReplaceTenantAdminsAsync(Guid tenantId, IReadOnlyCollection<User> admins, CancellationToken cancellationToken = default)
        {
            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenantId, cancellationToken);
            var adminRole = await tenantDb.Roles.FirstOrDefaultAsync(r => r.Code == "ADMIN" && r.IsActive, cancellationToken);

            var adminIds = admins.Select(u => u.UserId).ToHashSet();
            var existingAdmins = await tenantDb.TenantUsers
                .Where(x => x.Role == TenantUserRole.TenantAdmin.ToString())
                .ToListAsync(cancellationToken);

            var toDelete = existingAdmins.Where(x => !adminIds.Contains(x.UserId)).ToList();
            if (toDelete.Count > 0)
            {
                tenantDb.TenantUsers.RemoveRange(toDelete);
            }

            foreach (var admin in admins)
            {
                var existing = await tenantDb.TenantUsers.FirstOrDefaultAsync(x => x.UserId == admin.UserId, cancellationToken);
                if (existing == null)
                {
                    existing = new TenantScopedUser
                    {
                        TenantUserId = Guid.NewGuid(),
                        UserId = admin.UserId,
                        CreatedAt = DateTime.UtcNow
                    };
                    tenantDb.TenantUsers.Add(existing);
                }

                existing.Email = admin.Email;
                existing.DisplayName = admin.DisplayName ?? $"{admin.FirstName} {admin.LastName}".Trim();
                existing.Role = TenantUserRole.TenantAdmin.ToString();
                existing.Status = admin.Status.ToString();
                existing.IsActive = admin.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;

                if (adminRole != null)
                {
                    var existingAssignments = await tenantDb.UserRoles
                        .Where(x => x.UserId == admin.UserId)
                        .ToListAsync(cancellationToken);

                    if (existingAssignments.Count > 0)
                    {
                        tenantDb.UserRoles.RemoveRange(existingAssignments);
                    }

                    tenantDb.UserRoles.Add(new TenantUserRoleAssignment
                    {
                        UserId = admin.UserId,
                        RoleId = adminRole.RoleId,
                        AssignedAt = DateTime.UtcNow
                    });
                }
            }

            await tenantDb.SaveChangesAsync(cancellationToken);
        }
    }
}
