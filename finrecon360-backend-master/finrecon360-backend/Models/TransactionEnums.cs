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
        NeedsBankMatch,
        JournalReady
    }
}
