using Regex_Site_Blocker_App.Config;
using System.Text.RegularExpressions;

namespace Regex_Site_Blocker_App.Services.BrowserBlocking
{
    public sealed record BrowserObservation(nint Window, string Tab, string? DocumentUrl,
        string? AddressUrl, bool AddressFocused, bool Loading);
    public sealed record BlockDecision(BrowserObservation Observation, string Url, string Host, bool Timeout);

    public sealed class BrowserBlockPolicy
    {
        private RuleSnapshot rules = new(new AppConfig());
        private BrowserObservation? previous;
        private string? navigatingUrl;
        private bool editingDuringLoad;
        public RuleIssue[] Issues => Volatile.Read(ref rules).Issues;
        public void Apply(AppConfig config) => Interlocked.Exchange(ref rules, new RuleSnapshot(config));

        public BlockDecision? Evaluate(BrowserObservation current)
        {
            bool sameTab = previous?.Window == current.Window && previous.Tab == current.Tab;
            if (!sameTab || !current.Loading) { navigatingUrl = null; editingDuringLoad = false; }
            previous = current;
            // A loading signal is sufficient even on the first observation or after a provider retry.
            // Editing during an existing load still must not turn focus loss into a navigation signal.
            if (current.AddressFocused)
            {
                if (current.Loading) editingDuringLoad = true;
                navigatingUrl = null;
                return null;
            }
            if (current.Loading && !editingDuringLoad) navigatingUrl = current.AddressUrl;
            string? candidate = current.Loading && navigatingUrl != null ? navigatingUrl : current.DocumentUrl;
            if (!TryHost(candidate, out string host)) return null;
            try
            {
                bool blocked = Volatile.Read(ref rules).IsBlocked(host);
                return blocked ? new(current, candidate!, host, false) : null;
            }
            catch (RegexMatchTimeoutException) { return new(current, candidate!, host, true); }
        }

        public void Reset() { previous = null; navigatingUrl = null; editingDuringLoad = false; }
        public static bool CanRedirect(BlockDecision decision, BrowserObservation current, bool modifierDown) =>
            !modifierDown && !current.AddressFocused && current.Window == decision.Observation.Window &&
            current.Tab == decision.Observation.Tab && current.DocumentUrl == decision.Observation.DocumentUrl &&
            current.AddressUrl == decision.Observation.AddressUrl && current.Loading == decision.Observation.Loading;
        public static bool TryHost(string? url, out string host)
        {
            host = "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return false;
            host = uri.IdnHost.TrimEnd('.').ToLowerInvariant(); return true;
        }
    }
}

