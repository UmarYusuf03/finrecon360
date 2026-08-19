using System.Text;

namespace ScenarioSeeder;

/// <summary>
/// Orchestrates one full seeding pass against an already-bootstrapped tenant: bank accounts,
/// banking holidays, staff-entered Transactions (Level1/Level4), and the five CSV source files
/// (POS/ERP/GATEWAY/BANK/POS_SETTLEMENT). Transactions have no manual-CSV equivalent (there's no
/// import path for them) so they're always created via the real API. The CSV files, by default,
/// are also pushed through the real upload -> parse -> map -> validate -> commit import pipeline -
/// pass <paramref name="skipImport"/> = true to only write them to disk instead, e.g. so they can
/// be imported by hand through the UI for a live demo. Nothing here triggers matching directly -
/// that happens on ReconciliationCycleHostedService's own 5-minute cycle once the data is
/// COMMITTED (either by this tool or by a human finishing the manual import).
/// </summary>
public sealed class SeedRunner
{
    private readonly ApiClient _api;

    public SeedRunner(ApiClient api)
    {
        _api = api;
    }

    public async Task RunAsync(DateOnly anchorEnd, string outDir, bool skipImport = false)
    {
        Directory.CreateDirectory(outDir);

        Console.WriteLine("Creating bank accounts...");
        var primary = await _api.PostAsync<BankAccountResponseDto>("api/admin/bank-accounts", new
        {
            bankName = "Commercial Bank",
            accountName = "Primary Operating Account",
            accountNumber = "8001234567",
            currency = "LKR"
        });
        var secondary = await _api.PostAsync<BankAccountResponseDto>("api/admin/bank-accounts", new
        {
            bankName = "Commercial Bank",
            accountName = "Payroll Settlement Account",
            accountNumber = "8007654321",
            currency = "LKR"
        });
        Console.WriteLine($"  primary   = {primary!.BankAccountId}");
        Console.WriteLine($"  secondary = {secondary!.BankAccountId}");

        var ctx = new ScenarioContext(anchorEnd, primary.BankAccountId, secondary.BankAccountId);
        ScenarioCatalog.BuildAll(ctx);

        Console.WriteLine($"Registering {ctx.Holidays.Count} banking holiday(s)...");
        foreach (var holiday in ctx.Holidays.OrderBy(d => d))
        {
            await _api.PostAsync<object>("api/admin/reconciliation/banking-holidays", new
            {
                date = holiday.ToString("yyyy-MM-dd"),
                description = "Bank holiday (seeded to exercise the Level7 settlement window)"
            });
        }

        Console.WriteLine($"Creating and approving {ctx.Transactions.Count} transactions...");
        foreach (var t in ctx.Transactions)
        {
            var created = await _api.PostAsync<TransactionResponseDto>("api/admin/transactions", new
            {
                amount = t.Amount,
                transactionDate = t.Date.ToString("yyyy-MM-dd"),
                description = t.Description,
                referenceNumber = t.ReferenceNumber,
                bankAccountId = t.BankAccountId,
                transactionType = t.TransactionType,
                paymentMethod = t.PaymentMethod
            });
            await _api.PostAsync<object>($"api/admin/transactions/{created!.TransactionId}/approve",
                new { note = "Seeded for reconciliation scenario testing" });
        }

        Console.WriteLine(skipImport ? "Writing CSV source files (import skipped by request)..." : "Building and importing CSV source files...");
        await ImportSourceAsync("POS", ctx.Pos, null, outDir, skipImport: skipImport);
        await ImportSourceAsync("ERP", ctx.Erp, null, outDir, skipImport: skipImport);
        await ImportSourceAsync("GATEWAY", ctx.Gateway, null, outDir, skipImport: skipImport);
        await ImportSourceAsync("BANK", ctx.Bank, primary.BankAccountId, outDir, skipImport: skipImport);
        await ImportSourceAsync("BANK_SECONDARY", ctx.BankSecondary, secondary.BankAccountId, outDir, sourceTypeOverride: "BANK", skipImport: skipImport);
        await ImportSourceAsync("POS_SETTLEMENT", ctx.PosSettlement, null, outDir, skipImport: skipImport);

        Console.WriteLine();
        if (skipImport)
        {
            Console.WriteLine("CSV files are written to " + outDir + " - import each one by hand from here:");
            Console.WriteLine($"  POS.csv             -> sourceType POS,             no bank account");
            Console.WriteLine($"  ERP.csv              -> sourceType ERP,              no bank account");
            Console.WriteLine($"  GATEWAY.csv          -> sourceType GATEWAY,          no bank account");
            Console.WriteLine($"  BANK.csv             -> sourceType BANK,             bank account = {primary.AccountName} ({primary.BankAccountId})");
            Console.WriteLine($"  BANK_SECONDARY.csv   -> sourceType BANK,             bank account = {secondary.AccountName} ({secondary.BankAccountId})");
            Console.WriteLine($"  POS_SETTLEMENT.csv   -> sourceType POS_SETTLEMENT,   no bank account");
            Console.WriteLine("For each: Upload -> Parse -> Mapping (map every canonical field to the identically-named");
            Console.WriteLine("CSV column) -> Validate -> Commit. Matching only starts once a file is COMMITTED.");
        }
        else
        {
            Console.WriteLine("Seeding complete. Matching runs on the app's own 5-minute background cycle -");
            Console.WriteLine("give it a few cycles (or a restart, for the 10s startup run) before checking reports.");
        }
    }

    private async Task ImportSourceAsync(
        string label, List<ImportRow> rows, Guid? bankAccountId, string outDir,
        string? sourceTypeOverride = null, bool skipImport = false)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine($"  {label}: no rows, skipping.");
            return;
        }

        var sourceType = sourceTypeOverride ?? label;
        var csv = CsvWriterUtil.Build(rows);
        var filePath = Path.Combine(outDir, $"{label}.csv");
        await File.WriteAllTextAsync(filePath, csv, Encoding.UTF8);
        Console.WriteLine($"  {label}: wrote {rows.Count} row(s) to {filePath}");

        if (skipImport)
        {
            return;
        }

        var upload = await _api.PostMultipartAsync<ImportUploadResponseDto>(
            "api/imports", Path.GetFileName(filePath), Encoding.UTF8.GetBytes(csv), sourceType, bankAccountId);

        await _api.PostAsync<object>($"api/imports/{upload!.Id}/parse", null);

        var fieldMappings = ImportRow.Headers.ToDictionary(h => h, h => h, StringComparer.OrdinalIgnoreCase);
        await _api.PostAsync<object>($"api/imports/{upload.Id}/mapping", new
        {
            canonicalSchemaVersion = "v1",
            fieldMappings
        });

        var validation = await _api.PostAsync<ImportValidateResponseDto>($"api/imports/{upload.Id}/validate", null);
        if (validation!.InvalidRows > 0)
        {
            Console.WriteLine($"  {label}: {validation.InvalidRows} invalid row(s):");
            foreach (var err in validation.Errors)
            {
                Console.WriteLine($"    row {err.RowNumber}: {err.Message}");
            }

            throw new InvalidOperationException(
                $"{label} import failed validation - fix ScenarioCatalog.cs and re-run.");
        }

        var commit = await _api.PostAsync<ImportCommitResponseDto>($"api/imports/{upload.Id}/commit", null);
        Console.WriteLine($"  {label}: committed {commit!.NormalizedCount} normalized record(s) (batch {upload.Id}).");
    }
}
