using Regex_Site_Blocker_App.Services.BrowserBlocking;
using Xunit;

public sealed class BrowserRedirectVerificationTests
{
    private const string destination = "file:///C:/App/blocked.html#example.com";
    private static readonly BrowserObservation target = new(1, "tab", "https://allowed.test/", "https://example.com/", false, true);

    [Fact]
    public void ChangingOnlyAddressBarDoesNotConfirmRedirect()
    {
        var verification = new BrowserRedirectVerification(target, destination);
        Assert.Equal(RedirectStatus.Pending, verification.Observe(target with { AddressUrl = destination }, TimeSpan.Zero));
        Assert.Equal(RedirectStatus.Pending, verification.Observe(target with { AddressUrl = destination }, TimeSpan.FromMilliseconds(500)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SlowBlockedPageNavigationDoesNotRetryAgainstPreviousBlockedDocument(bool loading)
    {
        var current = target with { DocumentUrl = target.AddressUrl, AddressUrl = destination, Loading = loading };
        Assert.Equal(RedirectStatus.Pending, new BrowserRedirectVerification(target, destination)
            .Observe(current, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void OurAddressFocusDoesNotCancelDestinationWait()
    {
        Assert.Equal(RedirectStatus.Pending, new BrowserRedirectVerification(target, destination)
            .Observe(target with { AddressUrl = destination, AddressFocused = true }, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void UserEditingAnotherAddressAbandonsPendingRedirect()
    {
        Assert.Equal(RedirectStatus.ContextChanged, new BrowserRedirectVerification(target, destination)
            .Observe(target with { AddressUrl = "https://other.test/", AddressFocused = true }, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(destination)]
    [InlineData("file:///C:/App/blocked.html")]
    public void VisibleBlockedDocumentConfirmsEvenWithoutFragment(string document)
    {
        Assert.Equal(RedirectStatus.Confirmed, new BrowserRedirectVerification(target, destination)
            .Observe(target with { DocumentUrl = document }, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void OriginalSiteCommitAfterSubmitIsUnconfirmedAndBecomesRetryable()
    {
        var verification = new BrowserRedirectVerification(target, destination);
        var originalSite = target with { DocumentUrl = target.AddressUrl, Loading = false };
        Assert.Equal(RedirectStatus.Pending, verification.Observe(originalSite, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(RedirectStatus.TimedOut, verification.Observe(originalSite, TimeSpan.FromMilliseconds(500)));
    }

    [Theory]
    [InlineData(2, "tab")]
    [InlineData(1, "other")]
    public void WindowOrTabSwitchAbandonsPreviousAttempt(int window, string tab)
    {
        Assert.Equal(RedirectStatus.ContextChanged, new BrowserRedirectVerification(target, destination)
            .Observe(target with { Window = window, Tab = tab, DocumentUrl = destination }, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("file:///C:/Other/blocked.html")]
    [InlineData("https://example.com/blocked.html")]
    public void MissingOrDifferentDocumentNeverConfirms(string? document)
    {
        Assert.Equal(RedirectStatus.Pending, new BrowserRedirectVerification(target, destination)
            .Observe(target with { DocumentUrl = document, AddressUrl = destination }, TimeSpan.Zero));
    }
}
