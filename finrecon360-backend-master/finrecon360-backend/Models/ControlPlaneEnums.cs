namespace finrecon360_backend.Models
{
    public enum UserStatus
    {
        Invited = 0,
        Active = 1,
        Suspended = 2,
        Banned = 3
    }

    public enum TenantStatus
    {
        Pending = 0,
        Active = 1,
        Suspended = 2,
        Banned = 3,
        Rejected = 4
    }

    public enum TenantDatabaseStatus
    {
        Provisioning = 0,
        Ready = 1,
        Failed = 2
    }

    public enum TenantUserRole
    {
        TenantAdmin = 0,
        TenantUser = 1
    }

    public enum SubscriptionStatus
    {
        PendingPayment = 0,
        Active = 1,
        PastDue = 2,
        Canceled = 3
    }

    public enum PaymentSessionStatus
    {
        Created = 0,
        Paid = 1,
        Failed = 2
    }

    public enum EnforcementTargetType
    {
        Tenant = 0,
        User = 1
    }

    public enum EnforcementActionType
    {
        Suspend = 0,
        Ban = 1
    }
}
