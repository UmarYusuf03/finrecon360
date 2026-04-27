using finrecon360_backend.Data;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace finrecon360_backend.Services
{
    public record TenantResolution(Guid TenantId, TenantStatus Status, string Name);

    public interface ITenantContext
    {
        Task<TenantResolution?> ResolveAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// WHY: Facilitates the active routing of the user to the correct Tenant DB. It prefers 
    /// explicit client intent via the `X-Tenant-Id` header but falls back securely.
    /// </summary>
    public class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AppDbContext _dbContext;
        private TenantResolution? _cached;
        private bool _resolved;

        public TenantContext(IHttpContextAccessor httpContextAccessor, AppDbContext dbContext)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
        }

        public async Task<TenantResolution?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            if (_resolved)
            {
                return _cached;
            }

            _resolved = true;

            var userId = GetUserId();
            if (userId == null)
            {
                return null;
            }

            var headerTenantId = GetTenantIdFromHeader();
            if (headerTenantId.HasValue)
            {
                var tenant = await _dbContext.TenantUsers
                    .AsNoTracking()
                    .Where(tu => tu.UserId == userId.Value && tu.TenantId == headerTenantId.Value)
                    .Select(tu => new TenantResolution(tu.TenantId, tu.Tenant.Status, tu.Tenant.Name))
                    .FirstOrDefaultAsync(cancellationToken);

                if (tenant != null)
                {
                    _cached = tenant;
                    return tenant;
                }

                // Stale/invalid tenant header: continue to deterministic fallback using memberships.
            }

            var tenants = await _dbContext.TenantUsers
                .AsNoTracking()
                .Where(tu => tu.UserId == userId.Value)
                .Select(tu => new
                {
                    Resolution = new TenantResolution(tu.TenantId, tu.Tenant.Status, tu.Tenant.Name),
                    tu.Role,
                    tu.Tenant.ActivatedAt,
                    tu.Tenant.CreatedAt
                })
                .ToListAsync(cancellationToken);

            if (tenants.Count == 0)
            {
                _cached = null;
                return null;
            }

            if (tenants.Count == 1)
            {
                _cached = tenants[0].Resolution;
                return _cached;
            }

            // WHY: Deterministic fallback for multi-tenant users when the client has not supplied X-Tenant-Id.
            // Prefer active tenants and admin memberships so UI/auth state remains stable after refresh/login.
            _cached = tenants
                .OrderByDescending(t => t.Resolution.Status == TenantStatus.Active)
                .ThenByDescending(t => t.Role == TenantUserRole.TenantAdmin)
                .ThenByDescending(t => t.ActivatedAt ?? DateTime.MinValue)
                .ThenByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Resolution.TenantId)
                .Select(t => t.Resolution)
                .First();
            return _cached;
        }

        private Guid? GetUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var idValue = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

            if (Guid.TryParse(idValue, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private Guid? GetTenantIdFromHeader()
        {
            var header = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
            if (Guid.TryParse(header, out var tenantId))
            {
                return tenantId;
            }

            return null;
        }
    }
}
