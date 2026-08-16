namespace finrecon360_backend.Options
{
    public class GoogleSsoOptions
    {
        /// <summary>
        /// The OAuth client ID issued by Google Cloud Console. Doubles as the expected audience
        /// when validating ID tokens, which is what ties a token to this application specifically.
        /// Not a secret — it ships to the browser — but sign-in is disabled when it is unset.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Optional Google Workspace domain restriction, e.g. "uom.lk". When set, only accounts in
        /// that domain may sign in. Leave empty to allow any Google account.
        /// </summary>
        public string HostedDomain { get; set; } = string.Empty;

        /// <summary>
        /// Whether a Google sign-in for an unrecognised email may create a new account.
        /// When false, SSO only signs in people who already have an account — useful if
        /// registration is meant to stay invite-only.
        /// </summary>
        public bool AllowAutoProvisioning { get; set; } = true;
    }
}
