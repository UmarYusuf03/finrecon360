namespace ScenarioSeeder;

/// <summary>
/// Mirrors Services/Reconciliation/BusinessDayCalculator.cs in finrecon360-backend exactly
/// (weekday check + tenant holiday set, walk-one-day-at-a-time AddBusinessDays, inclusive window
/// boundary) so the dates this tool generates line up precisely with how the server's Level7
/// worker (PosSettlementMatchWorker) will evaluate IsWithinSettlementWindow. If the server's
/// algorithm ever changes, this needs to change with it.
/// </summary>
public static class BusinessDayMath
{
    public static bool IsBusinessDay(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek != DayOfWeek.Saturday &&
        date.DayOfWeek != DayOfWeek.Sunday &&
        !holidays.Contains(date);

    public static DateOnly AddBusinessDays(DateOnly start, int days, IReadOnlySet<DateOnly> holidays)
    {
        var current = start;
        var remaining = Math.Abs(days);
        var step = days >= 0 ? 1 : -1;
        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsBusinessDay(current, holidays))
            {
                remaining--;
            }
        }

        return current;
    }

    public static DateOnly NextBusinessDay(DateOnly from, IReadOnlySet<DateOnly> holidays)
    {
        var date = from;
        while (!IsBusinessDay(date, holidays))
        {
            date = date.AddDays(1);
        }

        return date;
    }
}
