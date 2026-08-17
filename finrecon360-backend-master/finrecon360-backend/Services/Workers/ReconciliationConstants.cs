namespace finrecon360_backend.Services.Workers
{
    /// <summary>
    /// ImportedNormalizedRecord.MatchStatus values. Centralized so a typo in one worker can't
    /// silently create a status string another worker's query never looks for.
    /// </summary>
    public static class MatchStatuses
    {
        public const string Pending = "PENDING";
        public const string Matched = "MATCHED";
        public const string Waiting = "WAITING";
        public const string Exception = "EXCEPTION";
        public const string Level2Matched = "LEVEL2_MATCHED";
        public const string Level3Matched = "LEVEL3_MATCHED";
        public const string Level6Matched = "LEVEL6_MATCHED";
        public const string SalesVerified = "SALES_VERIFIED";
    }

    /// <summary>
    /// ReconciliationMatchGroup/ReconciliationEvent.MatchLevel values (Level1-6).
    /// </summary>
    public static class MatchLevels
    {
        public const string Level1 = "Level1";
        public const string Level2 = "Level2";
        public const string Level3 = "Level3";
        public const string Level4 = "Level4";
        public const string Level5 = "Level5";
        public const string Level6 = "Level6";
    }

    /// <summary>
    /// ReconciliationEvent.EventType values.
    /// </summary>
    public static class ReconciliationEventTypes
    {
        public const string MatchFound = "MatchFound";
        public const string MatchNotFound = "MatchNotFound";
        public const string Variance = "Variance";
        public const string RequiresReview = "RequiresReview";
    }
}
