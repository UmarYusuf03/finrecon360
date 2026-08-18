using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAlertsAndBillingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: This migration intentionally does not touch Users.PasswordHash/ExternalProvider/
            // ExternalProviderId. Those columns are pre-existing (added by the hand-written, idempotent
            // 20260816081641_AddSsoIdentityColumnsToUsers migration via raw SQL), but that migration never
            // updated the EF model snapshot, so `dotnet ef migrations add` tried to re-add them here as a
            // diff. Re-adding them with typed AddColumn/AlterColumn calls would fail against any database
            // that already ran the SSO migration, so they were removed from this migration by hand.

            migrationBuilder.CreateTable(
                name: "SystemBillingSettings",
                columns: table => new
                {
                    SystemBillingSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentOverdueSuspensionThresholdDays = table.Column<int>(type: "int", nullable: false, defaultValue: 7),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemBillingSettings", x => x.SystemBillingSettingsId);
                });

            migrationBuilder.CreateTable(
                name: "TenantPaymentAlerts",
                columns: table => new
                {
                    TenantPaymentAlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodEndAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DaysOverdue = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentAlerts", x => x.TenantPaymentAlertId);
                    table.ForeignKey(
                        name: "FK_TenantPaymentAlerts_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "SubscriptionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantPaymentAlerts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentAlerts_SubscriptionId_Status",
                table: "TenantPaymentAlerts",
                columns: new[] { "SubscriptionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentAlerts_TenantId",
                table: "TenantPaymentAlerts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemBillingSettings");

            migrationBuilder.DropTable(
                name: "TenantPaymentAlerts");
        }
    }
}
