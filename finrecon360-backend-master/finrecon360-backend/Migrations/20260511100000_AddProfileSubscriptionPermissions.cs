using System;
using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <summary>
    /// Adds permissions and component for profile subscription management.
    /// This allows tenants to view and manage their subscription from their profile page.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511100000_AddProfileSubscriptionPermissions")]
    public partial class AddProfileSubscriptionPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert new permissions for profile subscription management
            var now = DateTime.UtcNow;

            // PROFILE.SUBSCRIPTION.VIEW - Allows viewing subscription in profile
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.VIEW')
                BEGIN
                    INSERT INTO Permissions (PermissionId, Code, Name, Module, Description, CreatedAt)
                    VALUES (NEWID(), 'PROFILE.SUBSCRIPTION.VIEW', 'View Subscription', 'Profile', 'View own subscription details in profile', '{now:O}')
                END");

            // PROFILE.SUBSCRIPTION.CHANGE - Allows changing subscription from profile
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.CHANGE')
                BEGIN
                    INSERT INTO Permissions (PermissionId, Code, Name, Module, Description, CreatedAt)
                    VALUES (NEWID(), 'PROFILE.SUBSCRIPTION.CHANGE', 'Change Subscription', 'Profile', 'Change subscription plan from profile', '{now:O}')
                END");

            // PROFILE.SUBSCRIPTION.MANAGE - Combined manage permission
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.MANAGE')
                BEGIN
                    INSERT INTO Permissions (PermissionId, Code, Name, Module, Description, CreatedAt)
                    VALUES (NEWID(), 'PROFILE.SUBSCRIPTION.MANAGE', 'Manage Subscription', 'Profile', 'Full subscription management in profile', '{now:O}')
                END");

            // Add AppComponent for profile subscription management
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM AppComponents WHERE Code = 'PROFILE_SUBSCRIPTION_MGMT')
                BEGIN
                    INSERT INTO AppComponents (AppComponentId, Code, Name, RoutePath, Category, Description, IsActive, CreatedAt)
                    VALUES (NEWID(), 'PROFILE_SUBSCRIPTION_MGMT', 'Profile Subscription', '/app/profile', 'Profile', 'Profile subscription management', 1, '{now:O}')
                END");

            // Grant new permissions to ADMIN role
            migrationBuilder.Sql($@"
                DECLARE @adminRoleId UNIQUEIDENTIFIER = (SELECT RoleId FROM Roles WHERE Code = 'ADMIN')
                DECLARE @viewPermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.VIEW')
                DECLARE @changePermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.CHANGE')
                DECLARE @managePermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.MANAGE')

                IF @adminRoleId IS NOT NULL
                BEGIN
                    IF @viewPermissionId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @adminRoleId AND PermissionId = @viewPermissionId)
                        INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt) VALUES (@adminRoleId, @viewPermissionId, '{now:O}')
                    
                    IF @changePermissionId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @adminRoleId AND PermissionId = @changePermissionId)
                        INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt) VALUES (@adminRoleId, @changePermissionId, '{now:O}')
                    
                    IF @managePermissionId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @adminRoleId AND PermissionId = @managePermissionId)
                        INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt) VALUES (@adminRoleId, @managePermissionId, '{now:O}')
                END");

            // Grant VIEW permission to USER role (all users can view their subscription)
            migrationBuilder.Sql($@"
                DECLARE @userRoleId UNIQUEIDENTIFIER = (SELECT RoleId FROM Roles WHERE Code = 'USER')
                DECLARE @viewPermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.VIEW')

                IF @userRoleId IS NOT NULL AND @viewPermissionId IS NOT NULL 
                    AND NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = @userRoleId AND PermissionId = @viewPermissionId)
                BEGIN
                    INSERT INTO RolePermissions (RoleId, PermissionId, GrantedAt) VALUES (@userRoleId, @viewPermissionId, '{now:O}')
                END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove role permissions first (foreign key constraint)
            migrationBuilder.Sql(@"
                DECLARE @viewPermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.VIEW')
                DECLARE @changePermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.CHANGE')
                DECLARE @managePermissionId UNIQUEIDENTIFIER = (SELECT PermissionId FROM Permissions WHERE Code = 'PROFILE.SUBSCRIPTION.MANAGE')

                DELETE FROM RolePermissions WHERE PermissionId IN (@viewPermissionId, @changePermissionId, @managePermissionId)");

            // Delete the component
            migrationBuilder.Sql("DELETE FROM AppComponents WHERE Code = 'PROFILE_SUBSCRIPTION_MGMT'");

            // Delete the permissions
            migrationBuilder.Sql("DELETE FROM Permissions WHERE Code IN ('PROFILE.SUBSCRIPTION.VIEW', 'PROFILE.SUBSCRIPTION.CHANGE', 'PROFILE.SUBSCRIPTION.MANAGE')");
        }
    }
}
