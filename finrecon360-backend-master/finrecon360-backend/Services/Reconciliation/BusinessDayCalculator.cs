namespace finrecon360_backend.Services.Reconciliation
{
    /// <summary>
    /// T+N business-day settlement-window arithmetic for PosSettlementMatchWorker (Level7).
    /// Deliberately separate from the +/- calendar-day fuzzy-match window other workers use
    /// (ReconciliationSettings.DateToleranceDays) — settlement latency is directional ("the bank
    /// deposit lands N business days after the POS batch date") and must skip weekends and
    /// tenant-maintained banking holidays, not just count raw calendar days.
    /// </summary>
    public static class BusinessDayCalculator
    {
        public static bool IsBusinessDay(DateTime date, IReadOnlySet<DateOnly> holidays) =>
            date.DayOfWeek != DayOfWeek.Saturday &&
            date.DayOfWeek != DayOfWeek.Sunday &&
            !holidays.Contains(DateOnly.FromDateTime(date));

        public static DateTime AddBusinessDays(DateTime start, int days, IReadOnlySet<DateOnly> holidays)
        {
            var date = start;
            var remaining = Math.Abs(days);
            var direction = days < 0 ? -1 : 1;

            while (remaining > 0)
            {
                date = date.AddDays(direction);
                if (IsBusinessDay(date, holidays))
                {
                    remaining--;
                }
            }

            return date;
        }

        /// <summary>
        /// True when <paramref name="candidateDate"/> (typically a bank deposit date) falls
        /// within [batchDate, batchDate + N business days] — the expected settlement window for
        /// a POS batch dated <paramref name="batchDate"/>. A deposit dated before the batch date
        /// can never be its settlement.
        /// </summary>
        public static bool IsWithinSettlementWindow(
            DateTime batchDate,
            DateTime candidateDate,
            int windowBusinessDays,
            IReadOnlySet<DateOnly> holidays)
        {
            if (candidateDate.Date < batchDate.Date)
            {
                return false;
            }

            var windowEnd = AddBusinessDays(batchDate.Date, windowBusinessDays, holidays);
            return candidateDate.Date <= windowEnd.Date;
        }
    }
}
