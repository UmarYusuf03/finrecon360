using finrecon360_backend.Services.Reconciliation;
using Xunit;

namespace finrecon360_backend.Tests;

public class BusinessDayCalculatorTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    [Theory]
    [InlineData("2026-08-17")] // Monday
    [InlineData("2026-08-21")] // Friday
    public void IsBusinessDay_true_for_weekdays(string date)
    {
        Assert.True(BusinessDayCalculator.IsBusinessDay(DateTime.Parse(date), NoHolidays));
    }

    [Theory]
    [InlineData("2026-08-15")] // Saturday
    [InlineData("2026-08-16")] // Sunday
    public void IsBusinessDay_false_for_weekends(string date)
    {
        Assert.False(BusinessDayCalculator.IsBusinessDay(DateTime.Parse(date), NoHolidays));
    }

    [Fact]
    public void IsBusinessDay_false_for_a_seeded_holiday()
    {
        var holiday = new DateOnly(2026, 8, 17); // otherwise a Monday
        var holidays = new HashSet<DateOnly> { holiday };

        Assert.False(BusinessDayCalculator.IsBusinessDay(new DateTime(2026, 8, 17), holidays));
    }

    [Fact]
    public void AddBusinessDays_skips_the_weekend()
    {
        // Friday 2026-08-21 + 1 business day should land on Monday 2026-08-24, not Saturday.
        var result = BusinessDayCalculator.AddBusinessDays(new DateTime(2026, 8, 21), 1, NoHolidays);

        Assert.Equal(new DateTime(2026, 8, 24), result);
    }

    [Fact]
    public void AddBusinessDays_skips_a_holiday_that_falls_inside_the_window()
    {
        // Monday 2026-08-17 + 2 business days would normally be Wednesday 2026-08-19,
        // but Tuesday 2026-08-18 is a holiday, so it should land on Thursday 2026-08-20.
        var holidays = new HashSet<DateOnly> { new DateOnly(2026, 8, 18) };

        var result = BusinessDayCalculator.AddBusinessDays(new DateTime(2026, 8, 17), 2, holidays);

        Assert.Equal(new DateTime(2026, 8, 20), result);
    }

    [Fact]
    public void IsWithinSettlementWindow_false_when_candidate_predates_the_batch()
    {
        var batchDate = new DateTime(2026, 8, 17);
        var candidateDate = new DateTime(2026, 8, 16);

        Assert.False(BusinessDayCalculator.IsWithinSettlementWindow(batchDate, candidateDate, 3, NoHolidays));
    }

    [Fact]
    public void IsWithinSettlementWindow_true_on_the_window_boundary()
    {
        // Monday 2026-08-17 + 3 business days = Thursday 2026-08-20.
        var batchDate = new DateTime(2026, 8, 17);
        var candidateDate = new DateTime(2026, 8, 20);

        Assert.True(BusinessDayCalculator.IsWithinSettlementWindow(batchDate, candidateDate, 3, NoHolidays));
    }

    [Fact]
    public void IsWithinSettlementWindow_false_beyond_the_window()
    {
        var batchDate = new DateTime(2026, 8, 17);
        var candidateDate = new DateTime(2026, 8, 21); // one business day past the 3-day window

        Assert.False(BusinessDayCalculator.IsWithinSettlementWindow(batchDate, candidateDate, 3, NoHolidays));
    }
}
