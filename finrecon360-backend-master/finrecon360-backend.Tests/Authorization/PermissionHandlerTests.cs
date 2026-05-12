using System.Linq;
using finrecon360_backend.Authorization;
using Xunit;

namespace finrecon360_backend.Tests.Authorization
{
    public class PermissionHandlerTests
    {
        [Fact]
        public void ExpandPermissions_WithScopedImportPermission_AddsWorkbenchViewPermission()
        {
            // Arrange
            var permissions = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.ROLES.VIEW" };

            // Act
            var expanded = PermissionHandler.ExpandPermissions(permissions);

            // Assert
            Assert.Contains("ADMIN.IMPORT_WORKBENCH.VIEW", expanded);
            Assert.Contains("ADMIN.IMPORTS.POS.CREATE", expanded);
            Assert.Contains("ADMIN.ROLES.VIEW", expanded);
        }

        [Fact]
        public void ExpandPermissions_WithMultipleScopedImportPermissions_AddsWorkbenchViewPermissionOnce()
        {
            // Arrange
            var permissions = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.IMPORTS.ERP.EDIT", "ADMIN.IMPORTS.BANK.COMMIT" };

            // Act
            var expanded = PermissionHandler.ExpandPermissions(permissions);

            // Assert
            var workbenchCount = Enumerable.Count(expanded, p => p.Equals("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, workbenchCount);
            Assert.Equal(4, expanded.Count); // 3 original + 1 added
        }

        [Fact]
        public void ExpandPermissions_WithoutScopedImportPermission_DoesNotAddWorkbenchView()
        {
            // Arrange
            var permissions = new[] { "ADMIN.ROLES.VIEW", "ADMIN.USERS.MANAGE" };

            // Act
            var expanded = PermissionHandler.ExpandPermissions(permissions);

            // Assert
            Assert.DoesNotContain("ADMIN.IMPORT_WORKBENCH.VIEW", expanded);
            Assert.Equal(2, expanded.Count);
        }

        [Fact]
        public void ExpandPermissions_WithExistingWorkbenchViewPermission_DoesNotDuplicate()
        {
            // Arrange
            var permissions = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.IMPORT_WORKBENCH.VIEW" };

            // Act
            var expanded = PermissionHandler.ExpandPermissions(permissions);

            // Assert
            var workbenchCount = Enumerable.Count(expanded, p => p.Equals("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, workbenchCount);
            Assert.Equal(2, expanded.Count);
        }

        [Fact]
        public void ExpandPermissions_WithNullInput_ReturnsEmptyList()
        {
            // Act
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
            var expanded = PermissionHandler.ExpandPermissions(null!);
#pragma warning restore CS8625

            // Assert
            Assert.NotNull(expanded);
            Assert.Empty(expanded);
        }

        [Fact]
        public void ExpandPermissions_WithEmptyList_ReturnsEmptyList()
        {
            // Act
            var expanded = PermissionHandler.ExpandPermissions(Array.Empty<string>());

            // Assert
            Assert.NotNull(expanded);
            Assert.Empty(expanded);
        }
    }
}
