using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Me;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace finrecon360_backend.Controllers
{
    [ApiController]
    [Route("api/me")]
    [Authorize]
    [EnableRateLimiting("me")]
    public class MeController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;
        private readonly IPermissionService _permissionService;

        public MeController(AppDbContext dbContext, IUserContext userContext, IPermissionService permissionService)
        {
            _dbContext = dbContext;
            _userContext = userContext;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<MeResponse>> Get()
        {
            if (_userContext.UserId is not { } userId)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user is null)
            {
                return NotFound();
            }

            if (!user.IsActive)
            {
                return Forbid();
            }

            var displayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim();
            var roles = await _permissionService.GetRolesForUserAsync(userId);
            var permissions = await _permissionService.GetPermissionsForUserAsync(userId);

            return Ok(new MeResponse(
                user.UserId,
                user.Email,
                displayName,
                roles.ToList(),
                permissions.OrderBy(p => p).ToList()));
        }
    }
}
