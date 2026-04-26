namespace finrecon360_backend.Models
{
    public enum TransactionType
    {
        CashIn,
        CashOut
    }

    public enum PaymentMethod
    {
        Cash,
        Card
    }

    public enum TransactionState
    {
        Pending,
        Approved,
        Rejected,
        // Card cash-outs pause here until the bank-match phase promotes them to JournalReady.
        NeedsBankMatch,
        JournalReady
    }
}
