namespace finrecon360_backend.Dtos.Admin
{
    public record ActionSummaryDto(
        Guid Id,
        string Code,
        string Name,
        string? Description,
        bool IsActive);

    public class ActionCreateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class ActionUpdateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
