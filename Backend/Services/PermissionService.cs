using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public class PermissionService : IPermissionService
    {
        private readonly AppDbContext _dbContext;
        private readonly Dictionary<Guid, HashSet<string>> _permissionCache = new();
        private readonly Dictionary<Guid, IReadOnlyList<string>> _roleCache = new();

        public PermissionService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<HashSet<string>> GetPermissionsForUserAsync(Guid userId)
        {
            if (_permissionCache.TryGetValue(userId, out var cached))
            {
                return cached;
            }

            var permissions = await _dbContext.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId && ur.Role.IsActive)
                .SelectMany(ur => ur.Role.RolePermissions
                    .Select(rp => rp.Permission.Code))
                .Distinct()
                .ToListAsync();

            var result = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            _permissionCache[userId] = result;
            return result;
        }

        public async Task<IReadOnlyList<string>> GetRolesForUserAsync(Guid userId)
        {
            if (_roleCache.TryGetValue(userId, out var cached))
            {
                return cached;
            }

            var roles = await _dbContext.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId && ur.Role.IsActive)
                .Select(ur => ur.Role.Code)
                .Distinct()
                .ToListAsync();

            _roleCache[userId] = roles;
            return roles;
        }
    }
}
