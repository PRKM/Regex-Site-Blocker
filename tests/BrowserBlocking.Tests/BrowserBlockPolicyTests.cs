using Regex_Site_Blocker_App.Config;
using Regex_Site_Blocker_App.Services.BrowserBlocking;
using Xunit;

public sealed class BrowserBlockPolicyTests
{
    private const string blocked = "https://example.com/page";
    private const string allowed = "https://allowed.test/";
    private static BrowserBlockPolicy Policy()
    {
        var policy = new BrowserBlockPolicy();
        policy.Apply(new() { Blacklist = [new() { Pattern = @"(^|\.)example\.com$", Enable = true }] }); return policy;
    }
    private static BrowserObservation Observe(string? document = blocked, string? address = blocked,
        bool focused = false, bool loading = false, string tab = "a", int window = 1) => new(window, tab, document, address, focused, loading);

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://WWW.EXAMPLE.COM./page")]
    [InlineData("http://example.com:8080/")]
    public void CompletedDocumentIsBlockedWithoutLoadingSignal(string url)
    { Assert.NotNull(Policy().Evaluate(Observe(document: url, address: url))); }

    [Fact]
    public void TypingAndFocusLossWithoutNavigationDoNotBlockEditedUrl()
    {
        var policy = Policy();
        Assert.Null(policy.Evaluate(Observe(allowed, blocked, focused: true)));
        Assert.Null(policy.Evaluate(Observe(allowed, blocked)));
        Assert.Null(policy.Evaluate(Observe(allowed, blocked)));
    }

