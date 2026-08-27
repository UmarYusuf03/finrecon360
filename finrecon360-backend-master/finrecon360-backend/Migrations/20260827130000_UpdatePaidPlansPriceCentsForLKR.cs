using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260827130000_UpdatePaidPlansPriceCentsForLKR")]
    public partial class UpdatePaidPlansPriceCentsForLKR : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert $49, $149, $499 USD to approximate LKR values (300 LKR = 1 USD).
            // LKR 14,700, 44,700, 149,700 => multiplied by 100 for cents.
            migrationBuilder.Sql(@"
                UPDATE Plans SET PriceCents = 1470000 WHERE Code = 'STARTER';
                UPDATE Plans SET PriceCents = 4470000 WHERE Code = 'GROWTH';
                UPDATE Plans SET PriceCents = 14970000 WHERE Code = 'ENTERPRISE';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert back to original USD-based cent values
            migrationBuilder.Sql(@"
                UPDATE Plans SET PriceCents = 4900 WHERE Code = 'STARTER';
                UPDATE Plans SET PriceCents = 14900 WHERE Code = 'GROWTH';
                UPDATE Plans SET PriceCents = 49900 WHERE Code = 'ENTERPRISE';
            ");
        }
    }
}
