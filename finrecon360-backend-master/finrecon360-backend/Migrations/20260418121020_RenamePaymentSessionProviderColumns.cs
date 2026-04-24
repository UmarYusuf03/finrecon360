using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <inheritdoc />
    public partial class RenamePaymentSessionProviderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('PaymentSessions', 'StripeSessionId') IS NOT NULL
    AND COL_LENGTH('PaymentSessions', 'ProviderSessionId') IS NULL
BEGIN
    EXEC sp_rename 'PaymentSessions.StripeSessionId', 'ProviderSessionId', 'COLUMN';
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('PaymentSessions', 'StripeCustomerId') IS NOT NULL
    AND COL_LENGTH('PaymentSessions', 'ProviderReferenceId') IS NULL
BEGIN
    EXEC sp_rename 'PaymentSessions.StripeCustomerId', 'ProviderReferenceId', 'COLUMN';
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentSessions_StripeSessionId' AND object_id = OBJECT_ID(N'[PaymentSessions]'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentSessions_ProviderSessionId' AND object_id = OBJECT_ID(N'[PaymentSessions]'))
BEGIN
    EXEC sp_rename N'PaymentSessions.IX_PaymentSessions_StripeSessionId', N'IX_PaymentSessions_ProviderSessionId', N'INDEX';
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('PaymentSessions', 'ProviderSessionId') IS NOT NULL
    AND COL_LENGTH('PaymentSessions', 'StripeSessionId') IS NULL
BEGIN
    EXEC sp_rename 'PaymentSessions.ProviderSessionId', 'StripeSessionId', 'COLUMN';
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('PaymentSessions', 'ProviderReferenceId') IS NOT NULL
    AND COL_LENGTH('PaymentSessions', 'StripeCustomerId') IS NULL
BEGIN
    EXEC sp_rename 'PaymentSessions.ProviderReferenceId', 'StripeCustomerId', 'COLUMN';
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentSessions_ProviderSessionId' AND object_id = OBJECT_ID(N'[PaymentSessions]'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentSessions_StripeSessionId' AND object_id = OBJECT_ID(N'[PaymentSessions]'))
BEGIN
    EXEC sp_rename N'PaymentSessions.IX_PaymentSessions_ProviderSessionId', N'IX_PaymentSessions_StripeSessionId', N'INDEX';
END
");
        }
    }
}
