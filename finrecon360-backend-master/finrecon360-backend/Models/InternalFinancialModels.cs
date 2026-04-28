namespace finrecon360_backend.Models
{
    public enum SystemTransactionType
    {
        Credit = 0,
        Debit = 1
    }

    public enum InvoiceStatus
    {
        Draft = 0,
        Sent = 1,
        Paid = 2,
        Overdue = 3
    }

    public enum PaymentGatewayPayoutStatus
    {
        Pending = 0,
        Settled = 1
    }

    public class SystemTransaction
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateTime TransactionDate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public SystemTransactionType Type { get; set; }
        public bool IsReconciled { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }

    public class Customer
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? ContactNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    }

    public class Invoice
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime DueDate { get; set; }
        public decimal TotalAmount { get; set; }
        public InvoiceStatus Status { get; set; }
        public bool IsReconciled { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public Customer Customer { get; set; } = default!;
    }

    public class PaymentGatewayPayout
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public DateTime PayoutDate { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal ProcessingFees { get; set; }
        public decimal NetAmount { get; set; }
        public PaymentGatewayPayoutStatus Status { get; set; }
        public bool IsReconciled { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    public class Order
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string? CustomerName { get; set; }
        public Guid? PaymentGatewayPayoutId { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }

        public PaymentGatewayPayout? PaymentGatewayPayout { get; set; }
    }

    public class BankAccount
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}