using finrecon360_backend.BackgroundServices;
using Xunit;

namespace finrecon360_backend.Tests;

public class ReportScheduleHostedServiceTests
{
    [Fact]
    public void ComputeNextRunAt_returns_same_day_at_delivery_hour_when_still_ahead()
    {
        // Wednesday 2026-04-01, 02:00 UTC. Target: Wednesday, delivery hour 06:00 UTC — still ahead today.
        var from = new DateTime(2026, 4, 1, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal(DayOfWeek.Wednesday, from.DayOfWeek);

        var next = ReportScheduleHostedService.ComputeNextRunAt(from, DayOfWeek.Wednesday);

        Assert.Equal(new DateTime(2026, 4, 1, 6, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void ComputeNextRunAt_rolls_to_next_week_when_delivery_hour_already_passed_today()
    {
        // Wednesday 2026-04-01, 10:00 UTC — past the 06:00 UTC delivery hour for today.
        var from = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        var next = ReportScheduleHostedService.ComputeNextRunAt(from, DayOfWeek.Wednesday);

        Assert.Equal(new DateTime(2026, 4, 8, 6, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void ComputeNextRunAt_finds_the_next_occurrence_of_a_different_weekday()
    {
        // Wednesday 2026-04-01 -> next Monday is 2026-04-06.
        var from = new DateTime(2026, 4, 1, 2, 0, 0, DateTimeKind.Utc);

        var next = ReportScheduleHostedService.ComputeNextRunAt(from, DayOfWeek.Monday);

        Assert.Equal(new DateTime(2026, 4, 6, 6, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void ComputeNextRunAt_never_returns_a_time_at_or_before_from()
    {
        var random = new Random(42);
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 200; i++)
        {
            var from = baseDate.AddMinutes(random.Next(0, 60 * 24 * 30)).AddHours(random.Next(0, 24));
            var targetDay = (DayOfWeek)random.Next(0, 7);

            var next = ReportScheduleHostedService.ComputeNextRunAt(from, targetDay);

            Assert.True(next > from, $"Expected {next} to be strictly after {from} for target day {targetDay}");
            Assert.Equal(targetDay, next.DayOfWeek);
        }
    }
}
