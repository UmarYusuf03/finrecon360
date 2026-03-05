/**
 * User Roles Enum
 * 
 * Defines all user roles in the FinRecon360 system.
 * These roles are used for authorization and access control.
 * 
 * The backend (IdentityServer) will use these same role names
 * for consistent role-based access control (RBAC).
 */
export enum UserRole {
    /**
     * System Administrator - Full access to all modules and settings
     */
    ADMIN = 'Admin',

    /**
     * Finance Manager - Can manage financial operations across all modules
     */
    FINANCE_MANAGER = 'FinanceManager',

    /**
     * Accountant - Can perform day-to-day accounting tasks
     */
    ACCOUNTANT = 'Accountant',

    /**
     * Auditor - Read-only access for audit purposes
     */
    AUDITOR = 'Auditor',

    /**
     * Matcher User - Access to transaction matching module
     */
    MATCHER_USER = 'MatcherUser',

    /**
     * Balancer User - Access to account reconciliation module
     */
    BALANCER_USER = 'BalancerUser',

    /**
     * Task Manager User - Access to close task management
     */
    TASK_MANAGER_USER = 'TaskManagerUser',

    /**
     * Journal Entry User - Can create and manage journal entries
     */
    JOURNAL_ENTRY_USER = 'JournalEntryUser',

    /**
     * Viewer - Read-only access to analytics and reports
     */
    VIEWER = 'Viewer',

    /**
     * Regular User - Basic authenticated user with limited access
     */
    USER = 'User',
}

/**
 * Helper function to check if a role has admin privileges
 * @param role - The user role to check
 * @returns true if the role is Admin or Finance Manager
 */
export function isAdminRole(role: UserRole): boolean {
    return role === UserRole.ADMIN || role === UserRole.FINANCE_MANAGER;
}

/**
 * Helper function to get display name for a role
 * @param role - The user role
 * @returns human-readable role name
 */
export function getRoleDisplayName(role: UserRole): string {
    const displayNames: Record<UserRole, string> = {
        [UserRole.ADMIN]: 'Administrator',
        [UserRole.FINANCE_MANAGER]: 'Finance Manager',
        [UserRole.ACCOUNTANT]: 'Accountant',
        [UserRole.AUDITOR]: 'Auditor',
        [UserRole.MATCHER_USER]: 'Matcher User',
        [UserRole.BALANCER_USER]: 'Balancer User',
        [UserRole.TASK_MANAGER_USER]: 'Task Manager User',
        [UserRole.JOURNAL_ENTRY_USER]: 'Journal Entry User',
        [UserRole.VIEWER]: 'Viewer',
        [UserRole.USER]: 'User',
    };
    return displayNames[role];
}
