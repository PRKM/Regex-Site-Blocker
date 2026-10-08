using Regex_Site_Blocker_App.Config;
using Regex_Site_Blocker_App.Services;
using Xunit;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RegexConfigTests", Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(directory, "config.yaml");
    private ConfigService Service => new(ConfigPath);

    [Fact]
    public async Task MissingFileCreatesDefaultsAndSubsequentLoadsPreserveChanges()
    {
        var config = await Service.LoadAsync();
        Assert.False(config.StartProgram);
        Assert.Empty(config.Blacklist);
        config.StartProgram = true;
        config.Blacklist.Add(new() { Pattern = @"(^|\.)example\.com$", Enable = true });
        await Service.SaveAsync(config);
        var loaded = await Service.LoadAsync();
        Assert.True(loaded.StartProgram);
        Assert.Equal(config.Blacklist[0].Pattern, loaded.Blacklist[0].Pattern);
        Assert.True(loaded.Blacklist[0].Enable);
        Assert.DoesNotContain("version:", await File.ReadAllTextAsync(ConfigPath));
    }

    [Fact]
    public async Task EditsEnablementAndDeletionSurviveReload()
    {
        var config = await Service.LoadAsync();
        config.Blacklist.Add(new() { Pattern = "example" });
        await Service.SaveAsync(config);
        config.Blacklist[0].Pattern = "한국";
        config.Blacklist[0].Enable = true;
        await Service.SaveAsync(config);
        var loaded = await Service.LoadAsync();
        Assert.Equal("한국", loaded.Blacklist[0].Pattern);
        Assert.True(loaded.Blacklist[0].Enable);
        config.Blacklist.RemoveAt(0);
        await Service.SaveAsync(config);
        Assert.Empty((await Service.LoadAsync()).Blacklist);
    }

    [Theory]
    [InlineData("")]
    [InlineData("blacklist: [")]
    [InlineData("blacklist: null")]
    [InlineData("start-program: true\nstart-program: false")]
    public async Task InvalidFileIsNotOverwritten(string yaml)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(ConfigPath, yaml);
        await Assert.ThrowsAnyAsync<Exception>(() => Service.LoadAsync());
        Assert.Equal(yaml, await File.ReadAllTextAsync(ConfigPath));
    }

    [Fact]
    public async Task InvalidRegexLoadsForIndividualFilterExclusion()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(ConfigPath, "blacklist:\n  - pattern: '['\n    enable: true");
        Assert.Equal("[", (await Service.LoadAsync()).Blacklist[0].Pattern);
    }

    [Fact]
    public async Task OnlySuccessfulLocalSavesPublishIsolatedSnapshots()
    {
        var service = Service;
        var config = await service.LoadAsync();
        var received = new List<AppConfig>();
        service.Changed += snapshot => { snapshot.StartProgram = false; throw new Exception("observer"); };
        service.Changed += received.Add;
        config.StartProgram = true;
        await service.SaveAsync(config);
        Assert.True(Assert.Single(received).StartProgram);
        await service.SaveAsync(config, Path.Combine(directory, "export.yaml"));
        config.Blacklist.Add(new() { Pattern = "[" });
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SaveAsync(config));
        Assert.Single(received);
    }

    [Fact]
    public async Task InvalidSaveAndCancellationPreserveExistingFile()
    {
        var config = await Service.LoadAsync();
        var original = await File.ReadAllTextAsync(ConfigPath);
        config.Blacklist.Add(new() { Pattern = "[" });
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.SaveAsync(config));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.SaveAsync(new(), new CancellationToken(true)));
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath));
    }

    [Fact]
    public async Task QueuedWritesUseSnapshotsAndLatestCallWins()
    {
        var service = Service;
        var config = await service.LoadAsync();
        config.Blacklist.Add(new() { Pattern = "first" });
        var first = service.SaveAsync(config);
        config.Blacklist[0].Pattern = "last";
        var last = service.SaveAsync(config);
        config.Blacklist[0].Pattern = "unsaved";
        await Task.WhenAll(first, last);
        Assert.Equal("last", (await service.LoadAsync()).Blacklist[0].Pattern);
    }

    [Fact]
    public async Task FailedReplacementCleansTemporaryFile()
    {
        Directory.CreateDirectory(ConfigPath);
        await Assert.ThrowsAnyAsync<Exception>(() => Service.SaveAsync(new()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.True(Directory.Exists(ConfigPath));
    }

    [Fact]
    public void ModelsNotifyOnlyWhenValuesChange()
    {
        var config = new AppConfig();
        var changed = new List<string?>();
        config.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        config.StartProgram = true;
        config.StartProgram = true;
        Assert.Equal([nameof(AppConfig.StartProgram)], changed);
        var rule = new BlacklistRule();
        changed.Clear();
        rule.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        rule.Pattern = "example";
        rule.Enable = true;
        Assert.Equal([nameof(BlacklistRule.Pattern), nameof(BlacklistRule.Enable)], changed);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
