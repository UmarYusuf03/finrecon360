using System.Globalization;

namespace ScenarioSeeder;

/// <summary>
/// Every matching rule and exception path the six live reconciliation workers can produce, two
/// instances each, spread across the ~3-month window ending at ScenarioContext.AnchorEnd.
/// Reference numbers are namespaced per level (SHIFT-/ORD-/INV-/CO-/STL- + numeric batch/terminal/
/// merchant ranges for Level7) deliberately, so the same GATEWAY/BANK/POS pool feeding multiple
/// workers can never accidentally cross-match between scenarios that were meant to stay isolated.
///
/// Level5 ("Collection Match") is skipped entirely — per ReconciliationCycleHostedService, no
/// running worker ever produces a Level5 group; it's retired, not just unused.
///
/// One Level1 exception path is NOT here: a Transaction with a blank Description ("no reference to
/// match on"). TransactionService.ValidateTransactionAsync requires a non-blank Description, so the
/// API itself refuses to create that row — the worker's defensive check for it is unreachable from
/// here. Only reachable via direct DB manipulation, which this tool deliberately avoids.
/// </summary>
public static class ScenarioCatalog
{
    public static void BuildAll(ScenarioContext c)
    {
        L1_Match(c);
        L1_NoMatch(c);
        L1_Variance(c);
        L1_Ambiguous(c);

        L2_Match(c);
        L2_NoMatch(c);
        L2_Variance(c);
        L2_Ambiguous(c);

        L3_Match(c);
        L3_NoMatch(c);
        L3_Variance(c);
        L3_Ambiguous(c);

        L4_Tier1Exact(c);
        L4_Tier2FeeExplained(c);
        L4_Tier3VarianceException(c);
        L4_AmbiguousGateway(c);
        L4_MissingSettlementKey(c);
        L4_NoBankForKey(c);
        L4_WrongBankAccount(c);
        L4_AmbiguousBank(c);

        L6_Match(c);
        L6_NoBankRef(c);
        L6_WrongBankAccount(c);
        L6_Variance(c);
        L6_Ambiguous(c);

        L7_Tier1ExactBatch(c);
        L7_Tier1SplitNetwork(c);
        L7_Tier2ManyToOne(c);
        L7_Tier3Merchant(c);
        L7_VarianceException(c);
        L7_NoCounterpart(c);
        L7_WindowBoundaryWithin(c);
        L7_WindowBoundaryOutside(c);
        L7_HolidayShift(c);
    }

    // ── formatting helpers ──────────────────────────────────────────────────────────

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Amt(decimal m) => m.ToString("0.00", CultureInfo.InvariantCulture);

    // ── row builders ────────────────────────────────────────────────────────────────

    private static ImportRow PosRow(DateOnly date, string reference, decimal net, string description) => new()
    {
        TransactionDate = D(date),
        ReferenceNumber = reference,
        NetAmount = Amt(net),
        Description = description,
        Currency = "LKR"
    };

    private static ImportRow ErpRow(DateOnly date, string reference, decimal net, string description) => new()
    {
        TransactionDate = D(date),
        ReferenceNumber = reference,
        NetAmount = Amt(net),
        Description = description,
        Currency = "LKR"
    };

    private static ImportRow GatewayRow(
        DateOnly date, decimal net, string description,
        string? settlementId = null, string? accountCode = null, string? referenceNumber = null,
        decimal? gross = null, decimal? fee = null) => new()
    {
        TransactionDate = D(date),
        NetAmount = Amt(net),
        Description = description,
        Currency = "LKR",
        SettlementId = settlementId,
        AccountCode = accountCode,
        ReferenceNumber = referenceNumber,
        GrossAmount = gross.HasValue ? Amt(gross.Value) : null,
        ProcessingFee = fee.HasValue ? Amt(fee.Value) : null
    };

    private static ImportRow BankRow(
        DateOnly date, decimal net, string description,
        string? settlementId = null, string? accountCode = null, string? referenceNumber = null,
        string? batchNumber = null, string? terminalId = null, string? merchantId = null) => new()
    {
        TransactionDate = D(date),
        NetAmount = Amt(net),
        Description = description,
        Currency = "LKR",
        SettlementId = settlementId,
        AccountCode = accountCode,
        ReferenceNumber = referenceNumber,
        BatchNumber = batchNumber,
        TerminalId = terminalId,
        MerchantId = merchantId
    };

