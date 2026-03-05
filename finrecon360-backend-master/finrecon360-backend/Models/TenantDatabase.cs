namespace finrecon360_backend.Models
{
    public class TenantDatabase
    {
        public Guid TenantDatabaseId { get; set; }
        public Guid TenantId { get; set; }
        public string EncryptedConnectionString { get; set; } = string.Empty;
        public string Provider { get; set; } = "SqlServer";
        public DateTime CreatedAt { get; set; }
        public DateTime? ProvisionedAt { get; set; }
        public TenantDatabaseStatus Status { get; set; }

        public Tenant Tenant { get; set; } = default!;
    }
}
