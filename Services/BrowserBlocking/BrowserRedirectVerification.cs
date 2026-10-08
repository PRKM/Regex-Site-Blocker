namespace Regex_Site_Blocker_App.Services.BrowserBlocking
{
    public enum RedirectStatus { Pending, Confirmed, TimedOut, ContextChanged }

    public sealed class BrowserRedirectVerification(BrowserObservation target, string destination)
    {
        public RedirectStatus Observe(BrowserObservation current, TimeSpan elapsed)
        {
            if (current.Window != target.Window || current.Tab != target.Tab) return RedirectStatus.ContextChanged;
            // Address bar text is editable and does not prove a committed navigation.
            if (IsDestination(current.DocumentUrl, destination)) return RedirectStatus.Confirmed;
            // The previous document can remain visible while the local navigation is pending.
            // A timer alone must never trigger Stop/Esc against our own destination.
            if (IsDestination(current.AddressUrl, destination)) return RedirectStatus.Pending;
            if (current.AddressFocused) return RedirectStatus.ContextChanged;
            return elapsed >= TimeSpan.FromMilliseconds(500) ? RedirectStatus.TimedOut : RedirectStatus.Pending;
        }

        public static bool IsDestination(string? url, string destination) =>
            Uri.TryCreate(destination, UriKind.Absolute, out var expected) && expected.IsFile &&
            Uri.TryCreate(url, UriKind.Absolute, out var actual) && actual.IsFile &&
            string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expected.LocalPath, actual.LocalPath, StringComparison.OrdinalIgnoreCase);
    }
}
