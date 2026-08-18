using System.ComponentModel.DataAnnotations;

namespace finrecon360_backend.Dtos.Reporting
{
    public record ReportScheduleResponse(
        Guid ReportScheduleId,
        string ReportType,
        string Format,
        int DayOfWeek,
        string RecipientEmail,
        bool IsActive,
        DateTime? LastRunAt,
        DateTime NextRunAt,
        DateTime CreatedAt);

    public class CreateReportScheduleRequest
    {
        [Required]
        public string ReportType { get; set; } = string.Empty;

        [Required]
        public string Format { get; set; } = "csv";

        [Range(0, 6)]
        public int DayOfWeek { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string RecipientEmail { get; set; } = string.Empty;
    }

    public class UpdateReportScheduleActiveRequest
    {
        public bool IsActive { get; set; }
    }
}
