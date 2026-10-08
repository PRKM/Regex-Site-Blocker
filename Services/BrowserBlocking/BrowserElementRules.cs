namespace Regex_Site_Blocker_App.Services.BrowserBlocking
{
    public static class BrowserElementRules
    {
        public static bool IsBusy(uint state) => (state & 0x800) != 0; // MSAA STATE_SYSTEM_BUSY

        public static bool IsStop(string id, string? name, string? legacyName = null,
            string? helpText = null, string? legacyDescription = null, string? fullDescription = null) =>
            id == "stop-button" || StopName(name) || StopName(legacyName) ||
            StopName(helpText) || StopName(legacyDescription) || StopName(fullDescription);

        private static bool StopName(string? name) => name != null &&
            (name.StartsWith("Stop", StringComparison.OrdinalIgnoreCase) || name.Contains("중지"));

        public static bool IsAddress(string browser, string controlType, string id, string name)
        {
            if (browser == "firefox" && controlType is "Edit" or "ComboBox" && id is "urlbar-input" or "urlbar") return true;
            if (controlType != "Edit") return false;
            return name.Contains("주소", StringComparison.OrdinalIgnoreCase) && (name.Contains("검색") || name.Contains("입력")) ||
                name.Contains("Address and search", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Search or enter address", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Search with Google or enter address", StringComparison.OrdinalIgnoreCase);
        }

        public static string? ReadValue(string? value, string? legacyValue, string id, string? name)
        {
            if (!string.IsNullOrEmpty(value)) return value;
            if (!string.IsNullOrEmpty(legacyValue)) return legacyValue;
            // Firefox's address combobox can expose the URL as Name while both Value patterns are empty.
            // This fallback is restricted to its browser chrome ID; never use a webpage's title as a URL.
            if (id == "urlbar-input" && Uri.TryCreate(name, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file") return name;
            return null;
        }
    }
}
