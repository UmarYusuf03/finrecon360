using System.Globalization;
using System.Text.Json;

namespace ScenarioSeeder;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);

        string Require(string key) =>
            opts.TryGetValue(key, out var v) ? v : throw new ArgumentException($"Missing required --{key}");

        string Optional(string key, string fallback) => opts.TryGetValue(key, out var v) ? v : fallback;

        if (opts.ContainsKey("help"))
        {
            PrintUsage();
            return 0;
        }

        if (opts.ContainsKey("verify"))
        {
            return RunVerify(opts);
        }

        try
        {
            var baseUrl = Optional("base-url", "http://localhost:5279");
            var systemAdminEmail = Require("system-admin-email");
            var systemAdminPassword = Require("system-admin-password");
            var businessName = Require("business-name");
            var tenantAdminEmail = Require("admin-email");
            var tenantPassword = Require("tenant-password");
            var businessType = Optional("business-type", "VEHICLE_RENTAL");
            var outDir = Optional("out-dir", Path.Combine("out", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            var anchorEndStr = Optional("anchor-end", DateTime.UtcNow.Date.AddDays(-2).ToString("yyyy-MM-dd"));
            var anchorEnd = DateOnly.ParseExact(anchorEndStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Directory.CreateDirectory(outDir);

            var api = new ApiClient(baseUrl);
            var bootstrapper = new TenantBootstrapper(api);
            var session = await bootstrapper.BootstrapAsync(
                systemAdminEmail, systemAdminPassword, businessName, tenantAdminEmail, tenantPassword, businessType);

            var sessionPath = Path.Combine(outDir, "tenant-session.json");
            await File.WriteAllTextAsync(sessionPath,
                JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine();
            Console.WriteLine($"Tenant ready: {session.TenantId}");
            Console.WriteLine($"  login: {session.AdminEmail} / {session.AdminPassword}");
            Console.WriteLine($"  saved to: {sessionPath}");
            Console.WriteLine();

            var skipImport = opts.ContainsKey("skip-import");

            var runner = new SeedRunner(api);
            await runner.RunAsync(anchorEnd, outDir, skipImport);

            Console.WriteLine();
            Console.WriteLine("Log in to the app with the tenant admin credentials above to watch matching happen.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Builds the scenario catalog against two dummy bank-account GUIDs and reports on it - no
    /// network calls, no live server needed. Checks the two things that would silently produce
    /// bad data if the cursor math in ScenarioContext were ever wrong: every generic-scenario date
    /// is unique (so Level4's amount+date GATEWAY lookup can't cross-match two unrelated
    /// scenarios), and every Level7 date this catalog computes still lands on or before
    /// --anchor-end (so nothing ends up dated in the future).
    /// </summary>
    private static int RunVerify(Dictionary<string, string> opts)
    {
        var anchorEndStr = opts.TryGetValue("anchor-end", out var a)
            ? a
            : DateTime.UtcNow.Date.AddDays(-2).ToString("yyyy-MM-dd");
        var anchorEnd = DateOnly.ParseExact(anchorEndStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var ctx = new ScenarioContext(anchorEnd, Guid.NewGuid(), Guid.NewGuid());
        ScenarioCatalog.BuildAll(ctx);

        Console.WriteLine($"anchorEnd = {anchorEnd:yyyy-MM-dd}");
        Console.WriteLine($"transactions       = {ctx.Transactions.Count}");
        Console.WriteLine($"pos rows           = {ctx.Pos.Count}");
        Console.WriteLine($"erp rows           = {ctx.Erp.Count}");
        Console.WriteLine($"gateway rows       = {ctx.Gateway.Count}");
        Console.WriteLine($"bank rows          = {ctx.Bank.Count}");
        Console.WriteLine($"bank_secondary rows= {ctx.BankSecondary.Count}");
        Console.WriteLine($"pos_settlement rows= {ctx.PosSettlement.Count}");
        Console.WriteLine($"holidays seeded    = {ctx.Holidays.Count} ({string.Join(", ", ctx.Holidays.OrderBy(d => d))})");

        var problems = new List<string>();

        var txnDates = ctx.Transactions.Select(t => t.Date).ToList();
        var dupTxnDates = txnDates.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupTxnDates.Count > 0)
        {
            problems.Add($"duplicate transaction dates (breaks Level4's amount+date GATEWAY lookup): {string.Join(", ", dupTxnDates)}");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var allBankDates = ctx.Bank.Concat(ctx.BankSecondary)
            .Select(r => DateOnly.ParseExact(r.TransactionDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        var futureDates = allBankDates.Where(d => d > today).ToList();
        if (futureDates.Count > 0)
        {
            problems.Add($"BANK rows dated after today: {string.Join(", ", futureDates.Distinct())}");
        }

        var maxTxnDate = txnDates.Count > 0 ? txnDates.Max() : (DateOnly?)null;
        if (maxTxnDate.HasValue && maxTxnDate.Value > today)
        {
            problems.Add($"a transaction is dated in the future: {maxTxnDate.Value} (TransactionDate must be <= today)");
        }

        Console.WriteLine();
        if (problems.Count == 0)
        {
            Console.WriteLine("OK - no date-safety problems found.");
            return 0;
        }

        Console.WriteLine("PROBLEMS FOUND:");
        foreach (var p in problems)
        {
            Console.WriteLine("  - " + p);
        }

        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            ScenarioSeeder - seeds a brand-new finrecon360 tenant with reconciliation test data
            covering every matching rule and exception path (see ScenarioCatalog.cs), across a
            ~3-month window, then commits it through the real import pipeline.

            Each run creates a NEW tenant, so running this again later (e.g. for a demo) never
            touches data from an earlier run - just pass a different --admin-email/--business-name.

            Required:
              --system-admin-email <email>      Control-plane system admin login (from the
                                                 backend's .env: SYSTEM_ADMIN_EMAIL)
              --system-admin-password <pw>      SYSTEM_ADMIN_PASSWORD from the same .env
              --business-name <name>            Name for the new tenant, e.g. "QA Test Co"
              --admin-email <email>             Email for the new tenant's admin user (must not
                                                 already exist)
              --tenant-password <password>      Password to set for that tenant admin

            Optional:
              --base-url <url>                  Default: http://localhost:5279
              --business-type <type>             VEHICLE_RENTAL | ACCOMMODATION (default VEHICLE_RENTAL)
              --out-dir <path>                  Where CSVs + tenant-session.json are written
                                                 (default: ./out/<timestamp>)
              --anchor-end <yyyy-MM-dd>          Last date in the 3-month scenario window
                                                 (default: 2 days before today)
              --skip-import                      Bootstrap the tenant, create bank accounts,
                                                 banking holidays and transactions as usual, but
                                                 only WRITE the 5 CSV files to --out-dir instead
                                                 of importing them - use this to import them by
                                                 hand through the UI for a live demo.

            Example (fully automatic - test run):
              dotnet run -- --system-admin-email admin@yourdomain.com --system-admin-password fantasia@123 \
                --business-name "QA Test Co" --admin-email qa-test-01@example.com \
                --tenant-password "TestPass!2026" --out-dir ./out/test-run

            Example (leave CSVs for a manual demo import):
              dotnet run -- --system-admin-email admin@yourdomain.com --system-admin-password fantasia@123 \
                --business-name "Demo Co" --admin-email demo-01@example.com \
                --tenant-password "DemoPass!2026" --out-dir ./out/demo-run --skip-import
            """);
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result[key] = hasValue ? args[++i] : "true";
        }

        return result;
    }
}
