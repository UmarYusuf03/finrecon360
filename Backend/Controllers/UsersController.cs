using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Users;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Controllers
{
    [ApiController]
    [Route("api/users/profile")]
    [Authorize]
    [EnableRateLimiting("me")]
    public class UsersController : ControllerBase
    {
        private const long MaxProfileImageBytes = 2 * 1024 * 1024;
        private readonly AppDbContext _dbContext;
        private readonly IUserContext _userContext;

        public UsersController(AppDbContext dbContext, IUserContext userContext)
        {
            _dbContext = dbContext;
            _userContext = userContext;
        }

        [HttpGet]
        public async Task<ActionResult<UserProfileDto>> GetProfile()
        {
            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null)
            {
                return NotFound();
            }

            return Ok(new UserProfileDto(
                user.UserId,
                user.Email,
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.ProfileImage != null,
                user.IsActive));
        }

        [HttpPut]
        public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null)
            {
                return NotFound();
            }

            if (request.DisplayName != null)
            {
                user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
            }

            if (request.FirstName != null)
            {
                user.FirstName = request.FirstName.Trim();
            }

            if (request.LastName != null)
            {
                user.LastName = request.LastName.Trim();
            }

            if (request.PhoneNumber != null)
            {
                user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(new UserProfileDto(
                user.UserId,
                user.Email,
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.ProfileImage != null,
                user.IsActive));
        }

        [HttpPost("photo")]
        [RequestSizeLimit(MaxProfileImageBytes)]
        public async Task<IActionResult> UploadProfilePhoto([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "No file uploaded." });
            }

            if (file.Length > MaxProfileImageBytes)
            {
                return BadRequest(new { message = "Profile image is too large." });
            }

            if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only image uploads are supported." });
            }

            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null)
            {
                return NotFound();
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            user.ProfileImage = stream.ToArray();
            user.ProfileImageContentType = file.ContentType;
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("photo")]
        public async Task<IActionResult> GetProfilePhoto()
        {
            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null || user.ProfileImage == null)
            {
                return NotFound();
            }

            return File(user.ProfileImage, user.ProfileImageContentType ?? "application/octet-stream");
        }

        [HttpDelete("photo")]
        public async Task<IActionResult> DeleteProfilePhoto()
        {
            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null)
            {
                return NotFound();
            }

            user.ProfileImage = null;
            user.ProfileImageContentType = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = _userContext.UserId;
            if (userId is null)
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user is null)
            {
                return NotFound();
            }

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}
