using finrecon360_backend.Data;
using finrecon360_backend.Models;

namespace finrecon360_backend.Services
{
    public class AuditLogger : IAuditLogger
    {
        private readonly AppDbContext _dbContext;

        public AuditLogger(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task LogAsync(Guid? userId, string action, string? entity = null, string? entityId = null, string? metadata = null)
        {
            var log = new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Metadata = metadata,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AuditLogs.Add(log);
            await _dbContext.SaveChangesAsync();
        }
    }
}