    [Fact]
    public void LoadingTransitionUsesNavigationAddressBeforeDocumentUpdates()
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed));
        var decision = policy.Evaluate(Observe(allowed, blocked, loading: true));
        Assert.Equal(blocked, decision?.Url);
        Assert.NotNull(policy.Evaluate(Observe(allowed, blocked, loading: true)));
    }

    [Fact]
    public void AddressEditingWhileLoadingIsNeverRedirected()
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed));
        Assert.Null(policy.Evaluate(Observe(allowed, blocked, focused: true, loading: true)));
        Assert.Null(policy.Evaluate(Observe(allowed, blocked, loading: true)));
    }

    [Theory]
    [InlineData("b", 1)] [InlineData("a", 2)]
    public void TabOrWindowSwitchChecksCompletedDocument(string tab, int window)
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed));
        Assert.NotNull(policy.Evaluate(Observe(tab: tab, window: window)));
    }

    [Fact]
    public void NewSelectedTabAlreadyLoadingUsesAddressBeforeDocumentUpdates()
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed));
        Assert.Equal(blocked, policy.Evaluate(Observe(allowed, blocked, loading: true, tab: "new"))?.Url);
        Assert.NotNull(policy.Evaluate(Observe(blocked, blocked, loading: true, tab: "new")));
    }

    [Fact]
    public void RedirectAndCachedHistoryAreBlockedByDocumentUrl()
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed, loading: true));
        Assert.NotNull(policy.Evaluate(Observe(blocked, blocked)));
        policy.Evaluate(Observe(allowed, allowed));
        Assert.NotNull(policy.Evaluate(Observe(blocked, blocked)));
    }

    [Fact]
    public void DocumentChecksDoNotOverwriteFocusedAddress()
    { Assert.Null(Policy().Evaluate(Observe(focused: true))); }

    [Fact]
    public void FirstLoadingObservationWithoutDocumentUsesAddress()
    { Assert.Equal(blocked, Policy().Evaluate(Observe(null, blocked, loading: true))?.Url); }

    [Fact]
    public void MissingDocumentWithoutLoadingDoesNotTreatAddressAsNavigation()
    { Assert.Null(Policy().Evaluate(Observe(null, blocked))); }

    [Fact]
    public void SubmittedAddressIsBlockedDuringConnectionWaitBeforeDocumentExists()
    {
        var policy = Policy();
        Assert.Null(policy.Evaluate(Observe(allowed, blocked, focused: true)));
        var decision = policy.Evaluate(Observe(null, blocked, loading: true));
        Assert.Equal(blocked, decision?.Url);
        Assert.True(BrowserBlockPolicy.CanRedirect(decision!, decision!.Observation, false));
    }

    [Fact]
    public void LinkNavigationUsesUpdatedAddressWhileOldDocumentRemainsVisible()
    {
        var policy = Policy();
        Assert.Null(policy.Evaluate(Observe(allowed, allowed)));
        Assert.Null(policy.Evaluate(Observe(allowed, allowed, loading: true)));
        // Browser address update can arrive after its loading notification.
        var decision = policy.Evaluate(Observe(allowed, blocked, loading: true));
        Assert.Equal(blocked, decision?.Url);
        Assert.Equal(allowed, decision?.Observation.DocumentUrl);
    }

    [Fact]
    public void LoadingAddressIsUpdatedAcrossServerRedirectsBeforeDocumentUpdates()
    {
        var policy = Policy();
        Assert.Null(policy.Evaluate(Observe(null, allowed, loading: true)));
        Assert.Equal(blocked, policy.Evaluate(Observe(null, blocked, loading: true))?.Url);
    }

    [Theory]
    [InlineData("https://notexample.com/")]
    [InlineData("https://example.com.evil/")]
    [InlineData("https://allowed.test/example.com")]
    [InlineData("file:///C:/blocked.html#example.com")]
    [InlineData("chrome://settings/")]
    [InlineData("about:blank")]
    [InlineData("example.com")]
    public void OnlyHttpHostIsMatched(string url)
    { Assert.Null(Policy().Evaluate(Observe(url, url))); }

    [Fact]
    public void RuleUpdatesApplyToAlreadyDisplayedPageAndSkipInvalidOrDisabledRules()
    {
        var policy = new BrowserBlockPolicy(); var observation = Observe();
        Assert.Null(policy.Evaluate(observation));
        policy.Apply(new() { Blacklist = [new() { Pattern = "[", Enable = true },
            new() { Pattern = "example", Enable = false }, new() { Pattern = "example", Enable = true }] });
        Assert.Single(policy.Issues); Assert.NotNull(policy.Evaluate(observation));
        policy.Apply(new()); Assert.Null(policy.Evaluate(observation));
    }

    [Fact]
    public void RegexTimeoutProducesBlockDecisionInsteadOfStoppingMonitor()
    {
        var policy = new BrowserBlockPolicy(); policy.Apply(new() { Blacklist = [new() { Pattern = "(a+)+$", Enable = true }] });
        string url = "https://" + new string('a', 60) + "b.test/";
        Assert.True(policy.Evaluate(Observe(url, url))?.Timeout);
    }

    [Fact]
    public void InternationalDomainsUseAsciiHostnames()
    {
        Assert.True(BrowserBlockPolicy.TryHost("https://bücher.example/", out var host));
        Assert.Equal("xn--bcher-kva.example", host);
    }

    [Fact]
    public void ResetDiscardsPendingNavigationFromPreviousWindow()
    {
        var policy = Policy(); policy.Evaluate(Observe(allowed, allowed));
        Assert.NotNull(policy.Evaluate(Observe(allowed, blocked, loading: true)));
        policy.Reset(); Assert.Null(policy.Evaluate(Observe(allowed, allowed, loading: true)));
    }

    [Fact]
    public void RedirectRevalidationRejectsChangedTabWindowUrlFocusAndPressedModifiers()
    {
        var observation = Observe(); var decision = Policy().Evaluate(observation)!;
        Assert.True(BrowserBlockPolicy.CanRedirect(decision, observation, false));
        foreach (var changed in new[] { observation with { Window = 2 }, observation with { Tab = "other" },
            observation with { DocumentUrl = allowed }, observation with { AddressUrl = allowed },
            observation with { AddressFocused = true }, observation with { Loading = true } })
            Assert.False(BrowserBlockPolicy.CanRedirect(decision, changed, false));
        Assert.False(BrowserBlockPolicy.CanRedirect(decision, observation, true));
    }

    [Fact]
    public void BlockPageIsLocalAndEncodesUntrustedHostAsFragment()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string page = BlockPage.Create(directory);
            Assert.True(new Uri(page).IsFile);
            Assert.Equal(BlockPage.Html, File.ReadAllText(new Uri(page).LocalPath));
            Assert.EndsWith("#%3Cscript%3E", BlockPage.Destination(page, "<script>"));
            Assert.Contains("textContent=", BlockPage.Html);
        }
        finally { Directory.Delete(directory, true); }
    }
}
