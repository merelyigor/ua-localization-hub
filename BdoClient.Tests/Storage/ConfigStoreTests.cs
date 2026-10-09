using BdoClient.Storage;
using BdoClient.Logging;

namespace BdoClient.Tests.Storage;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly ConfigStore _store;
    private readonly NullLogger _logger = new();

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BdoClientTests_" + Guid.NewGuid().ToString("N")[..8]);
        _paths = new AppPaths(_tempDir);
        _paths.EnsureDirectories();
        _store = new ConfigStore(_paths, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsMissingWithDefaultConfig()
    {
        var result = _store.Load();
        Assert.Equal(FileLoadStatus.Missing, result.Status);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value!.GamePath);
        Assert.Null(result.Value!.LastMode);
    }

    [Fact]
    public async Task SaveAndLoad_AutostartPromptDismissedRoundtrip()
    {
        var store = new ApplicationConfigStore(_paths, _logger);
        var config = new ApplicationConfig { AutostartPromptDismissed = true };

        await store.SaveAsync(config);
        var result = store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.True(result.Value!.AutostartPromptDismissed);
    }

    [Fact]
    public async Task ApplicationConfig_OldJsonWithoutSelectedGameId_LoadsAndPreservesAutostart()
    {
        await File.WriteAllTextAsync(_paths.ApplicationConfigFile, "{\"autostart_prompt_dismissed\":true}");

        var result = new ApplicationConfigStore(_paths, _logger).Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.True(result.Value!.AutostartPromptDismissed);
        Assert.Null(result.Value.SelectedGameId);
    }

    [Fact]
    public async Task ApplicationConfig_SelectedGameIdRoundtripPreservesAutostartFlag()
    {
        var store = new ApplicationConfigStore(_paths, _logger);
        await store.SaveAsync(new ApplicationConfig
        {
            AutostartPromptDismissed = true,
            SelectedGameId = "black-desert-online"
        });

        var result = store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.True(result.Value!.AutostartPromptDismissed);
        Assert.Equal("black-desert-online", result.Value.SelectedGameId);
    }

    [Fact]
    public async Task SaveAndLoad_PreservesAllFields()
    {
        var config = new Config
        {
            GamePath = @"C:\Games\Black Desert Online",
            LastMode = "full-ukrainian",
        };

        await _store.SaveAsync(config);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.Equal(@"C:\Games\Black Desert Online", result.Value!.GamePath);
        Assert.Equal("full-ukrainian", result.Value!.LastMode);
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip()
    {
        var config = new Config
        {
            GamePath = @"C:\Games\Black Desert Online",
            LastMode = "full-ukrainian"
        };

        await _store.SaveAsync(config);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.Equal(@"C:\Games\Black Desert Online", result.Value!.GamePath);
        Assert.Equal("full-ukrainian", result.Value!.LastMode);
    }

    [Fact]
    public async Task SaveAndLoad_UnicodeAndSpaces()
    {
        var config = new Config
        {
            GamePath = @"C:\Програми\Black Desert Online\Гра"
        };

        await _store.SaveAsync(config);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.Equal(@"C:\Програми\Black Desert Online\Гра", result.Value!.GamePath);
    }

    [Fact]
    public async Task SaveAndLoad_NullFields()
    {
        var config = new Config();
        await _store.SaveAsync(config);
        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Valid, result.Status);
        Assert.Null(result.Value!.GamePath);
        Assert.Null(result.Value!.LastMode);
    }

    [Fact]
    public async Task MalformedExistingFile_ReturnsInvalid()
    {
        await File.WriteAllTextAsync(_paths.ConfigFile, "not valid json");

        var result = _store.Load();

        Assert.Equal(FileLoadStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task MalformedFile_DoesNotModifyFile()
    {
        var originalContent = "not valid json";
        await File.WriteAllTextAsync(_paths.ConfigFile, originalContent);

        _store.Load();

        var content = await File.ReadAllTextAsync(_paths.ConfigFile);
        Assert.Equal(originalContent, content);
    }

    [Fact]
    public async Task Save_OverwritesExistingFile()
    {
        var config1 = new Config { GamePath = "path1" };
        var config2 = new Config { GamePath = "path2" };

        await _store.SaveAsync(config1);
        await _store.SaveAsync(config2);

        var result = _store.Load();
        Assert.Equal("path2", result.Value!.GamePath);
    }

    [Fact]
    public async Task Save_NoTempFileRemainsAfterSave()
    {
        var config = new Config { GamePath = "test" };
        await _store.SaveAsync(config);

        var tempFile = _paths.ConfigFile + ".tmp";
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task ConcurrentStores_SerializeAtomicSavesWithoutTempCollisionsOrCorruptJson()
    {
        var stores = Enumerable.Range(0, 6).Select(_ => new ConfigStore(_paths, _logger)).ToArray();
        var saves = Enumerable.Range(0, 60)
            .Select(index => stores[index % stores.Length].SaveAsync(new Config
            {
                GamePath = $@"C:\Games\Parallel {index}",
                LastMode = $"mode-{index}"
            }));
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var result = _store.Load();
                Assert.NotEqual(FileLoadStatus.Invalid, result.Status);
            }
        }));

        await Task.WhenAll(saves.Concat(readers));

        var loaded = _store.Load();
        Assert.Equal(FileLoadStatus.Valid, loaded.Status);
        Assert.StartsWith(@"C:\Games\Parallel ", loaded.Value!.GamePath);
        Assert.StartsWith("mode-", loaded.Value.LastMode);
        Assert.Empty(Directory.GetFiles(_tempDir, "config.json.*.tmp", SearchOption.AllDirectories));
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
