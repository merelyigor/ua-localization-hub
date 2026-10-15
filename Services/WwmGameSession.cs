using System.Net.Http;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class WwmGameSession : IGameSession
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _manualDetectionGate = new(1, 1);
    private bool _disposed;

    private WwmGameSession(AppPaths paths, ILogger logger, HttpClient httpClient, bool ownsHttpClient,
        IEnumerable<string>? steamRoots = null, Func<string, IEnumerable<string>>? manualDirectoryEnumerator = null,
        int maximumManualDirectories = WwmSteamDetector.DefaultMaximumManualDirectories,
        int maximumManualDepth = WwmSteamDetector.DefaultMaximumManualDepth,
        string? epicManifestDirectory = null)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        var definition = WwmGameDefinition.Default;
        Descriptor = new GameDescriptor(definition.Id, definition.DisplayName);
        PersistencePaths = paths.GetGamePersistencePaths(definition.Id);
        PersistencePaths.EnsureDirectories();
        ConfigStore = new ConfigStore(PersistencePaths, logger);
        StateStore = new WwmStateStore(PersistencePaths);
        Detector = new WwmSteamDetector(logger, steamRoots, definition,
            manualDirectoryEnumerator ?? (path => Directory.EnumerateDirectories(path)),
            maximumManualDirectories, maximumManualDepth, epicManifestDirectory);
        ApiClient = new Winds4UaApiClient(httpClient, logger);
        InstallService = new WwmInstallService(httpClient, PersistencePaths, StateStore, logger, definition, ConfigStore);
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
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetimeCts.Token);
        var searchToken = linkedCts.Token;
        await _manualDetectionGate.WaitAsync(searchToken).ConfigureAwait(false);
        ManualResolveResult resolution;
        try
        {
            resolution = await Task.Run(() => Detector.ResolveManualRoot(selectedPath, searchToken), searchToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _manualDetectionGate.Release();
        }
        searchToken.ThrowIfCancellationRequested();
        if (resolution.Status != ManualResolveStatus.Found || resolution.GamePath == null)
            return new WwmManualDetectionResult(resolution.Status, null);

        var detection = Detector.DescribeRoot(resolution.GamePath, "manual-folder-selection");
        if (!detection.IsFound) return new WwmManualDetectionResult(ManualResolveStatus.NotFound, null);
        var loaded = ConfigStore.Load();
        var config = loaded.Value ?? new Config();
        var persistedRoot = WwmGameDefinition.NormalizeInstallationRoot(detection.GameRoot!);
        config.GamePath = persistedRoot;
        await ConfigStore.SaveAsync(config, searchToken).ConfigureAwait(false);
        // A successful explicit selection plus persisted canonical path is trusted
        // legacy-root evidence for this session. Never grant it before SaveAsync.
        detection = detection with { PersistedGamePath = persistedRoot };
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
        bool ownsHttpClient = false, IEnumerable<string>? steamRoots = null,
        Func<string, IEnumerable<string>>? manualDirectoryEnumerator = null,
        int maximumManualDirectories = WwmSteamDetector.DefaultMaximumManualDirectories,
        int maximumManualDepth = WwmSteamDetector.DefaultMaximumManualDepth,
        string? epicManifestDirectory = null)
        => new(paths, logger, client, ownsHttpClient, steamRoots, manualDirectoryEnumerator,
            maximumManualDirectories, maximumManualDepth, epicManifestDirectory);

    public Task StopAsync() => Poller.StopAsync();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        Poller.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public sealed record WwmManualDetectionResult(ManualResolveStatus Status, WwmDetectionResult? Detection);
