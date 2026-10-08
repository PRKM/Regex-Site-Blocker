using System.Text.RegularExpressions;
using Regex_Site_Blocker_App.Config;

namespace Regex_Site_Blocker_App.Services.BrowserBlocking
{
    internal sealed class RuleSnapshot
    {
        private readonly Regex[] patterns;
        public RuleIssue[] Issues { get; }

        public RuleSnapshot(AppConfig config)
        {
            var valid = new List<Regex>();
            var invalid = new List<RuleIssue>();
            foreach (var rule in config.Blacklist)
            {
                if (!rule.Enable) continue;
                try
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(rule.Pattern);
                    valid.Add(new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)));
                }
                catch (ArgumentException ex) { invalid.Add(new(rule.Pattern, ex.Message)); }
            }
            patterns = valid.ToArray();
            Issues = invalid.ToArray();
        }

        public bool IsBlocked(string domain)
        {
            foreach (var pattern in patterns)
                if (pattern.IsMatch(domain)) return true;
            return false;
        }
    }
}
