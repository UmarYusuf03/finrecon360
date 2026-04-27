using System.ComponentModel.DataAnnotations;

namespace finrecon360_backend.Dtos.BankAccounts
{
    public class CreateBankAccountRequest
    {
        [Required]
        [MaxLength(200)]
        public string BankName { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string AccountName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string AccountNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Currency { get; set; } = string.Empty;
    }

    public class UpdateBankAccountRequest
    {
        public string? BankName { get; set; }
        public string? AccountName { get; set; }
        public string? AccountNumber { get; set; }
        public string? Currency { get; set; }
        public bool? IsActive { get; set; }
    }

    public record BankAccountResponse(
        Guid BankAccountId,
        string BankName,
        string AccountName,
        string AccountNumber,
        string Currency,
        bool IsActive,
        DateTime CreatedAt,
        DateTime? UpdatedAt);
}