    private static ImportRow PosSettlementRow(
        DateOnly date, decimal gross, decimal fee, string description,
        string? batchNumber = null, string? terminalId = null, string? merchantId = null) => new()
    {
        TransactionDate = D(date),
        GrossAmount = Amt(gross),
        ProcessingFee = Amt(fee),
        NetAmount = Amt(gross - fee),
        Description = description,
        Currency = "LKR",
        BatchNumber = batchNumber,
        TerminalId = terminalId,
        MerchantId = merchantId
    };

    // ── Level1: Operational Match (staff CashIn Transaction <-> POS EOD) ───────────────
    // Key: {TransactionDate:yyyy-MM-dd}|{reference}. On the staff side "reference" is actually
    // Transaction.Description (there is no dedicated ref field on Transaction) — so Description
    // on the transaction must equal ReferenceNumber on the POS row, verbatim.

    private static void L1_Match(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 4200m + i * 850m + 0.50m;
            var label = $"SHIFT-L1MATCH-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, label, null, null, "CashIn", "Cash"));
            c.Pos.Add(PosRow(date, label, amount, "Shift cash handover"));
        }
    }

    private static void L1_NoMatch(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 3100m + i * 400m;
            var label = $"SHIFT-L1NOMATCH-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, label, null, null, "CashIn", "Cash"));
            // deliberately no POS row for this key
        }
    }

    private static void L1_Variance(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var staffAmount = 5000m + i * 300m;
            var posAmount = staffAmount - 480m; // well beyond the 0.01 tolerance
            var label = $"SHIFT-L1VAR-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, staffAmount, date, label, null, null, "CashIn", "Cash"));
            c.Pos.Add(PosRow(date, label, posAmount, "Shift cash handover (till short)"));
        }
    }

    private static void L1_Ambiguous(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var staffAmount = 6000m + i * 250m;
            var label = $"SHIFT-L1AMB-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, staffAmount, date, label, null, null, "CashIn", "Cash"));
            c.Pos.Add(PosRow(date, label, staffAmount, "Shift cash handover - register A"));
            c.Pos.Add(PosRow(date, label, staffAmount, "Shift cash handover - register B (duplicate entry)"));
        }
    }

    // ── Level2: Sync Audit (POS EOD <-> ERP Sales Ledger) ──────────────────────────────
    // Key: ReferenceNumber (Order ID), case-insensitive.

    private static void L2_Match(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 7400m + i * 610m;
            var refNo = $"ORD-L2MATCH-{i + 1:00}";
            c.Pos.Add(PosRow(date, refNo, amount, "POS EOD sale"));
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry"));
        }
    }

    private static void L2_NoMatch(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 6100m + i * 400m;
            var refNo = $"ORD-L2NOMATCH-{i + 1:00}";
            c.Pos.Add(PosRow(date, refNo, amount, "POS EOD sale (no ERP entry yet)"));
        }
    }

    private static void L2_Variance(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var posAmount = 8300m + i * 500m;
            var erpAmount = posAmount - 220m; // discount / rounding difference
            var refNo = $"ORD-L2VAR-{i + 1:00}";
            c.Pos.Add(PosRow(date, refNo, posAmount, "POS EOD sale"));
            c.Erp.Add(ErpRow(date, refNo, erpAmount, "ERP sales ledger entry (discount applied)"));
        }
    }

    private static void L2_Ambiguous(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 9100m + i * 300m;
            var refNo = $"ORD-L2AMB-{i + 1:00}";
            c.Pos.Add(PosRow(date, refNo, amount, "POS EOD sale"));
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry - draft 1"));
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry - draft 2 (duplicate posting)"));
        }
    }

    // ── Level3: Sales Match (ERP Sales Ledger <-> Payment Gateway) ─────────────────────
    // Key: ReferenceNumber (Order ID). GATEWAY rows here carry ReferenceNumber only (no
    // SettlementId/AccountCode) so they can never be picked up by Level4's SettlementKeyResolver
    // or Level6's SettlementId grouping.

    private static void L3_Match(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 5600m + i * 470m;
            var refNo = $"INV-L3MATCH-{i + 1:00}";
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement credit", referenceNumber: refNo));
        }
    }

    private static void L3_NoMatch(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 4700m + i * 350m;
            var refNo = $"INV-L3NOMATCH-{i + 1:00}";
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry (revenue not yet collected)"));
        }
    }

    private static void L3_Variance(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var erpAmount = 6800m + i * 400m;
            var gatewayAmount = erpAmount - 340m; // processing fee deduction / partial refund
            var refNo = $"INV-L3VAR-{i + 1:00}";
            c.Erp.Add(ErpRow(date, refNo, erpAmount, "ERP sales ledger entry"));
            c.Gateway.Add(GatewayRow(date, gatewayAmount, "Gateway settlement credit (fee deducted)", referenceNumber: refNo));
        }
    }

    private static void L3_Ambiguous(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 7200m + i * 260m;
            var refNo = $"INV-L3AMB-{i + 1:00}";
            c.Erp.Add(ErpRow(date, refNo, amount, "ERP sales ledger entry"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement credit - split 1", referenceNumber: refNo));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement credit - split 2", referenceNumber: refNo));
        }
    }

    // ── Level4: Expense Match (CashOut/Card Transaction <-> GATEWAY <-> BANK) ──────────
    // GATEWAY rows here carry AccountCode+ReferenceNumber (the SettlementKeyResolver composite
    // fallback), deliberately never SettlementId, so they can never be swept up by Level6's
    // SettlementId grouping. The GATEWAY-candidate search itself is by (date, amount) against the
    // transaction, not by key — so every transaction here needs its own unique date, which
    // ScenarioContext.ClaimGenericDate() guarantees globally across the whole catalog.

    private static void L4_Tier1Exact(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 12000m + i * 900m;
            var label = $"CO-L4T1-{i + 1:00}";
            var acct = $"ACC{100 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - card cashout", accountCode: acct, referenceNumber: label));
            c.Bank.Add(BankRow(date, amount, "Bank credit - card cashout settlement", accountCode: acct, referenceNumber: label));
        }
    }

    private static void L4_Tier2FeeExplained(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 13500m + i * 800m;
            var fee = 300m + i * 25m;
            var label = $"CO-L4T2-{i + 1:00}";
            var acct = $"ACC{200 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - card cashout", accountCode: acct, referenceNumber: label, gross: amount + fee, fee: fee));
            c.Bank.Add(BankRow(date, amount - fee, "Bank credit - card cashout net of processing fee", accountCode: acct, referenceNumber: label));
        }
    }

    private static void L4_Tier3VarianceException(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 9800m + i * 550m;
            var label = $"CO-L4T3-{i + 1:00}";
            var acct = $"ACC{300 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - card cashout", accountCode: acct, referenceNumber: label));
            c.Bank.Add(BankRow(date, amount - 730m, "Bank credit - unexplained shortfall", accountCode: acct, referenceNumber: label));
        }
    }

    private static void L4_AmbiguousGateway(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 11000m + i * 400m;
            var label = $"CO-L4AMBGW-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            // Two GATEWAY rows on the same date, both within tolerance of the transaction amount -
            // the ambiguity check fires before settlement-key resolution, so no BANK row is needed.
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout candidate A", accountCode: $"ACC{400 + i}A", referenceNumber: label + "-A"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout candidate B", accountCode: $"ACC{400 + i}B", referenceNumber: label + "-B"));
        }
    }

    private static void L4_MissingSettlementKey(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 8600m + i * 300m;
            var label = $"CO-L4NOKEY-{i + 1:00}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            // AccountCode, ReferenceNumber and SettlementId are all left blank on purpose.
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - missing settlement identifiers"));
        }
    }

    private static void L4_NoBankForKey(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 10200m + i * 350m;
            var label = $"CO-L4NOBANK-{i + 1:00}";
            var acct = $"ACC{500 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - awaiting bank settlement", accountCode: acct, referenceNumber: label));
            // deliberately no BANK row anywhere carries this settlement key
        }
    }

    private static void L4_WrongBankAccount(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 9400m + i * 300m;
            var label = $"CO-L4WRONGACCT-{i + 1:00}";
            var acct = $"ACC{600 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - card cashout", accountCode: acct, referenceNumber: label));
            // matching BANK row exists, but only in the secondary account's batch
            c.BankSecondary.Add(BankRow(date, amount, "Bank credit - lodged to wrong account", accountCode: acct, referenceNumber: label));
        }
    }

    private static void L4_AmbiguousBank(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 8800m + i * 400m;
            var label = $"CO-L4AMBBANK-{i + 1:00}";
            var acct = $"ACC{700 + i}";
            c.Transactions.Add(new TransactionSeed(label, amount, date, "Card cashout - " + label, null, c.PrimaryBankAccountId, "CashOut", "Card"));
            c.Gateway.Add(GatewayRow(date, amount, "Gateway payout - card cashout", accountCode: acct, referenceNumber: label));
            c.Bank.Add(BankRow(date, amount, "Bank credit - candidate A", accountCode: acct, referenceNumber: label));
            c.Bank.Add(BankRow(date, amount, "Bank credit - candidate B (duplicate memo)", accountCode: acct, referenceNumber: label));
        }
    }

    // ── Level6: Settlement Match (GATEWAY grouped by SettlementId <-> BANK) ────────────
    // GATEWAY rows here carry SettlementId only (no AccountCode/ReferenceNumber), so Level3 (which
    // requires ReferenceNumber) and Level4 (whose resolver would otherwise prefer SettlementId)
    // never pick them up.

    private static void L6_Match(ScenarioContext c)
    {
        // instance 1: simple one gateway record : one bank deposit
        {
            var date = c.ClaimGenericDate();
            const decimal amount = 42000m;
            const string settleId = "STL-L6MATCH-01";
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement batch", settlementId: settleId));
            c.Bank.Add(BankRow(date, amount, "Bank settlement deposit", referenceNumber: settleId));
        }

        // instance 2: many gateway records consolidated into one bank deposit
        {
            var date = c.ClaimGenericDate();
            const decimal part1 = 18000m;
            const decimal part2 = 26500m;
            const string settleId = "STL-L6MATCH-02";
            c.Gateway.Add(GatewayRow(date, part1, "Gateway settlement batch - part 1", settlementId: settleId));
            c.Gateway.Add(GatewayRow(date, part2, "Gateway settlement batch - part 2", settlementId: settleId));
            c.Bank.Add(BankRow(date, part1 + part2, "Bank settlement deposit (consolidated)", referenceNumber: settleId));
        }
    }

    private static void L6_NoBankRef(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 31000m + i * 1500m;
            var settleId = $"STL-L6NOBANK-{i + 1:00}";
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement batch (awaiting bank deposit)", settlementId: settleId));
        }
    }

    private static void L6_WrongBankAccount(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 27500m + i * 1200m;
            var settleId = $"STL-L6WRONGACCT-{i + 1:00}";
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement batch", settlementId: settleId));
            c.BankSecondary.Add(BankRow(date, amount, "Bank settlement deposit (wrong account)", referenceNumber: settleId));
        }
    }

    private static void L6_Variance(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var gwAmount = 36000m + i * 1400m;
            var bankAmount = gwAmount - 900m; // undisclosed settlement fee
            var settleId = $"STL-L6VAR-{i + 1:00}";
            c.Gateway.Add(GatewayRow(date, gwAmount, "Gateway settlement batch", settlementId: settleId));
            c.Bank.Add(BankRow(date, bankAmount, "Bank settlement deposit (fee withheld)", referenceNumber: settleId));
        }
    }

    private static void L6_Ambiguous(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var date = c.ClaimGenericDate();
            var amount = 29500m + i * 1600m;
            var settleId = $"STL-L6AMB-{i + 1:00}";
            c.Gateway.Add(GatewayRow(date, amount, "Gateway settlement batch", settlementId: settleId));
            c.Bank.Add(BankRow(date, amount, "Bank settlement deposit - candidate A", referenceNumber: settleId));
            c.Bank.Add(BankRow(date, amount, "Bank settlement deposit - candidate B", referenceNumber: settleId));
        }
    }

    // ── Level7: POS-terminal batch settlement (POS_SETTLEMENT <-> BANK) ────────────────
    // BANK rows here carry only BatchNumber/TerminalId/MerchantId (never SettlementId/
    // AccountCode/ReferenceNumber), so they never surface in Level4/Level6 matching.
    // Dates come from ScenarioContext.ClaimL7BaseDate(), which starts far enough back that the
    // forward business-day arithmetic below never lands after AnchorEnd.

    private static void L7_Tier1ExactBatch(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            var gross = 25000m + i * 1800m;
            var fee = 750m + i * 40m;
            var batch = (7001 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, gross - fee, "Bank credit - POS settlement", batchNumber: batch));
        }
    }

    private static void L7_Tier1SplitNetwork(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            var gross = 40000m + i * 2200m;
            var fee = 1200m + i * 60m;
            var net = gross - fee;
            var visaShare = Math.Round(net * 0.6m, 2);
            var amexShare = net - visaShare;
            var batch = (7101 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (split network)", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, visaShare, "Bank credit - POS settlement (Visa)", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, amexShare, "Bank credit - POS settlement (Amex)", batchNumber: batch));
        }
    }

    private static void L7_Tier2ManyToOne(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            var gross1 = 15000m + i * 900m;
            const decimal fee1 = 450m;
            var gross2 = 9000m + i * 500m;
            const decimal fee2 = 270m;
            var terminal = $"TID{8001 + i}";
            c.PosSettlement.Add(PosSettlementRow(posDate, gross1, fee1, "POS terminal batch close - shift 1", terminalId: terminal));
            c.PosSettlement.Add(PosSettlementRow(posDate, gross2, fee2, "POS terminal batch close - shift 2", terminalId: terminal));
            var bankNet = (gross1 - fee1) + (gross2 - fee2);
            c.Bank.Add(BankRow(bankDate, bankNet, "Bank credit - POS settlement", terminalId: terminal));
        }
    }

    private static void L7_Tier3Merchant(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            var gross = 18000m + i * 1100m;
            var fee = 540m + i * 30m;
            var merchant = $"MID{9001 + i}";
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (merchant EOD only)", merchantId: merchant));
            c.Bank.Add(BankRow(bankDate, gross - fee, "Bank credit - POS settlement", merchantId: merchant));
        }
    }

    private static void L7_VarianceException(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            var gross = 22000m + i * 1300m;
            const decimal fee = 660m;
            var net = gross - fee;
            var batch = (7201 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, net - 1500m, "Bank credit - POS settlement (short)", batchNumber: batch));
        }
    }

    private static void L7_NoCounterpart(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var gross = 12000m + i * 700m;
            const decimal fee = 360m;
            var batch = (7301 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (settlement pending)", batchNumber: batch));
            // deliberately no BANK row at all
        }
    }

    private static void L7_WindowBoundaryWithin(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var bankDate = BusinessDayMath.AddBusinessDays(posDate, 3, c.Holidays); // exactly the (inclusive) boundary
            var gross = 16000m + i * 900m;
            const decimal fee = 480m;
            var batch = (7401 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (slow settlement)", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, gross - fee, "Bank credit - POS settlement (arrived on window boundary)", batchNumber: batch));
        }
    }

    private static void L7_WindowBoundaryOutside(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();
            var boundary = BusinessDayMath.AddBusinessDays(posDate, 3, c.Holidays);
            var bankDate = BusinessDayMath.AddBusinessDays(boundary, 1, c.Holidays); // one business day past the window
            var gross = 17000m + i * 950m;
            const decimal fee = 510m;
            var batch = (7501 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (very slow settlement)", batchNumber: batch));
            c.Bank.Add(BankRow(bankDate, gross - fee, "Bank credit - POS settlement (arrived after window)", batchNumber: batch));
        }
    }

    private static void L7_HolidayShift(ScenarioContext c)
    {
        for (var i = 0; i < 2; i++)
        {
            var posDate = c.ClaimL7BaseDate();

            // Insert a holiday one business day after posDate, inside the naive 3-business-day
            // window. That pushes the effective window boundary out by exactly one day. The BANK
            // row lands on the *shifted* boundary - a date that would fall outside the window
            // without this holiday - proving BankingHolidays actually participates in the math,
            // not just weekends.
            var holiday = BusinessDayMath.AddBusinessDays(posDate, 1, c.Holidays);
            c.Holidays.Add(holiday);

            var shiftedBoundary = BusinessDayMath.AddBusinessDays(posDate, 3, c.Holidays);
            var gross = 19000m + i * 1000m;
            const decimal fee = 570m;
            var batch = (7601 + i).ToString();
            c.PosSettlement.Add(PosSettlementRow(posDate, gross, fee, "POS terminal batch close (holiday-adjacent settlement)", batchNumber: batch));
            c.Bank.Add(BankRow(shiftedBoundary, gross - fee, "Bank credit - POS settlement (after banking holiday)", batchNumber: batch));
        }
    }
}
