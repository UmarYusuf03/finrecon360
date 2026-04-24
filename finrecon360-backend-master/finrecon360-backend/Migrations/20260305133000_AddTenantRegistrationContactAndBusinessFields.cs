using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace finrecon360_backend.Migrations
{
    [DbContext(typeof(Data.AppDbContext))]
    [Migration("20260305133000_AddTenantRegistrationContactAndBusinessFields")]
    public partial class AddTenantRegistrationContactAndBusinessFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'PhoneNumber') IS NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests]
                    ADD [PhoneNumber] nvarchar(32) NOT NULL
                        CONSTRAINT [DF_TenantRegistrationRequests_PhoneNumber] DEFAULT (N'');
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'BusinessRegistrationNumber') IS NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests]
                    ADD [BusinessRegistrationNumber] nvarchar(128) NOT NULL
                        CONSTRAINT [DF_TenantRegistrationRequests_BusinessRegistrationNumber] DEFAULT (N'');
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'BusinessType') IS NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests]
                    ADD [BusinessType] nvarchar(64) NOT NULL
                        CONSTRAINT [DF_TenantRegistrationRequests_BusinessType] DEFAULT (N'VEHICLE_RENTAL');
                END
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'BusinessType') IS NOT NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests] DROP CONSTRAINT [DF_TenantRegistrationRequests_BusinessType];
                    ALTER TABLE [TenantRegistrationRequests] DROP COLUMN [BusinessType];
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'BusinessRegistrationNumber') IS NOT NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests] DROP CONSTRAINT [DF_TenantRegistrationRequests_BusinessRegistrationNumber];
                    ALTER TABLE [TenantRegistrationRequests] DROP COLUMN [BusinessRegistrationNumber];
                END
                """
            );

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('TenantRegistrationRequests', 'PhoneNumber') IS NOT NULL
                BEGIN
                    ALTER TABLE [TenantRegistrationRequests] DROP CONSTRAINT [DF_TenantRegistrationRequests_PhoneNumber];
                    ALTER TABLE [TenantRegistrationRequests] DROP COLUMN [PhoneNumber];
                END
                """
            );
        }
    }
}
