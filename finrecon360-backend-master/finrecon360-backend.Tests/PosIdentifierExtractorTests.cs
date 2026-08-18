using finrecon360_backend.Services.Reconciliation;
using Xunit;

namespace finrecon360_backend.Tests;

public class PosIdentifierExtractorTests
{
    [Fact]
    public void Extract_parses_TID_and_batch_focused_narrative()
    {
        // "POS SETTLEMENT - TID88552 - BATCH 000452"
        var patterns = new Dictionary<string, string>
        {
            ["BatchNumber"] = @"BATCH\s+(\d+)",
            ["TerminalId"] = @"TID(\d+)",
        };

        var result = PosIdentifierExtractor.Extract("POS SETTLEMENT - TID88552 - BATCH 000452", patterns);

        Assert.Equal("452", result.BatchNumber); // leading zeros stripped
        Assert.Equal("88552", result.TerminalId);
        Assert.Null(result.MerchantId);
    }

    [Fact]
    public void Extract_parses_MID_focused_narrative()
    {
        // "MERCHANT EOD 987654321001 BATCH:000452"
        var patterns = new Dictionary<string, string>
        {
            ["BatchNumber"] = @"BATCH:(\d+)",
            ["MerchantId"] = @"EOD\s+(\d+)",
        };

        var result = PosIdentifierExtractor.Extract("MERCHANT EOD 987654321001 BATCH:000452", patterns);

        Assert.Equal("452", result.BatchNumber);
        Assert.Equal("987654321001", result.MerchantId);
        Assert.Null(result.TerminalId);
    }

    [Fact]
    public void Extract_parses_bank_vendor_focused_narrative()
    {
        // "COMBANK POS CR TID88552 BATCH000452"
        var patterns = new Dictionary<string, string>
        {
            ["BatchNumber"] = @"BATCH(\d+)",
            ["TerminalId"] = @"TID(\d+)",
        };

        var result = PosIdentifierExtractor.Extract("COMBANK POS CR TID88552 BATCH000452", patterns);

        Assert.Equal("452", result.BatchNumber);
        Assert.Equal("88552", result.TerminalId);
    }

    [Fact]
    public void Extract_returns_all_null_when_no_patterns_configured()
    {
        var result = PosIdentifierExtractor.Extract("POS SETTLEMENT - TID88552 - BATCH 000452", null);

        Assert.Null(result.BatchNumber);
        Assert.Null(result.TerminalId);
        Assert.Null(result.MerchantId);
    }

    [Fact]
    public void Extract_returns_null_for_field_whose_pattern_does_not_match()
    {
        var patterns = new Dictionary<string, string> { ["BatchNumber"] = @"BATCH(\d+)" };

        var result = PosIdentifierExtractor.Extract("No batch info here", patterns);

        Assert.Null(result.BatchNumber);
    }

    [Fact]
    public void Extract_does_not_throw_on_malformed_regex()
    {
        var patterns = new Dictionary<string, string> { ["BatchNumber"] = @"BATCH(\d+" }; // unbalanced paren

        var result = PosIdentifierExtractor.Extract("BATCH000452", patterns);

        Assert.Null(result.BatchNumber);
    }

    [Fact]
    public void Extract_preserves_all_zero_batch_number_instead_of_reducing_to_empty()
    {
        var patterns = new Dictionary<string, string> { ["BatchNumber"] = @"BATCH(\d+)" };

        var result = PosIdentifierExtractor.Extract("BATCH000", patterns);

        Assert.Equal("0", result.BatchNumber);
    }

    [Fact]
    public void Extract_is_case_insensitive_and_uppercases_output()
    {
        var patterns = new Dictionary<string, string> { ["TerminalId"] = @"tid(\w+)" };

        var result = PosIdentifierExtractor.Extract("TERMINAL tidabc123 END", patterns);

        Assert.Equal("ABC123", result.TerminalId);
    }
}
