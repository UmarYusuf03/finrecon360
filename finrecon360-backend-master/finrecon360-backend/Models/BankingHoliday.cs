namespace finrecon360_backend.Models
{
    /// <summary>
    /// A single non-business day (weekend excluded — that's handled separately) for the tenant's
    /// settlement-date-window calculations. Admin-maintained per tenant rather than sourced from
    /// a public-holiday library, since bank operating calendars don't reliably match public
    /// holidays and no such package exists in this project.
    /// </summary>
    public class BankingHoliday
    {
        public Guid BankingHolidayId { get; set; }
        public DateOnly Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
