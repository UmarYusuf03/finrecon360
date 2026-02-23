using finrecon360_backend.Authorization;
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
    [RequirePermission("USER_MANAGEMENT")]
    [EnableRateLimiting("admin")]
    public class AdminUsersController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private readonly AppDbContext _dbContext;
        private readonly IPasswordHasher _passwordHasher;

        public AdminUsersController(AppDbContext dbContext, IPasswordHasher passwordHasher)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminUserSummaryDto>>> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : Math.Min(pageSize, MaxPageSize);

            var query = _dbContext.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                if (term.Length > 100)
                {
                    term = term.Substring(0, 100);
                }
                query = query.Where(u => u.Email.Contains(term) || (u.DisplayName != null && u.DisplayName.Contains(term)));
            }

            var totalCount = await query.CountAsync();
            var users = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = users.Select(u => u.UserId).ToList();
            var roleLookup = await _dbContext.UserRoles
                .AsNoTracking()
                .Where(ur => userIds.Contains(ur.UserId))
                .Select(ur => new { ur.UserId, ur.Role.Code })
                .ToListAsync();

            var rolesByUser = roleLookup
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Code).Distinct().ToList());

            var items = users.Select(u => new AdminUserSummaryDto(
                u.UserId,
                u.Email,
                u.DisplayName ?? $"{u.FirstName} {u.LastName}".Trim(),
                u.IsActive,
                rolesByUser.TryGetValue(u.UserId, out var roles) ? roles : Array.Empty<string>()))
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
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound();
            }

            var roles = await _dbContext.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => new RoleSummaryDto(
                    ur.Role.RoleId,
                    ur.Role.Code,
                    ur.Role.Name,
                    ur.Role.Description,
                    ur.Role.IsSystem,
                    ur.Role.IsActive))
                .ToListAsync();

            return Ok(new AdminUserDetailDto(
                user.UserId,
                user.Email,
                user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
                user.IsActive,
                roles));
        }

        [HttpPost]
        public async Task<ActionResult<AdminUserSummaryDto>> CreateUser([FromBody] AdminUserCreateRequest request)
        {
            var email = request.Email.Trim();
            var displayName = request.DisplayName.Trim();
            var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

            var exists = await _dbContext.Users.AnyAsync(u => u.Email == email);
            if (exists)
            {
                return Conflict(new { message = "Email already exists." });
            }

            var user = new User
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
                IsActive = true
            };

            _dbContext.Users.Add(user);

            var (roleIds, hasMissing) = await ResolveRoleIdsAsync(request.RoleIds, request.RoleCodes);
            if (hasMissing)
            {
                return BadRequest(new { message = "One or more role identifiers were not found." });
            }

            foreach (var roleId in roleIds)
            {
                _dbContext.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = roleId,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUser), new { userId = user.UserId }, new AdminUserSummaryDto(
                user.UserId,
                user.Email,
                user.DisplayName ?? displayName,
                user.IsActive,
                await GetRoleCodesForUserAsync(user.UserId)));
        }

        [HttpPut("{userId:guid}")]
        public async Task<ActionResult<AdminUserSummaryDto>> UpdateUser(Guid userId, [FromBody] AdminUserUpdateRequest request)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound();
            }

            var displayName = request.DisplayName.Trim();
            user.DisplayName = displayName;
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return Ok(new AdminUserSummaryDto(
                user.UserId,
                user.Email,
                user.DisplayName ?? displayName,
                user.IsActive,
                await GetRoleCodesForUserAsync(user.UserId)));
        }

        [HttpPut("{userId:guid}/roles")]
        public async Task<IActionResult> ReplaceUserRoles(Guid userId, [FromBody] AdminUserRoleSetRequest request)
        {
            var userExists = await _dbContext.Users.AnyAsync(u => u.UserId == userId);
            if (!userExists)
            {
                return NotFound();
            }

            var (roleIds, hasMissing) = await ResolveRoleIdsAsync(request.RoleIds, request.RoleCodes);
            if (hasMissing)
            {
                return BadRequest(new { message = "One or more role identifiers were not found." });
            }

            var existing = await _dbContext.UserRoles
                .Where(ur => ur.UserId == userId)
                .ToListAsync();

            _dbContext.UserRoles.RemoveRange(existing);

            foreach (var roleId in roleIds)
            {
                _dbContext.UserRoles.Add(new UserRole
                {
                    UserId = userId,
                    RoleId = roleId,
                    AssignedAt = DateTime.UtcNow
                });
            }

            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{userId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateUser(Guid userId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound();
            }

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{userId:guid}/activate")]
        public async Task<IActionResult> ActivateUser(Guid userId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null)
            {
                return NotFound();
            }

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        private async Task<IReadOnlyList<string>> GetRoleCodesForUserAsync(Guid userId)
        {
            return await _dbContext.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Code)
                .Distinct()
                .ToListAsync();
        }

        private async Task<(HashSet<Guid> RoleIds, bool HasMissing)> ResolveRoleIdsAsync(IReadOnlyList<Guid>? roleIds, IReadOnlyList<string>? roleCodes)
        {
            var ids = new HashSet<Guid>();
            var hasMissing = false;

            if (roleIds != null)
            {
                var requestedIds = roleIds.Distinct().ToList();
                var existingIds = await _dbContext.Roles
                    .Where(r => requestedIds.Contains(r.RoleId))
                    .Select(r => r.RoleId)
                    .ToListAsync();

                if (existingIds.Count != requestedIds.Count)
                {
                    hasMissing = true;
                }

                foreach (var id in existingIds)
                {
                    ids.Add(id);
                }
            }

            if (roleCodes != null && roleCodes.Count > 0)
            {
                var codes = roleCodes
                    .Select(c => c.Trim().ToUpperInvariant())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var matchingIds = await _dbContext.Roles
                    .Where(r => codes.Contains(r.Code))
                    .Select(r => r.RoleId)
                    .ToListAsync();

                if (matchingIds.Count != codes.Count)
                {
                    hasMissing = true;
                }

                foreach (var id in matchingIds)
                {
                    ids.Add(id);
                }
            }

            if (ids.Count == 0)
            {
                return (ids, hasMissing);
            }

            var activeRoles = await _dbContext.Roles
                .Where(r => ids.Contains(r.RoleId) && r.IsActive)
                .Select(r => r.RoleId)
                .ToListAsync();

            return (new HashSet<Guid>(activeRoles), hasMissing);
        }
    }
}
