namespace Syncr.Core
{
    /// <summary>
    /// Single source of truth for the SYNCR version string.
    /// Bump this before every release and match the GitHub tag (without leading 'v').
    /// Example: GitHub tag "v2.7.0" → Current = "2.7.0"
    /// </summary>
    public static class SyncrVersion
    {
        public const string Current = "2.6.5";
        public const string Display = $"SYNCR v{Current}";
    }
}
