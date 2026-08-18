namespace finrecon360_backend.Models
{
    public class ImportMappingTemplate
    {
        public Guid ImportMappingTemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string CanonicalSchemaVersion { get; set; } = "v1";
        public string MappingJson { get; set; } = string.Empty;

        // Optional JSON dict of {"BatchNumber": "regex", "TerminalId": "regex", "MerchantId": "regex"}
        // (each with exactly one capture group), applied against the mapped Description value at
        // commit time by PosIdentifierExtractor. Separate from MappingJson (a flat column-name
        // lookup) since this is pattern extraction from unstructured narrative text, not a
        // column rename — keeping them separate avoids changing MappingJson's shape for every
        // existing consumer (SaveMapping/DeserializeMappings, the workbench UI).
        public string? ExtractionPatternsJson { get; set; }
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ImportBatch> Batches { get; set; } = new List<ImportBatch>();
    }
}
