using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BdoClient.Logging;
using BdoClient.Models;

namespace BdoClient.Api;

public sealed class BdoUaApiClient
{
    private const string BaseUrl = "https://bdo-ua.com.ua/api/public/v1";
    private const int DefaultTimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly int _timeoutSeconds;
    private readonly object _latestCacheLock = new();
    private readonly Dictionary<string, LatestReleaseCacheEntry> _latestCache = new(StringComparer.Ordinal);

    public BdoUaApiClient(HttpClient httpClient, ILogger logger, int timeoutSeconds = DefaultTimeoutSeconds)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>
    /// Lightweight HEAD request to pre-warm DNS/TLS cache. Fire-and-forget at startup.
    /// </summary>
    public async Task WarmupConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var sw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://bdo-ua.com.ua/");
            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            sw.Stop();

            _logger.Debug($"Connection warmup completed in {sw.ElapsedMilliseconds}ms (status {(int)response.StatusCode})");
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Connection warmup cancelled/timed out");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Connection warmup failed: {ex.Message}");
        }
    }

    public async Task<ApiResult<ReleasesResponse>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/releases";
        _logger.Debug($"Fetching releases from {url}");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var totalSw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            var headersMs = totalSw.ElapsedMilliseconds;

            LogCorrelationHeaders(response);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var error = $"HTTP {statusCode} {response.ReasonPhrase}";
                _logger.Warning($"API error: {error}");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={statusCode} headers_ms={headersMs} total_ms={totalSw.ElapsedMilliseconds} error=Http");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Http, error);
            }

            var contentBytes = await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
            var bodyMs = totalSw.ElapsedMilliseconds - headersMs;

            if (contentBytes.Length == 0)
            {
                _logger.Warning("Empty API response");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} total_ms={totalSw.ElapsedMilliseconds} bytes=0");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Empty API response");
            }

            var content = Encoding.UTF8.GetString(contentBytes);

            var parseSw = Stopwatch.StartNew();
            ReleasesResponse? releases;
            try
            {
                releases = JsonSerializer.Deserialize<ReleasesResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                parseSw.Stop();
                _logger.Error($"JSON error: {ex.Message}");
                _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, $"JSON error: {ex.Message}");
            }
            parseSw.Stop();

            totalSw.Stop();
            _logger.Debug($"API timing: host=bdo-ua.com.ua status={(int)response.StatusCode} http={response.Version} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");

            if (releases == null)
            {
                _logger.Warning("Failed to deserialize API response");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "Failed to deserialize API response");
            }

            if (!releases.Success)
            {
                _logger.Warning("API response indicates failure");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "API response success=false");
            }

            if (releases.Data == null)
            {
                _logger.Warning("API response data is null");
                return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.InvalidResponse, "API response data=null");
            }

            _logger.Debug($"Successfully fetched releases: {releases.Data.Modes?.Count ?? 0} modes");
            return ApiResult<ReleasesResponse>.Success(releases);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.Warning($"API request timed out after {_timeoutSeconds}s");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Timeout");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Timeout, $"Request timed out after {_timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("API request cancelled");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Cancelled");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Cancelled, "Request cancelled");
        }
        catch (HttpRequestException ex)
        {
            var diag = NetworkDiagnostics.FormatNetworkError(ex);
            _logger.Error($"API network error: {diag}");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Network");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Network, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error: {ex.Message}");
            _logger.Debug($"API timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Unexpected");
            return ApiResult<ReleasesResponse>.Failure(ApiErrorKind.Unexpected, $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<LatestReleaseResult> GetLatestReleaseAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeSlug(slug))
            return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, "A valid mode slug is required.");

        var escapedSlug = Uri.EscapeDataString(slug);
        var url = $"{BaseUrl}/releases/latest/{escapedSlug}";
        LatestReleaseCacheEntry? cachedEntry;
        lock (_latestCacheLock)
            _latestCache.TryGetValue(slug, out cachedEntry);

        _logger.Debug($"Fetching latest release for mode '{slug}' from {url}");
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var totalSw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (cachedEntry?.ETag is { Length: > 0 } etag
                && EntityTagHeaderValue.TryParse(etag, out var parsedEtag))
            {
                request.Headers.IfNoneMatch.Add(parsedEtag);
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            var headersMs = totalSw.ElapsedMilliseconds;
            LogCorrelationHeaders(response);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                if (cachedEntry?.Data == null)
                {
                    _logger.Warning($"Latest API returned 304 without cached metadata for mode '{slug}'");
                    return LatestReleaseResult.Failure(
                        ApiErrorKind.InvalidResponse,
                        "HTTP 304 received without cached latest-release metadata.");
                }

                _logger.Debug($"Latest release for mode '{slug}' not modified; cached metadata retained.");
                return LatestReleaseResult.NotModified(cachedEntry.Data, cachedEntry.ETag);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var error = await TryReadLatestErrorAsync(response.Content, linkedCts.Token).ConfigureAwait(false);
                var allowed = error?.Error == "unknown_mode" ? error.Allowed : null;
                var message = allowed is { Count: > 0 }
                    ? $"Unknown mode slug '{slug}'. Allowed slugs: {string.Join(", ", allowed)}"
                    : $"Unknown mode slug '{slug}'.";
                _logger.Warning($"Latest API unknown mode: {message}");
                return LatestReleaseResult.UnknownMode(allowed, ApiErrorKind.Http, message);
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var error = await TryReadLatestErrorAsync(response.Content, linkedCts.Token).ConfigureAwait(false);
                var retryAfter = TryReadRetryAfterSeconds(response);
                var message = error?.Error == "official_patch_unconfirmed"
                    ? error.Message
                    : null;
                message = string.IsNullOrWhiteSpace(message)
                    ? "Official game patch is not confirmed (HTTP 503)."
                    : message;
                _logger.Warning($"Latest API patch unconfirmed for mode '{slug}'. Retry-After={retryAfter?.TotalSeconds.ToString() ?? "unspecified"}s");
                return LatestReleaseResult.PatchUnconfirmed(retryAfter, ApiErrorKind.Http, message);
            }

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var message = $"HTTP {statusCode} {response.ReasonPhrase}";
                _logger.Warning($"Latest API error for mode '{slug}': {message}");
                _logger.Debug($"API latest timing: host=bdo-ua.com.ua status={statusCode} headers_ms={headersMs} total_ms={totalSw.ElapsedMilliseconds} error=Http");
                return LatestReleaseResult.Failure(ApiErrorKind.Http, message);
            }

            var contentBytes = await response.Content.ReadAsByteArrayAsync(linkedCts.Token).ConfigureAwait(false);
            var bodyMs = totalSw.ElapsedMilliseconds - headersMs;
            if (contentBytes.Length == 0)
            {
                _logger.Warning($"Empty latest API response for mode '{slug}'");
                _logger.Debug($"API latest timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} total_ms={totalSw.ElapsedMilliseconds} bytes=0");
                return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, "Empty latest API response.");
            }

            LatestReleaseResponse? latest;
            var parseSw = Stopwatch.StartNew();
            try
            {
                latest = JsonSerializer.Deserialize<LatestReleaseResponse>(Encoding.UTF8.GetString(contentBytes), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                parseSw.Stop();
                _logger.Error($"Latest API JSON error for mode '{slug}': {ex.Message}");
                _logger.Debug($"API latest timing: host=bdo-ua.com.ua status={(int)response.StatusCode} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");
                return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, $"JSON error: {ex.Message}");
            }
            parseSw.Stop();

            if (latest?.Success != true || latest.Data == null)
            {
                _logger.Warning($"Latest API response is missing successful data for mode '{slug}'");
                return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, "Latest API response success/data is invalid.");
            }

            var data = latest.Data;
            if (string.IsNullOrWhiteSpace(data.Filename)
                || string.IsNullOrWhiteSpace(data.InstallGuideUrl)
                || string.IsNullOrWhiteSpace(data.Mode?.Slug)
                || string.IsNullOrWhiteSpace(data.Mode.PublicName))
            {
                _logger.Warning($"Latest API response has missing required fields for mode '{slug}'");
                return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, "Latest API response has missing required fields.");
            }

            if (!string.Equals(data.Mode.Slug, slug, StringComparison.Ordinal))
            {
                var message = $"Latest API returned mode '{data.Mode.Slug}' for requested slug '{slug}'.";
                _logger.Error(message);
                return LatestReleaseResult.Failure(ApiErrorKind.InvalidResponse, message);
            }

            var responseEtag = TryReadEtag(response);
            lock (_latestCacheLock)
                _latestCache[slug] = new LatestReleaseCacheEntry(data, responseEtag);

            _logger.Debug($"Latest release metadata received for mode '{slug}' (current={(data.Current == null ? "none" : data.Current.PublicId ?? "present")}).");
            _logger.Debug($"API latest timing: host=bdo-ua.com.ua status={(int)response.StatusCode} http={response.Version} headers_ms={headersMs} body_ms={bodyMs} parse_ms={parseSw.ElapsedMilliseconds} total_ms={totalSw.ElapsedMilliseconds} bytes={contentBytes.Length}");
            return LatestReleaseResult.Modified(data, responseEtag);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.Warning($"Latest API request timed out after {_timeoutSeconds}s for mode '{slug}'");
            _logger.Debug($"API latest timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Timeout");
            return LatestReleaseResult.Failure(ApiErrorKind.Timeout, $"Request timed out after {_timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Latest API request cancelled for mode '{slug}'");
            _logger.Debug($"API latest timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Cancelled");
            return LatestReleaseResult.Failure(ApiErrorKind.Cancelled, "Request cancelled");
        }
        catch (HttpRequestException ex)
        {
            var diagnostic = NetworkDiagnostics.FormatNetworkError(ex);
            _logger.Error($"Latest API network error for mode '{slug}': {diagnostic}");
            _logger.Debug($"API latest timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Network");
            return LatestReleaseResult.Failure(ApiErrorKind.Network, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected latest API error for mode '{slug}': {ex.Message}");
            _logger.Debug($"API latest timing: host=bdo-ua.com.ua total_ms={totalSw.ElapsedMilliseconds} error=Unexpected");
            return LatestReleaseResult.Failure(ApiErrorKind.Unexpected, $"Unexpected error: {ex.Message}");
        }
    }

    private static bool IsSafeSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && string.Equals(slug, slug.Trim(), StringComparison.Ordinal)
        && slug[0] != '-'
        && slug[^1] != '-'
        && !slug.Contains("--", StringComparison.Ordinal)
        && slug.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-');

    private async Task<LatestReleaseErrorResponse?> TryReadLatestErrorAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
                return null;

            return JsonSerializer.Deserialize<LatestReleaseErrorResponse>(Encoding.UTF8.GetString(bytes), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.Debug($"Ignoring malformed optional latest API error body: {ex.Message}");
            return null;
        }
    }

    private static string? TryReadEtag(HttpResponseMessage response)
    {
        if (!response.Headers.NonValidated.TryGetValues("ETag", out var values))
            return null;

        foreach (var value in values)
        {
            if (EntityTagHeaderValue.TryParse(value, out var etag))
                return etag.ToString();
        }

        return null;
    }

    private static TimeSpan? TryReadRetryAfterSeconds(HttpResponseMessage response)
    {
        if (!response.Headers.NonValidated.TryGetValues("Retry-After", out var values))
            return null;

        var value = values.FirstOrDefault()?.Trim();
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
            return null;

        return TimeSpan.FromSeconds(seconds);
    }

    private sealed record LatestReleaseCacheEntry(LatestReleaseData Data, string? ETag);

    private void LogCorrelationHeaders(HttpResponseMessage response)
    {
        var parts = new List<string>();

        if (response.Headers.TryGetValues("X-Request-ID", out var requestIdValues))
        {
            var value = requestIdValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"request_id={value}");
        }

        if (response.Headers.TryGetValues("Server-Timing", out var serverTimingValues))
        {
            var value = serverTimingValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"server_timing=\"{value}\"");
        }

        if (response.Headers.TryGetValues("CF-Ray", out var cfRayValues))
        {
            var value = cfRayValues.FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
                parts.Add($"cf_ray={value}");
        }

        if (parts.Count > 0)
            _logger.Debug($"API correlation: {string.Join(" ", parts)}");
    }
}
