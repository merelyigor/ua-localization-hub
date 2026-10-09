using System.Net.Http;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Services;

/// <summary>
/// Owns the concrete runtime composition for the currently supported game.
/// Stage 2 has one BDO session; switching and session replacement belong to a later stage.
/// </summary>
public sealed class BdoGameSession : IGameSession
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    private BdoGameSession(
        AppPaths appPaths,
        ILogger logger,
        AppVersionInfo appVersionInfo,
        HttpClient httpClient,
        bool ownsHttpClient,
        GameDescriptor descriptor,
        string persistenceGameId)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(appVersionInfo);
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;

        GameDefinition = BdoGameDefinition.Default;
        Descriptor = descriptor;
        PersistencePaths = appPaths.GetGamePersistencePaths(persistenceGameId);

        if (IsCanonicalBdoScope(PersistencePaths))
        {
            new LegacyBdoPersistenceMigrator(appPaths, PersistencePaths, logger).MigrateIfNeeded();
            new LegacyBdoReleaseFeedCacheMigrator(appPaths, PersistencePaths, logger)
                .ImportIfNeeded();
        }
        PersistencePaths.EnsureDirectories();

        ConfigStore = new ConfigStore(PersistencePaths, logger);
        InstallationStateStore = new InstallationStateStore(PersistencePaths, logger);
        BackupStore = new BackupStore(PersistencePaths, logger, GameDefinition);
        GameDetector = new GameDetector(ConfigStore, logger, GameDefinition);
        ApiClient = new BdoUaApiClient(_httpClient, logger);
        LocalizationInstaller = new LocalizationInstaller(_httpClient, appPaths, logger);
        LocalizationStateService = new LocalizationStateService(
            InstallationStateStore, logger, GameDefinition);
        LocalizationCompatibilityService = new LocalizationCompatibilityService();
        ReleaseFeedCacheStore = new ReleaseFeedCacheStore(PersistencePaths, logger);
        ReleaseFeedPoller = new ReleaseFeedPoller(ApiClient, logger);
    }

    public GameDescriptor Descriptor { get; }
    public BdoGameDefinition GameDefinition { get; }
    public GamePersistencePaths PersistencePaths { get; }
    public ConfigStore ConfigStore { get; }
    public InstallationStateStore InstallationStateStore { get; }
    public BackupStore BackupStore { get; }
    public GameDetector GameDetector { get; }
    public BdoUaApiClient ApiClient { get; }
    public LocalizationInstaller LocalizationInstaller { get; }
    public LocalizationStateService LocalizationStateService { get; }
    public LocalizationCompatibilityService LocalizationCompatibilityService { get; }
    public ReleaseFeedCacheStore ReleaseFeedCacheStore { get; }
    public ReleaseFeedPoller ReleaseFeedPoller { get; }
    internal bool IsDisposed => _disposed;

    private static bool IsCanonicalBdoScope(GamePersistencePaths paths) =>
        string.Equals(paths.GameId, BdoGameDefinition.Default.Id, StringComparison.Ordinal);

    public static BdoGameSession CreateProduction(
        AppPaths appPaths,
        ILogger logger,
        AppVersionInfo appVersionInfo)
    {
        var httpClient = BdoUaHttpClientConfiguration.CreateHttpClient(
            appVersionInfo, logger, TimeSpan.FromSeconds(30));

        try
        {
            return new BdoGameSession(
                appPaths, logger, appVersionInfo, httpClient, true,
                new GameDescriptor(BdoGameDefinition.Default.Id, BdoGameDefinition.Default.DisplayName),
                BdoGameDefinition.Default.Id);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal static BdoGameSession CreateForTests(
        AppPaths appPaths,
        ILogger logger,
        AppVersionInfo appVersionInfo,
        HttpClient httpClient,
        bool ownsHttpClient = false,
        GameDescriptor? descriptor = null)
    {
        var selectedDescriptor = descriptor
            ?? new GameDescriptor(BdoGameDefinition.Default.Id, BdoGameDefinition.Default.DisplayName);
        return new BdoGameSession(
            appPaths, logger, appVersionInfo, httpClient, ownsHttpClient,
            selectedDescriptor, selectedDescriptor.Id);
    }

    public Task StopAsync() => ReleaseFeedPoller.StopAsync();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseFeedPoller.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
