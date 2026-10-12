using System.Net.Http;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class WwmGameSession : IGameSession
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    private WwmGameSession(AppPaths paths, ILogger logger, HttpClient httpClient, bool ownsHttpClient, IEnumerable<string>? steamRoots = null)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        var definition = WwmGameDefinition.Default;
        Descriptor = new GameDescriptor(definition.Id, definition.DisplayName);
        PersistencePaths = paths.GetGamePersistencePaths(definition.Id);
        PersistencePaths.EnsureDirectories();
        ConfigStore = new ConfigStore(PersistencePaths, logger);
        StateStore = new WwmStateStore(PersistencePaths);
        Detector = new WwmSteamDetector(logger, steamRoots);
        ApiClient = new Winds4UaApiClient(httpClient, logger);
        InstallService = new WwmInstallService(httpClient, PersistencePaths, StateStore, logger, definition);
        Poller = new WwmReleaseFeedPoller(ApiClient);
    }

    public GameDescriptor Descriptor { get; }
    public GamePersistencePaths PersistencePaths { get; }
    public ConfigStore ConfigStore { get; }
    public WwmStateStore StateStore { get; }
    public WwmSteamDetector Detector { get; }
    public Winds4UaApiClient ApiClient { get; }
    public WwmInstallService InstallService { get; }
    public WwmReleaseFeedPoller Poller { get; }
    public string? GameRoot { get; private set; }
    public string? SteamBuildId { get; private set; }
    public WwmDetectionResult CurrentDetection { get; private set; } = WwmDetectionResult.NotFound;
    public bool MutationSupported => CurrentDetection.IsMutationSupported;
    public bool IsDisposed => _disposed;

    public WwmDetectionResult DetectGame()
    {
        var config = ConfigStore.Load().Value;
        var result = Detector.Detect(config?.GamePath);
        CurrentDetection = result;
        GameRoot = result.GameRoot;
        SteamBuildId = result.BuildId;
        return result;
    }

    public async Task<WwmManualDetectionResult> ResolveAndSaveManualPathAsync(string selectedPath, CancellationToken token = default)
    {
        var resolution = Detector.ResolveManualRoot(selectedPath);
        if (resolution.Status != ManualResolveStatus.Found || resolution.GamePath == null)
            return new WwmManualDetectionResult(resolution.Status, null);

        var detection = Detector.DescribeRoot(resolution.GamePath, "manual-folder-selection");
        if (!detection.IsFound) return new WwmManualDetectionResult(ManualResolveStatus.NotFound, null);
        var loaded = ConfigStore.Load();
        var config = loaded.Value ?? new Config();
        config.GamePath = detection.GameRoot;
        await ConfigStore.SaveAsync(config, token).ConfigureAwait(false);
        CurrentDetection = detection;
        GameRoot = detection.GameRoot;
        SteamBuildId = detection.BuildId;
        return new WwmManualDetectionResult(ManualResolveStatus.Found, detection);
    }

    public static WwmGameSession CreateProduction(AppPaths paths, ILogger logger)
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }, disposeHandler: true);
        try { return new WwmGameSession(paths, logger, client, true); }
        catch { client.Dispose(); throw; }
    }

    internal static WwmGameSession CreateForTests(AppPaths paths, ILogger logger, HttpClient client,
        bool ownsHttpClient = false, IEnumerable<string>? steamRoots = null)
        => new(paths, logger, client, ownsHttpClient, steamRoots);

    public Task StopAsync() => Poller.StopAsync();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Poller.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public sealed record WwmManualDetectionResult(ManualResolveStatus Status, WwmDetectionResult? Detection);
