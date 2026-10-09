using System.Text.Json;
using System.Collections.Concurrent;
using BdoClient.Logging;

namespace BdoClient.Storage;

public sealed class ConfigStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configFile;
    private readonly ILogger _logger;

    // Legacy-layout adapter is retained only for compatibility fixtures and
    // bounded migration tests; production composition uses GamePersistencePaths.
    internal ConfigStore(AppPaths paths, ILogger logger)
        : this(paths?.ConfigFile ?? throw new ArgumentNullException(nameof(paths)), logger)
    {
    }

    public ConfigStore(GamePersistencePaths paths, ILogger logger)
        : this(paths?.ConfigFile ?? throw new ArgumentNullException(nameof(paths)), logger)
    {
    }

    private ConfigStore(string configFile, ILogger logger)
    {
        _configFile = configFile;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FileLoadResult<Config> Load()
    {
        var gate = GetFileLock();
        gate.Wait();
        try
        {
            if (!File.Exists(_configFile))
            {
                _logger.Debug("Config file not found, using defaults");
                return FileLoadResult<Config>.Missing(new Config());
            }

            try
            {
                var json = File.ReadAllText(_configFile);
                var config = JsonSerializer.Deserialize<Config>(json, JsonOptions);

                if (config == null)
                {
                    _logger.Warning("Config file deserialized to null");
                    return FileLoadResult<Config>.Invalid("Deserialized to null");
                }

                return FileLoadResult<Config>.Valid(config);
            }
            catch (JsonException ex)
            {
                _logger.Error($"Config file is invalid: {ex.Message}");
                return FileLoadResult<Config>.Invalid($"JSON error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to read config file: {ex.Message}");
                return FileLoadResult<Config>.Invalid($"Read error: {ex.Message}");
            }
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(Config config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempFile = _configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var gate = GetFileLock();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(_configFile))
            {
                File.Replace(tempFile, _configFile, null);
            }
            else
            {
                File.Move(tempFile, _configFile);
            }

            _logger.Debug("Config saved successfully");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save config: {ex.Message}");
            CleanupTempFile(tempFile);
            throw;
        }
        finally { gate.Release(); }
    }

    private SemaphoreSlim GetFileLock()
        => FileLocks.GetOrAdd(Path.GetFullPath(_configFile), static _ => new SemaphoreSlim(1, 1));

    private void CleanupTempFile(string tempFile)
    {
        try
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup temp file: {ex.Message}");
        }
    }
}
