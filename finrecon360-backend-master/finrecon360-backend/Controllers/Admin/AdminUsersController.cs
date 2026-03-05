using finrecon360_backend.Data;
using finrecon360_backend.Dtos;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize]
    [EnableRateLimiting("admin")]
    public class AdminUsersController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUserContext _userContext;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDbContextFactory _tenantDbContextFactory;

        public AdminUsersController(
            AppDbContext dbContext,
            IPasswordHasher passwordHasher,
            IUserContext userContext,
            ITenantContext tenantContext,
            ITenantDbContextFactory tenantDbContextFactory)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _userContext = userContext;
            _tenantContext = tenantContext;
            _tenantDbContextFactory = tenantDbContextFactory;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminUserSummaryDto>>> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;
            var tenantId = auth.TenantId;

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var tenantUserQuery = _dbContext.TenantUsers.AsNoTracking().Where(tu => tu.TenantId == tenantId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100) term = term[..100];
                tenantUserQuery = tenantUserQuery.Where(tu => tu.User.Email.Contains(term) || (tu.User.DisplayName != null && tu.User.DisplayName.Contains(term)));
            }

            var totalCount = await tenantUserQuery.CountAsync();
            var userIds = await tenantUserQuery.OrderBy(tu => tu.User.Email).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(tu => tu.UserId)
                .ToListAsync();

            var users = await _dbContext.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToListAsync();

            var tenantRoles = await tenantDb.UserRoles.AsNoTracking()
                .Where(ur => userIds.Contains(ur.UserId))
                .Select(ur => new { ur.UserId, ur.Role.Code })
                .ToListAsync();

            var tenantScopedActiveFlags = await tenantDb.TenantUsers.AsNoTracking()
                .Where(tu => userIds.Contains(tu.UserId))
                .Select(tu => new { tu.UserId, tu.IsActive })
                .ToListAsync();

            var roleMap = tenantRoles.GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Code).Distinct().ToList());
            var activeMap = tenantScopedActiveFlags.ToDictionary(x => x.UserId, x => x.IsActive);

            var items = users.OrderBy(u => u.Email)
                .Select(u => new AdminUserSummaryDto(
                    u.UserId,
                    u.Email,
                    u.DisplayName ?? $"{u.FirstName} {u.LastName}".Trim(),
                    activeMap.TryGetValue(u.UserId, out var scopedIsActive) ? scopedIsActive : true,
                    u.Status.ToString(),
                    roleMap.TryGetValue(u.UserId, out var roles) ? roles : Array.Empty<string>()))
                .ToList();

            return Ok(new PagedResult<AdminUserSummaryDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpGet("{userId:guid}")]
        public async Task<ActionResult<AdminUserDetailDto>> GetUser(Guid userId)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var inTenant = await _dbContext.TenantUsers.AsNoTracking().AnyAsync(tu => tu.TenantId == auth.TenantId && tu.UserId == userId);
            if (!inTenant) return NotFound();

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound();

            var roles = await tenantDb.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => new RoleSummaryDto(ur.Role.RoleId, ur.Role.Code, ur.Role.Name, ur.Role.Description, ur.Role.IsSystem, ur.Role.IsActive))
                .ToListAsync();

            var tenantScopedUser = await tenantDb.TenantUsers.AsNoTracking().FirstOrDefaultAsync(tu => tu.UserId == userId);

            return Ok(new AdminUserDetailDto(
                user.UserId,
                user.Email,
                user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
                tenantScopedUser?.IsActive ?? true,
                user.Status.ToString(),
                roles));
        }

        [HttpPost]
        public async Task<ActionResult<AdminUserSummaryDto>> CreateUser([FromBody] AdminUserCreateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync(requireTenantDb: false);
            if (auth.Error != null) return auth.Error;
            var tenantId = auth.TenantId;

            var email = request.Email.Trim().ToLowerInvariant();
            var displayName = request.DisplayName.Trim();
            var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

            var maxAccounts = await _dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.TenantId == tenantId)
                .Select(t => t.CurrentSubscription != null ? (int?)t.CurrentSubscription.Plan.MaxAccounts : null)
                .FirstOrDefaultAsync();

            if (maxAccounts.HasValue)
            {
                var currentUsers = await _dbContext.TenantUsers.AsNoTracking().Where(tu => tu.TenantId == tenantId).Select(tu => tu.UserId).Distinct().CountAsync();
                if (currentUsers >= maxAccounts.Value)
                {
                    return BadRequest(new { message = $"Tenant user limit reached ({maxAccounts.Value})." });
                }
            }

            await using var tenantDb = await _tenantDbContextFactory.CreateAsync(tenantId);
            var actorId = _userContext.UserId!.Value;
            var actorIsActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == actorId && tu.IsActive);
            if (!actorIsActiveInTenant)
            {
                return Forbid();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    Email = email,
                    DisplayName = displayName,
                    PhoneNumber = phoneNumber,
                    FirstName = displayName,
                    LastName = string.Empty,
                    Country = string.Empty,
                    Gender = string.Empty,
                    PasswordHash = _passwordHasher.Hash(request.Password),
                    CreatedAt = DateTime.UtcNow,
                    EmailConfirmed = false,
                    IsActive = true,
                    Status = UserStatus.Active
                };
                _dbContext.Users.Add(user);
            }
            else
            {
                var hasOtherTenantMembership = await _dbContext.TenantUsers.AsNoTracking()
                    .AnyAsync(tu => tu.UserId == user.UserId && tu.TenantId != tenantId);
                if (hasOtherTenantMembership)
                {
                    return Conflict(new { message = "This email is already assigned to another tenant." });
                }
            }

            var tenantMembership = await _dbContext.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == user.UserId);
            if (tenantMembership == null)
            {
                tenantMembership = new TenantUser
                {
                    TenantUserId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = user.UserId,
                    Role = TenantUserRole.TenantUser,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.TenantUsers.Add(tenantMembership);
            }

            var (roleIds, hasMissing) = await ResolveTenantRoleIdsAsync(tenantDb, request.RoleIds, request.RoleCodes);
            if (hasMissing) return BadRequest(new { message = "One or more role identifiers were not found." });

            if (roleIds.Count == 0)
            {
                var defaultRole = await tenantDb.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Code == "USER" && r.IsActive);
                if (defaultRole != null)
                {
                    roleIds.Add(defaultRole.RoleId);
                }
            }

            var roleCodes = await tenantDb.Roles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId)).Select(r => r.Code).ToListAsync();
            tenantMembership.Role = roleCodes.Contains("ADMIN") ? TenantUserRole.TenantAdmin : TenantUserRole.TenantUser;

            await _dbContext.SaveChangesAsync();

            await UpsertTenantScopedUserAsync(tenantDb, user, tenantMembership.Role);
            await SetTenantUserRolesAsync(tenantDb, user.UserId, roleIds);

            return CreatedAtAction(nameof(GetUser), new { userId = user.UserId }, new AdminUserSummaryDto(
                user.UserId,
                user.Email,
                user.DisplayName ?? displayName,
                user.IsActive,
                user.Status.ToString(),
                roleCodes));
        }

        [HttpPut("{userId:guid}")]
        public async Task<ActionResult<AdminUserSummaryDto>> UpdateUser(Guid userId, [FromBody] AdminUserUpdateRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var inTenant = await _dbContext.TenantUsers.AsNoTracking().AnyAsync(tu => tu.TenantId == auth.TenantId && tu.UserId == userId);
            if (!inTenant) return NotFound();

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null) return NotFound();

            var displayName = request.DisplayName.Trim();
            user.DisplayName = displayName;
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var role = await _dbContext.TenantUsers.AsNoTracking()
                .Where(tu => tu.TenantId == auth.TenantId && tu.UserId == userId)
                .Select(tu => tu.Role)
                .FirstOrDefaultAsync();

            await UpsertTenantScopedUserAsync(tenantDb, user, role);

            var roleCodes = await GetRoleCodesForUserAsync(tenantDb, userId);
            var tenantScopedUser = await tenantDb.TenantUsers.AsNoTracking().FirstOrDefaultAsync(tu => tu.UserId == userId);
            return Ok(new AdminUserSummaryDto(user.UserId, user.Email, user.DisplayName ?? displayName, tenantScopedUser?.IsActive ?? true, user.Status.ToString(), roleCodes));
        }

        [HttpPut("{userId:guid}/roles")]
        public async Task<IActionResult> ReplaceUserRoles(Guid userId, [FromBody] AdminUserRoleSetRequest request)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var tenantMembership = await _dbContext.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == auth.TenantId && tu.UserId == userId);
            if (tenantMembership == null) return NotFound();

            var (roleIds, hasMissing) = await ResolveTenantRoleIdsAsync(tenantDb, request.RoleIds, request.RoleCodes);
            if (hasMissing) return BadRequest(new { message = "One or more role identifiers were not found." });

            if (roleIds.Count == 0)
            {
                var defaultRole = await tenantDb.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Code == "USER" && r.IsActive);
                if (defaultRole != null) roleIds.Add(defaultRole.RoleId);
            }

            var roleCodes = await tenantDb.Roles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId)).Select(r => r.Code).ToListAsync();
            var targetIsCurrentlyAdmin = tenantMembership.Role == TenantUserRole.TenantAdmin;
            var targetWillBeAdmin = roleCodes.Contains("ADMIN");
            if (targetIsCurrentlyAdmin && !targetWillBeAdmin)
            {
                var hasAnotherTenantAdmin = await _dbContext.TenantUsers.AsNoTracking().AnyAsync(tu =>
                    tu.TenantId == auth.TenantId &&
                    tu.Role == TenantUserRole.TenantAdmin &&
                    tu.UserId != userId);
                if (!hasAnotherTenantAdmin)
                {
                    return Conflict(new { message = "Cannot remove ADMIN from the last tenant admin. Assign ADMIN to another user first." });
                }
            }

            tenantMembership.Role = targetWillBeAdmin ? TenantUserRole.TenantAdmin : TenantUserRole.TenantUser;

            await _dbContext.SaveChangesAsync();
            await SetTenantUserRolesAsync(tenantDb, userId, roleIds);

            return NoContent();
        }

        [HttpPost("{userId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateUser(Guid userId)
        {
            return await SetUserActiveState(userId, false);
        }

        [HttpPost("{userId:guid}/activate")]
        public async Task<IActionResult> ActivateUser(Guid userId)
        {
            return await SetUserActiveState(userId, true);
        }

        private async Task<IActionResult> SetUserActiveState(Guid userId, bool isActive)
        {
            var auth = await AuthorizeTenantAdminAsync();
            if (auth.Error != null) return auth.Error;
            await using var tenantDb = auth.Db!;

            var inTenant = await _dbContext.TenantUsers.AsNoTracking().AnyAsync(tu => tu.TenantId == auth.TenantId && tu.UserId == userId);
            if (!inTenant) return NotFound();

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null) return NotFound();

            var role = await _dbContext.TenantUsers.AsNoTracking().Where(tu => tu.TenantId == auth.TenantId && tu.UserId == userId).Select(tu => tu.Role).FirstOrDefaultAsync();
            await UpsertTenantScopedUserAsync(tenantDb, user, role, isActive);
            return NoContent();
        }

        private static async Task<IReadOnlyList<string>> GetRoleCodesForUserAsync(TenantDbContext tenantDb, Guid userId)
        {
            return await tenantDb.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Code)
                .Distinct()
                .ToListAsync();
        }

        private static async Task<(HashSet<Guid> RoleIds, bool HasMissing)> ResolveTenantRoleIdsAsync(TenantDbContext tenantDb, IReadOnlyList<Guid>? roleIds, IReadOnlyList<string>? roleCodes)
        {
            var ids = new HashSet<Guid>();
            var hasMissing = false;

            if (roleIds != null)
            {
                var requestedIds = roleIds.Distinct().ToList();
                var existingIds = await tenantDb.Roles.Where(r => requestedIds.Contains(r.RoleId)).Select(r => r.RoleId).ToListAsync();
                if (existingIds.Count != requestedIds.Count) hasMissing = true;
                foreach (var id in existingIds) ids.Add(id);
            }

            if (roleCodes != null && roleCodes.Count > 0)
            {
                var codes = roleCodes.Select(c => c.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
                var matchingIds = await tenantDb.Roles.Where(r => codes.Contains(r.Code)).Select(r => r.RoleId).ToListAsync();
                if (matchingIds.Count != codes.Count) hasMissing = true;
                foreach (var id in matchingIds) ids.Add(id);
            }

            if (ids.Count == 0)
            {
                return (ids, hasMissing);
            }

            var activeRoles = await tenantDb.Roles.Where(r => ids.Contains(r.RoleId) && r.IsActive).Select(r => r.RoleId).ToListAsync();
            return (new HashSet<Guid>(activeRoles), hasMissing);
        }

        private static async Task SetTenantUserRolesAsync(TenantDbContext tenantDb, Guid userId, HashSet<Guid> roleIds)
        {
            var existing = await tenantDb.UserRoles.Where(ur => ur.UserId == userId).ToListAsync();
            if (existing.Count > 0)
            {
                tenantDb.UserRoles.RemoveRange(existing);
            }

            foreach (var roleId in roleIds)
            {
                tenantDb.UserRoles.Add(new TenantUserRoleAssignment
                {
                    UserId = userId,
                    RoleId = roleId,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await tenantDb.SaveChangesAsync();
        }

        private static async Task UpsertTenantScopedUserAsync(TenantDbContext tenantDb, User user, TenantUserRole role, bool? tenantIsActiveOverride = null)
        {
            var record = await tenantDb.TenantUsers.FirstOrDefaultAsync(x => x.UserId == user.UserId);
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
            record.IsActive = tenantIsActiveOverride ?? record.IsActive;
            record.UpdatedAt = DateTime.UtcNow;
            await tenantDb.SaveChangesAsync();
        }

        private async Task<(Guid TenantId, TenantDbContext? Db, ActionResult? Error)> AuthorizeTenantAdminAsync(bool requireTenantDb = true)
        {
            if (_userContext.UserId is not { } userId) return (Guid.Empty, null, Unauthorized());

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant == null) return (Guid.Empty, null, Forbid());

            var isTenantAdmin = await _dbContext.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenant.TenantId && tu.UserId == userId && tu.Role == TenantUserRole.TenantAdmin);
            if (!isTenantAdmin) return (Guid.Empty, null, Forbid());

            TenantDbContext? tenantDb = null;
            if (requireTenantDb)
            {
                tenantDb = await _tenantDbContextFactory.CreateAsync(tenant.TenantId);
                var isActiveInTenant = await tenantDb.TenantUsers.AsNoTracking().AnyAsync(tu => tu.UserId == userId && tu.IsActive);
                if (!isActiveInTenant) return (Guid.Empty, null, Forbid());
            }

            return (tenant.TenantId, tenantDb, null);
        }
    }
}
