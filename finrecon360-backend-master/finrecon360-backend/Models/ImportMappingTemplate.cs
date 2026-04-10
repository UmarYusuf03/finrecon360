namespace finrecon360_backend.Models
{
    public class ImportMappingTemplate
    {
        public Guid ImportMappingTemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string CanonicalSchemaVersion { get; set; } = "v1";
        public string MappingJson { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ImportBatch> Batches { get; set; } = new List<ImportBatch>();
    }
}
