namespace finrecon360_backend.Models
{
    public class AppComponent
    {
        public Guid AppComponentId { get; set; }
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string RoutePath { get; set; } = default!;
        public string? Category { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
