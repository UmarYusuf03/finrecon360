using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <summary>
    /// Adds the columns needed for external-provider (SSO) sign-in.
    ///
    /// - ExternalProvider / ExternalProviderId record which provider vouched for the account and
    ///   that provider's own immutable id for the person. Matching on the provider id rather than
    ///   the email means an account survives the user renaming their email at the provider.
    /// - PasswordHash becomes nullable, because an account that only ever signs in through Google
    ///   genuinely has no password. Storing a placeholder hash instead would be a value that some
    ///   input could, in principle, satisfy.
    ///
    /// Written by hand rather than scaffolded, in the style of the other manual migrations here,
    /// and made idempotent so it is safe to re-run against databases that are already partly
    /// migrated — this project has a history of schema drift between environments.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260816081641_AddSsoIdentityColumnsToUsers")]
    public partial class AddSsoIdentityColumnsToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF COL_LENGTH('Users', 'ExternalProvider') IS NULL
                    ALTER TABLE [Users] ADD [ExternalProvider] nvarchar(64) NULL;
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('Users', 'ExternalProviderId') IS NULL
                    ALTER TABLE [Users] ADD [ExternalProviderId] nvarchar(256) NULL;
            ");

            // Relax PasswordHash to nullable for SSO-only accounts.
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'PasswordHash' AND IS_NULLABLE = 'NO')
                    ALTER TABLE [Users] ALTER COLUMN [PasswordHash] nvarchar(max) NULL;
            ");

            // One account per provider identity. Filtered so the many password-only accounts,
            // which leave both columns null, do not collide with each other.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_Users_ExternalProvider_ExternalProviderId'
                      AND object_id = OBJECT_ID('dbo.Users'))
                    CREATE UNIQUE INDEX [IX_Users_ExternalProvider_ExternalProviderId]
                        ON [Users]([ExternalProvider], [ExternalProviderId])
                        WHERE [ExternalProvider] IS NOT NULL AND [ExternalProviderId] IS NOT NULL;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = 'IX_Users_ExternalProvider_ExternalProviderId'
                      AND object_id = OBJECT_ID('dbo.Users'))
                    DROP INDEX [IX_Users_ExternalProvider_ExternalProviderId] ON [Users];
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('Users', 'ExternalProviderId') IS NOT NULL
                    ALTER TABLE [Users] DROP COLUMN [ExternalProviderId];
            ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('Users', 'ExternalProvider') IS NOT NULL
                    ALTER TABLE [Users] DROP COLUMN [ExternalProvider];
            ");

            // Deliberately not reverting PasswordHash to NOT NULL: any SSO account created while
            // this migration was applied has a null there, and the tightening would fail.
        }
    }
}
