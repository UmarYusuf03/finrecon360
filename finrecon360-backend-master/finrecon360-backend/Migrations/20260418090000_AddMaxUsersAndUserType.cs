using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace finrecon360_backend.Migrations
{
    [DbContext(typeof(Data.AppDbContext))]
    [Migration("20260418090000_AddMaxUsersAndUserType")]
    public partial class AddMaxUsersAndUserType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Plans', 'MaxUsers') IS NULL
BEGIN
    ALTER TABLE [Plans] ADD [MaxUsers] int NOT NULL CONSTRAINT [DF_Plans_MaxUsers] DEFAULT (10);
    EXEC(N'UPDATE [Plans] SET [MaxUsers] = [MaxAccounts] WHERE [MaxUsers] = 10;');
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Users', 'UserType') IS NULL
BEGIN
    ALTER TABLE [Users] ADD [UserType] nvarchar(32) NOT NULL CONSTRAINT [DF_Users_UserType] DEFAULT (N'GlobalPublic');

        EXEC(N'UPDATE [Users] SET [UserType] = N''SystemAdmin'' WHERE [IsSystemAdmin] = 1;');

        EXEC(N'UPDATE u SET [UserType] = N''TenantOperational'' FROM [Users] u WHERE u.[IsSystemAdmin] = 0 AND EXISTS (SELECT 1 FROM [TenantUsers] tu WHERE tu.[UserId] = u.[UserId]);');
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Users', 'UserType') IS NOT NULL
BEGIN
    DECLARE @dfUsersUserType nvarchar(128);
    SELECT @dfUsersUserType = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[Users]') AND c.name = N'UserType';

    IF @dfUsersUserType IS NOT NULL
        EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @dfUsersUserType + ']');

    ALTER TABLE [Users] DROP COLUMN [UserType];
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Plans', 'MaxUsers') IS NOT NULL
BEGIN
    DECLARE @dfPlansMaxUsers nvarchar(128);
    SELECT @dfPlansMaxUsers = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[Plans]') AND c.name = N'MaxUsers';

    IF @dfPlansMaxUsers IS NOT NULL
        EXEC(N'ALTER TABLE [Plans] DROP CONSTRAINT [' + @dfPlansMaxUsers + ']');

    ALTER TABLE [Plans] DROP COLUMN [MaxUsers];
END
");
        }
    }
}
