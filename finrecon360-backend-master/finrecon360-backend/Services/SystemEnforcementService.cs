using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Admin;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    public enum EnforcementApplyResult
    {
        Success,
        Unauthorized,
        NotFound,
        InvalidTarget
    }

    public interface ISystemEnforcementService
    {
        Task<EnforcementApplyResult> ApplyTenantActionAsync(
            Guid tenantId,
            TenantStatus newStatus,
            EnforcementActionType actionType,
            EnforcementActionRequest request,
            CancellationToken cancellationToken = default);

        Task<EnforcementApplyResult> ReinstateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

        Task<EnforcementApplyResult> ApplyUserActionAsync(
            Guid tenantId,
            Guid userId,
            UserStatus newStatus,
            EnforcementActionType actionType,
            EnforcementActionRequest request,
            CancellationToken cancellationToken = default);

        Task<EnforcementApplyResult> ReinstateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    }

    public class SystemEnforcementService : ISystemEnforcementService
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;

        public SystemEnforcementService(AppDbContext dbContext, IUserContext userContext)
        {
            _dbContext = dbContext;
            _userContext = userContext;
        }

        public async Task<EnforcementApplyResult> ApplyTenantActionAsync(
            Guid tenantId,
            TenantStatus newStatus,
            EnforcementActionType actionType,
            EnforcementActionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_userContext.UserId is not { } actorId)
            {
                return EnforcementApplyResult.Unauthorized;
            }

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            tenant.Status = newStatus;

            _dbContext.EnforcementActions.Add(new EnforcementAction
            {
                EnforcementActionId = Guid.NewGuid(),
                TargetType = EnforcementTargetType.Tenant,
                TargetId = tenantId,
                ActionType = actionType,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? "No reason provided." : request.Reason.Trim(),
                CreatedBy = actorId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return EnforcementApplyResult.Success;
        }

        public async Task<EnforcementApplyResult> ReinstateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            if (_userContext.UserId is not { })
            {
                return EnforcementApplyResult.Unauthorized;
            }

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            if (tenant.Status != TenantStatus.Suspended)
            {
                return EnforcementApplyResult.InvalidTarget;
            }

            tenant.Status = TenantStatus.Active;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return EnforcementApplyResult.Success;
        }

        public async Task<EnforcementApplyResult> ApplyUserActionAsync(
            Guid tenantId,
            Guid userId,
            UserStatus newStatus,
            EnforcementActionType actionType,
            EnforcementActionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_userContext.UserId is not { } actorId)
            {
                return EnforcementApplyResult.Unauthorized;
            }

            var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            var membershipExists = await _dbContext.TenantUsers
                .AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenantId && tu.UserId == userId, cancellationToken);
            if (!membershipExists)
            {
                return EnforcementApplyResult.NotFound;
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            user.Status = newStatus;
            user.UpdatedAt = DateTime.UtcNow;

            _dbContext.EnforcementActions.Add(new EnforcementAction
            {
                EnforcementActionId = Guid.NewGuid(),
                TargetType = EnforcementTargetType.User,
                TargetId = userId,
                ActionType = actionType,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? "No reason provided." : request.Reason.Trim(),
                CreatedBy = actorId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return EnforcementApplyResult.Success;
        }

        public async Task<EnforcementApplyResult> ReinstateUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            if (_userContext.UserId is not { })
            {
                return EnforcementApplyResult.Unauthorized;
            }

            var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            var membershipExists = await _dbContext.TenantUsers
                .AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenantId && tu.UserId == userId, cancellationToken);
            if (!membershipExists)
            {
                return EnforcementApplyResult.NotFound;
            }

            // User reinstatement is tenant-scoped; if tenant itself is suspended/banned, keep user blocked.
            if (tenant.Status != TenantStatus.Active)
            {
                return EnforcementApplyResult.InvalidTarget;
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null)
            {
                return EnforcementApplyResult.NotFound;
            }

            if (user.Status != UserStatus.Suspended)
            {
                return EnforcementApplyResult.InvalidTarget;
            }

            user.Status = UserStatus.Active;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return EnforcementApplyResult.Success;
        }
    }
}
