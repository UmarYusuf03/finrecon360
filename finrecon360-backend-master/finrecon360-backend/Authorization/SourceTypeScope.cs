namespace finrecon360_backend.Authorization
{
    /// <summary>
    /// WHY: Source-type scoped permissions let a CASHIER role operate only on POS data
    /// while sharing the same controller endpoints as MANAGER / ADMIN roles.
    /// The rule is hierarchical:
    ///
    ///   ADMIN.IMPORTS.CREATE          → full unrestricted access (any source type)
    ///   ADMIN.IMPORTS.POS.CREATE      → access limited to SourceType == "POS"
    ///
    /// Similarly for EDIT, COMMIT, DELETE and the reconciliation actions.
    ///
    /// Usage (inside a controller action, after [RequirePermission] has already verified
    /// the coarse-grained module permission):
    ///
    ///   if (!SourceTypeScope.IsAllowed(userPermissions, "IMPORTS", "CREATE", batch.SourceType))
    ///       return Forbid();
    ///
    /// </summary>
    public static class SourceTypeScope
    {
        /// <summary>
        /// Returns true if the user's permission set allows the given action on the
        /// given sourceType.  Full (unscoped) permission always wins; scoped permission
        /// wins only when the source type matches.
        /// </summary>
        /// <param name="permissions">Flat list of permission codes from the tenant DB.</param>
        /// <param name="module">Module segment, e.g. "IMPORTS" or "RECONCILIATION".</param>
        /// <param name="action">Action segment, e.g. "CREATE", "EDIT", "COMMIT", "RESOLVE".</param>
        /// <param name="sourceType">Normalised source type, e.g. "POS", "ERP", "GATEWAY", "BANK".</param>
        public static bool IsAllowed(
            IEnumerable<string> permissions,
            string module,
            string action,
            string sourceType)
        {
            var full   = $"ADMIN.{module}.{action}";
            var scoped = $"ADMIN.{module}.{sourceType.ToUpperInvariant()}.{action}";

            return permissions.Any(p =>
                string.Equals(p, full,   StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, scoped, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the set of source types the user may access for a given module + action.
        /// Returns null when the user holds the unrestricted (non-scoped) permission,
        /// which means "all source types allowed" — callers should treat null as no filter.
        /// </summary>
        public static IReadOnlySet<string>? AllowedSourceTypes(
            IEnumerable<string> permissions,
            string module,
            string action)
        {
            var full = $"ADMIN.{module}.{action}";
            var permList = permissions.ToList();

            // Full/unrestricted grant → no source-type restriction
            if (permList.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
                return null;

            // Collect every scoped grant that matches ADMIN.<MODULE>.<SOURCETYPE>.<ACTION>
            var prefix  = $"ADMIN.{module}.".ToUpperInvariant();
            var suffix  = $".{action}".ToUpperInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in permList)
            {
                var upper = p.ToUpperInvariant();
                if (upper.StartsWith(prefix) && upper.EndsWith(suffix))
                {
                    // Extract the middle segment, e.g. "POS" from "ADMIN.IMPORTS.POS.CREATE"
                    var mid = upper[prefix.Length..^suffix.Length];
                    // Reject if mid contains another dot (would indicate a different structure)
                    if (mid.Length > 0 && !mid.Contains('.'))
                        allowed.Add(mid);
                }
            }

            return allowed.Count > 0 ? allowed : null;
        }
    }
}
