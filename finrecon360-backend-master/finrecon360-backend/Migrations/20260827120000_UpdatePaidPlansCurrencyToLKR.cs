using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <summary>
    /// Starter/Growth/Enterprise were re-entered with Currency = 'USD' after the earlier
    /// UpdatePlansCurrencyToLKR migration ran. PayHereCheckoutService previously ignored a
    /// plan's Currency entirely and always charged in PAYHERE_CURRENCY (LKR), masking this;
    /// now that it honors the plan's currency, these rows need to actually say LKR.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260827120000_UpdatePaidPlansCurrencyToLKR")]
    public partial class UpdatePaidPlansCurrencyToLKR : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Plans
                SET Currency = 'LKR'
                WHERE Code IN ('STARTER', 'GROWTH', 'ENTERPRISE') AND Currency = 'USD'
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Plans
                SET Currency = 'USD'
                WHERE Code IN ('STARTER', 'GROWTH', 'ENTERPRISE') AND Currency = 'LKR'
            ");
        }
    }
}
