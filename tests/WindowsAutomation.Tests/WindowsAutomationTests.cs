using Regex_Site_Blocker_App.Config;
using Regex_Site_Blocker_App.Services;
using Regex_Site_Blocker_App.WinUI;
using Xunit;
using System.Runtime.InteropServices;

public sealed class WindowsAutomationTests
{
    [Theory]
    [InlineData(false, 0x8000u, true)]
    [InlineData(true, 0u, false)]
    [InlineData(null, 0x100004u, true)]
    [InlineData(null, 0x8000u, false)]
    [InlineData(null, 0x10000u, false)]
    [InlineData(null, null, null)]
    public void VisibilityUsesSupportedUiaThenLegacyAndKeepsMissingStateUnknown(bool? offscreen, uint? legacy, bool? expected)
    { Assert.Equal(expected, BrowserAutomationAdapter.Visibility(() => offscreen, () => legacy)); }

    [Fact]
    public void UnsupportedIsOffscreenDoesNotAbortLegacyVisibilityRecovery()
    {
        Assert.True(BrowserAutomationAdapter.Visibility(() => throw new FlaUI.Core.Exceptions.PropertyNotSupportedException(
            FlaUI.Core.Identifiers.PropertyId.Register(FlaUI.Core.AutomationType.UIA3, 30022, "IsOffscreen")), () => 0u));
        Assert.Null(BrowserAutomationAdapter.Visibility(() => throw new COMException("detached", unchecked((int)0x80040201)), () => null));
    }

    [Fact]
    public void DelayedFirefoxAddressValueIsConfirmedBeforeSubmission()
    {
        int reads = 0;
        Assert.Equal("CONFIRMED", BrowserAutomationAdapter.ConfirmAddress(() =>
            (++reads >= 3 ? "CONFIRMED" : "ADDRESS_VALUE_NOT_CONFIRMED", true), TimeSpan.FromSeconds(1)));
        Assert.Equal(3, reads);
    }

    [Theory]
    [InlineData("ADDRESS_FOCUS_LOST")]
    [InlineData("TARGET_CHANGED_OR_MODIFIER_PRESSED")]
    public void FocusOrTargetChangeAbortsAddressConfirmationWithoutFurtherReads(string reason)
    {
        int reads = 0;
        Assert.Equal(reason, BrowserAutomationAdapter.ConfirmAddress(() =>
            (++reads == 1 ? "ADDRESS_VALUE_NOT_CONFIRMED" : reason, reads == 1), TimeSpan.FromSeconds(1)));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void UnconfirmedAddressExpiresInsteadOfSubmitting()
    {
        Assert.Equal("ADDRESS_VALUE_NOT_CONFIRMED", BrowserAutomationAdapter.ConfirmAddress(() =>
            ("ADDRESS_VALUE_NOT_CONFIRMED", true), TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void ChromiumAddressConfirmationKeepsSingleImmediateRead()
    {
        int reads = 0;
        Assert.Equal("ADDRESS_VALUE_NOT_CONFIRMED", BrowserAutomationAdapter.ConfirmAddress(() =>
            (++reads == 2 ? "CONFIRMED" : "ADDRESS_VALUE_NOT_CONFIRMED", true), TimeSpan.Zero));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void TransientOptionalPropertyFailureUsesFallbackAndNextReadRecovers()
    {
        Assert.Null(BrowserAutomationAdapter.Optional<string?>(() => throw new COMException("provider retry", unchecked((int)0x80040201)), null));
        Assert.Equal("https://example.com/", BrowserAutomationAdapter.Optional<string?>(() => "https://example.com/", null));
    }

    [Fact]
    public void UnrelatedComFailureIsNotHiddenByOptionalPropertyFallback()
    {
        var failure = new COMException("element unavailable", unchecked((int)0x80040200));
        Assert.Same(failure, Assert.Throws<COMException>(() => BrowserAutomationAdapter.Optional<string?>(() => throw failure, null)));
    }
    [Fact]
    public async Task NativeAutomationStartsStopsAndRestartsWithoutRulesOrBrowserInput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "UiaSmoke", Guid.NewGuid().ToString("N"));
        var source = new Source(); var service = new BrowserBlockingService(source, directory);
        var failures = new List<Exception>(); service.Failed += failures.Add;
        try
        {
            await service.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await service.StartAsync(); Assert.Equal(1, source.Subscribers);
            await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, source.Subscribers); Assert.Empty(failures);
            Assert.True(File.Exists(Path.Combine(directory, "blocked.html")));
            await service.StartAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await service.StopAsync(); Assert.Empty(failures); Assert.Equal(0, source.Subscribers);
        }
        finally { await service.StopAsync(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidConfigNeverStartsAutomationOrLeavesSubscription()
    {
        var source = new Source { FailLoad = true }; var service = new BrowserBlockingService(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.StartAsync());
        Assert.Equal(0, source.Subscribers); await service.StopAsync();
    }

    private sealed class Source : IConfigSource
    {
        public event Action<AppConfig>? Changed;
        public bool FailLoad;
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default) =>
            FailLoad ? throw new InvalidDataException("fake") : Task.FromResult(new AppConfig());
    }
}
