using Regex_Site_Blocker_App.Services.BrowserBlocking;
using Xunit;

public sealed class BrowserElementRulesTests
{
    [Theory]
    [InlineData("Stop loading this page")]
    [InlineData("이 페이지 로드 중지")]
    public void ChromeStopIsDetectedWhenOnlyFullDescriptionChanges(string description)
    { Assert.True(BrowserElementRules.IsStop("view_1003", "새로고침", "새로고침", "", "", description)); }
    [Theory]
    [InlineData(0x800u, true)]
    [InlineData(0x804u, true)]
    [InlineData(0u, false)]
    [InlineData(0x4u, false)]
    public void BusyStateIsDetectedIndependentlyOfOtherStateBits(uint state, bool busy)
    { Assert.Equal(busy, BrowserElementRules.IsBusy(state)); }

    [Fact]
    public void StopButtonAcceptsLegacyNameButCombinedReloadIdAloneIsNotLoading()
    {
        Assert.True(BrowserElementRules.IsStop("stop-reload-button", "", "Stop loading"));
        Assert.True(BrowserElementRules.IsStop("", "", "로드 중지"));
        Assert.False(BrowserElementRules.IsStop("stop-reload-button", "Reload", "Reload"));
    }

    [Theory]
    [InlineData("Stop loading this page")]
    [InlineData("이 페이지 로드 중지")]
    public void ChromeReloadNameCanExposeStopInHelpText(string help)
    { Assert.True(BrowserElementRules.IsStop("", "Reload", "Reload", helpText: help)); }

    [Theory]
    [InlineData("Stop loading this page")]
    [InlineData("이 페이지 로드 중지")]
    public void ChromeReloadNameCanExposeStopInLegacyDescription(string description)
    { Assert.True(BrowserElementRules.IsStop("", "Reload", "Reload", legacyDescription: description)); }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Reload this page")]
    [InlineData("이 페이지 새로고침")]
    public void IdleReloadDescriptionsDoNotInventLoading(string? description)
    { Assert.False(BrowserElementRules.IsStop("", "Reload", "Reload", description, description, description)); }
    [Theory]
    [InlineData("ComboBox", "urlbar-input", "https://example.com/")]
    [InlineData("ComboBox", "urlbar-input", "Google 검색 또는 주소 입력")]
    [InlineData("Edit", "urlbar-input", "")]
    public void FirefoxAddressDoesNotRequireEditControlOrFixedName(string type, string id, string name)
    { Assert.True(BrowserElementRules.IsAddress("firefox", type, id, name)); }

    [Fact]
    public void GenericComboboxAndWebpageTitleAreNeverAddressFallbacks()
    {
        Assert.False(BrowserElementRules.IsAddress("firefox", "ComboBox", "page-search", "주소 입력"));
        Assert.Null(BrowserElementRules.ReadValue("", "", "page-search", "https://example.com/"));
        Assert.Null(BrowserElementRules.ReadValue("", "", "urlbar-input", "Google 검색 또는 주소 입력"));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("file:///C:/blocked.html#example.com")]
    public void FirefoxUrlNameIsUsedWhenBothValuePatternsAreEmpty(string url)
    { Assert.Equal(url, BrowserElementRules.ReadValue("", "", "urlbar-input", url)); }

    [Fact]
    public void ActualValueTakesPriorityOverName()
    { Assert.Equal("https://allowed.test/", BrowserElementRules.ReadValue("https://allowed.test/", "", "urlbar-input", "https://example.com/")); }
}
