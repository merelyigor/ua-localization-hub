using System.Net.Http;
using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Api;

public sealed class Winds4UaApiClient
{
    public const string LatestUrl = "https://winds4ua.com.ua/api/public/v1/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _timeout;

    public Winds4UaApiClient(HttpClient httpClient, ILogger logger, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<WwmApiResult> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        _logger.Debug($"Winds4UA latest request started: endpoint={LatestUrl}.");
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await _httpClient.GetAsync(LatestUrl, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"Winds4UA latest request returned HTTP {(int)response.StatusCode}.");
                return WwmApiResult.Failure($"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            var feed = await JsonSerializer.DeserializeAsync<WwmReleaseFeed>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, linked.Token).ConfigureAwait(false);
            if (feed?.Success != true || feed.Data?.Modes == null)
            {
                _logger.Warning("Winds4UA latest response rejected: successful data/modes are missing.");
                return WwmApiResult.Failure("The API response has no successful data/modes.");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mode in feed.Data.Modes)
            {
                if (string.IsNullOrWhiteSpace(mode.Slug) || string.IsNullOrWhiteSpace(mode.Variant)
                    || string.IsNullOrWhiteSpace(mode.Label) || !identities.Add($"{mode.Slug}\0{mode.Variant}"))
                {
                    _logger.Warning("Winds4UA latest response rejected: mode identity is invalid or duplicated.");
                    return WwmApiResult.Failure("The API response contains invalid or duplicate mode identity.");
                }
            }

            var availableModes = string.Join(",", feed.Data.Modes
                .Where(mode => mode.Available)
                .Select(mode => $"{mode.Slug}/{mode.Variant}"));
            var currentVersion = string.IsNullOrWhiteSpace(feed.Data.Current?.Version)
                ? "none"
                : feed.Data.Current.Version;
            _logger.Info($"Winds4UA latest response accepted: current_version={currentVersion} available_modes={availableModes} generated_at={feed.GeneratedAt ?? "none"}.");

            return WwmApiResult.SuccessResult(feed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WwmApiResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Winds4UA latest request timed out.");
            return WwmApiResult.Failure("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"Winds4UA latest request failed: {ex.Message}");
            return WwmApiResult.Failure(ex.Message);
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Winds4UA response JSON is invalid: {ex.Message}");
            return WwmApiResult.Failure("Malformed API JSON.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected Winds4UA API failure: {ex.Message}");
            return WwmApiResult.Failure("Unexpected API failure.");
        }
    }
}

public sealed record WwmApiResult(bool IsSuccess, bool IsCancelled, WwmReleaseFeed? Feed, string? Error)
{
    public static WwmApiResult SuccessResult(WwmReleaseFeed feed) => new(true, false, feed, null);
    public static WwmApiResult Failure(string error) => new(false, false, null, error);
    public static WwmApiResult Cancelled() => new(false, true, null, null);
}
