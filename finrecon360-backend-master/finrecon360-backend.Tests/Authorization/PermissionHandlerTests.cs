using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using finrecon360_backend.Authorization;
using Xunit;

namespace finrecon360_backend.Tests.Authorization
{
    /// <summary>
    /// PermissionHandler no longer exposes a static ExpandPermissions helper — authorization now
    /// resolves a required permission against a reverse AliasMap (requirement -> permissions that
    /// satisfy it) inline in HandleRequirementAsync. These tests exercise that AliasMap directly.
    /// </summary>
    public class PermissionHandlerTests
    {
        private static IReadOnlyDictionary<string, string[]> AliasMap
        {
            get
            {
                var field = typeof(PermissionHandler).GetField("AliasMap", BindingFlags.NonPublic | BindingFlags.Static)!;
                return (IReadOnlyDictionary<string, string[]>)field.GetValue(null)!;
            }
        }

        [Fact]
        public void AliasMap_ImportsView_IsSatisfiedByScopedPosCreate()
        {
            var aliases = AliasMap["ADMIN.IMPORTS.VIEW"];

            Assert.Contains("ADMIN.IMPORTS.POS.CREATE", aliases);
        }

        [Fact]
        public void AliasMap_ImportsView_IsSatisfiedByLegacyWorkbenchPermissions()
        {
            var aliases = AliasMap["ADMIN.IMPORTS.VIEW"];

            Assert.Contains("ADMIN.IMPORT_WORKBENCH.VIEW", aliases);
            Assert.Contains("ADMIN.IMPORT_WORKBENCH.MANAGE", aliases);
        }

        [Fact]
        public void AliasMap_ScopedPosCreate_IsSatisfiedByFullImportsCreateOrManage()
        {
            var aliases = AliasMap["ADMIN.IMPORTS.POS.CREATE"];

            Assert.Contains("ADMIN.IMPORTS.CREATE", aliases);
            Assert.Contains("ADMIN.IMPORTS.MANAGE", aliases);
        }

        [Fact]
        public void AliasMap_ReconciliationView_IsSatisfiedByScopedResolvePermissions()
        {
            var aliases = AliasMap["ADMIN.RECONCILIATION.VIEW"];

            Assert.Contains("MATCHER.VIEW", aliases);
            Assert.Contains("MATCHER.MANAGE", aliases);
            Assert.Contains("ADMIN.RECONCILIATION.POS.RESOLVE", aliases);
            Assert.Contains("ADMIN.RECONCILIATION.ERP.RESOLVE", aliases);
            Assert.Contains("ADMIN.RECONCILIATION.GATEWAY.RESOLVE", aliases);
            Assert.Contains("ADMIN.RECONCILIATION.BANK.RESOLVE", aliases);
        }

        [Fact]
        public void AliasMap_LegacyRoleManagementAlias_MapsToCurrentPermission()
        {
            var aliases = AliasMap["ROLE_MANAGEMENT"];

            Assert.Single(aliases);
            Assert.Contains("ADMIN.ROLES.MANAGE", aliases);
        }

        [Fact]
        public void AliasMap_UnknownPermission_HasNoEntry()
        {
            Assert.False(AliasMap.ContainsKey("ADMIN.NOT_A_REAL_PERMISSION"));
        }
    }
}
