using Microsoft.AspNetCore.Http;

namespace finrecon360_backend.Dtos.Reconciliation
{
    /// <summary>
    /// Request payload for uploading a bank statement file.
    /// </summary>
    public class UploadStatementRequest
    {
        /// <summary>
        /// Statement file to upload.
        /// </summary>
        public IFormFile File { get; set; } = default!;

        /// <summary>
        /// Bank account identifier associated with this statement.
        /// </summary>
        public Guid BankAccountId { get; set; }
    }

    /// <summary>
    /// Response model representing a bank statement import result.
    /// </summary>
    public class StatementImportResponse
    {
        /// <summary>
        /// Unique identifier of the import.
        /// </summary>
        public Guid ImportId { get; set; }

        /// <summary>
        /// Date and time when the import was created.
        /// </summary>
        public DateTime ImportDate { get; set; }

        /// <summary>
        /// Bank account identifier linked to the import.
        /// </summary>
        public Guid BankAccountId { get; set; }

        /// <summary>
        /// Current status of the import.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Total number of statement lines imported.
        /// </summary>
        public int TotalLinesImported { get; set; }
    }
}