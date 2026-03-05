namespace finrecon360_backend.Dtos.Admin
{
    public class EnforcementActionRequest
    {
        public string Reason { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }
}
