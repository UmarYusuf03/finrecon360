using System.ComponentModel.DataAnnotations.Schema;

namespace finrecon360_backend.Models
{
    public class ImportBatch
    {
        public Guid ImportBatchId { get; set; }
        public Guid? MappingTemplateId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ImportedAt { get; set; }
        public Guid? UploadedByUserId { get; set; }
        public string? OriginalFileName { get; set; }
        public int RawRecordCount { get; set; }
        public int NormalizedRecordCount { get; set; }
        public string? ErrorMessage { get; set; }

        [NotMapped]
        public string? FileName
        {
            get => OriginalFileName;
            set => OriginalFileName = value;
        }

        [NotMapped]
        public DateTime CreatedAt
        {
            get => ImportedAt;
            set => ImportedAt = value;
        }

        public ImportMappingTemplate? MappingTemplate { get; set; }
        public ICollection<ImportedRawRecord> RawRecords { get; set; } = new List<ImportedRawRecord>();
        public ICollection<ImportedNormalizedRecord> NormalizedRecords { get; set; } = new List<ImportedNormalizedRecord>();
    }
}
