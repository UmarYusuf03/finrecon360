namespace finrecon360_backend.Dtos.Admin
{
    public record ComponentSummaryDto(
        Guid Id,
        string Code,
        string Name,
        string RoutePath,
        string? Category,
        string? Description,
        bool IsActive);

    public class ComponentCreateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RoutePath { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Description { get; set; }
    }

    public class ComponentUpdateRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RoutePath { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Description { get; set; }
    }
}
