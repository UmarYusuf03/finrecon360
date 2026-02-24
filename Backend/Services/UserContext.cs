using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace finrecon360_backend.Services
{
    public class UserContext : IUserContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AppDbContext _dbContext;
        private Guid? _userId;
        private bool? _isActive;

        public UserContext(IHttpContextAccessor httpContextAccessor, AppDbContext dbContext)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
        }

        public Guid? UserId
        {
            get
            {
                if (_userId.HasValue)
                {
                    return _userId;
                }

                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null || user.Identity?.IsAuthenticated != true)
                {
                    return null;
                }

                var idValue = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

                if (Guid.TryParse(idValue, out var parsed))
                {
                    _userId = parsed;
                    return parsed;
                }

                return null;
            }
        }

        public string? Email
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                return user?.FindFirstValue(JwtRegisteredClaimNames.Email)
                    ?? user?.FindFirstValue(ClaimTypes.Email);
            }
        }

        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public bool IsActive
        {
            get
            {
                if (_isActive.HasValue)
                {
                    return _isActive.Value;
                }

                if (UserId is not { } userId)
                {
                    _isActive = false;
                    return false;
                }

                var isActive = _dbContext.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => u.IsActive)
                    .FirstOrDefault();

                _isActive = isActive;
                return isActive;
            }
        }
    }
}
