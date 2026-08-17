using finrecon360_backend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace finrecon360_backend.Migrations
{
    /// <summary>
    /// Updates existing plans from USD (or NULL) to LKR as the default currency.
    /// This ensures all subscription pricing is consistent with the Sri Lankan Rupees standard.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260511150000_UpdatePlansCurrencyToLKR")]
    public partial class UpdatePlansCurrencyToLKR : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update all plans with USD or NULL currency to LKR
            migrationBuilder.Sql(@"
                UPDATE Plans 
                SET Currency = 'LKR' 
                WHERE Currency = 'USD' OR Currency IS NULL OR Currency = ''
            ");

            // Also ensure any new plans default to LKR (though this is also handled in model)
            // This is a safeguard for existing data
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert LKR back to USD (if needed for rollback)
            migrationBuilder.Sql(@"
                UPDATE Plans 
                SET Currency = 'USD' 
                WHERE Currency = 'LKR'
            ");
        }
    }
}
