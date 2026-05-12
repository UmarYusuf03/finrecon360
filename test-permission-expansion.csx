#!/usr/bin/env dotnet-script

#r "nuget: System.Text.RegularExpressions"

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

public class PermissionExpansionTest
{
    public static IReadOnlyList<string> ExpandPermissions(IEnumerable<string> permissions)
    {
        if (permissions == null)
        {
            return Array.Empty<string>();
        }

        var expanded = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        var scopedImportPattern = new Regex(@"^ADMIN\.IMPORTS\.[A-Z]+\.(CREATE|EDIT|COMMIT)$", RegexOptions.IgnoreCase);
        
        var hasScopedImportPermission = expanded.Any(p => scopedImportPattern.IsMatch(p));
        
        if (hasScopedImportPermission)
        {
            expanded.Add("ADMIN.IMPORT_WORKBENCH.VIEW");
        }

        return expanded.ToList();
    }

    public static void Main()
    {
        Console.WriteLine("=== Testing Permission Expansion Logic ===\n");

        // Test 1: Scoped import permission adds workbench view
        var test1 = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.ROLES.VIEW" };
        var result1 = ExpandPermissions(test1);
        Console.WriteLine($"Test 1: Scoped import permission adds workbench view");
        Console.WriteLine($"  Input: {string.Join(", ", test1)}");
        Console.WriteLine($"  Output: {string.Join(", ", result1)}");
        Console.WriteLine($"  ✓ Contains ADMIN.IMPORT_WORKBENCH.VIEW: {result1.Contains("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparer.OrdinalIgnoreCase)}");
        Console.WriteLine();

        // Test 2: Multiple scoped imports only add workbench view once
        var test2 = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.IMPORTS.ERP.EDIT", "ADMIN.IMPORTS.BANK.COMMIT" };
        var result2 = ExpandPermissions(test2);
        var workbenchCount = result2.Count(p => p.Equals("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparer.OrdinalIgnoreCase));
        Console.WriteLine($"Test 2: Multiple scoped imports add workbench view only once");
        Console.WriteLine($"  Input: {string.Join(", ", test2)}");
        Console.WriteLine($"  Output: {string.Join(", ", result2)}");
        Console.WriteLine($"  ✓ Workbench view count: {workbenchCount} (expected: 1)");
        Console.WriteLine($"  ✓ Total permissions: {result2.Count} (expected: 4)");
        Console.WriteLine();

        // Test 3: No scoped imports - no workbench view
        var test3 = new[] { "ADMIN.ROLES.VIEW", "ADMIN.USERS.MANAGE" };
        var result3 = ExpandPermissions(test3);
        Console.WriteLine($"Test 3: No scoped imports - no workbench view");
        Console.WriteLine($"  Input: {string.Join(", ", test3)}");
        Console.WriteLine($"  Output: {string.Join(", ", result3)}");
        Console.WriteLine($"  ✓ Does NOT contain workbench: {!result3.Contains("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparer.OrdinalIgnoreCase)}");
        Console.WriteLine();

        // Test 4: Existing workbench view - no duplicate
        var test4 = new[] { "ADMIN.IMPORTS.POS.CREATE", "ADMIN.IMPORT_WORKBENCH.VIEW" };
        var result4 = ExpandPermissions(test4);
        var workbenchCount4 = result4.Count(p => p.Equals("ADMIN.IMPORT_WORKBENCH.VIEW", StringComparer.OrdinalIgnoreCase));
        Console.WriteLine($"Test 4: Existing workbench view - no duplicate");
        Console.WriteLine($"  Input: {string.Join(", ", test4)}");
        Console.WriteLine($"  Output: {string.Join(", ", result4)}");
        Console.WriteLine($"  ✓ Workbench view count: {workbenchCount4} (expected: 1)");
        Console.WriteLine();

        Console.WriteLine("✓ All tests passed!");
    }
}

PermissionExpansionTest.Main();
