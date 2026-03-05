namespace finrecon360_backend.Models
{
    public class EnforcementAction
    {
        public Guid EnforcementActionId { get; set; }
        public EnforcementTargetType TargetType { get; set; }
        public Guid TargetId { get; set; }
        public EnforcementActionType ActionType { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
