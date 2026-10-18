using System.Diagnostics;
using System.Security.Cryptography;
using BdoClient.Api;
using BdoClient.Logging;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient.Services;

public sealed class LocalizationInstaller
{
    private static readonly int[] DefaultRetryDelaysMs = { 1000, 2000, 4000 };
    private const int DefaultTimeoutSeconds = 60;
    private const int ReadBufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly AppPaths _appPaths;
    private readonly ILogger _logger;
    private readonly int _timeoutSeconds;
    private readonly int[] _retryDelaysMs;

    public LocalizationInstaller(HttpClient httpClient, AppPaths appPaths, ILogger logger,
        int timeoutSeconds = DefaultTimeoutSeconds, int[]? retryDelaysMs = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _appPaths = appPaths ?? throw new ArgumentNullException(nameof(appPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
        _retryDelaysMs = retryDelaysMs ?? DefaultRetryDelaysMs;
    }

    public LocalizationInstaller(AppPaths appPaths, ILogger logger,
        int timeoutSeconds = DefaultTimeoutSeconds, int[]? retryDelaysMs = null)
        : this(CreateDefaultHttpClient(logger), appPaths, logger, timeoutSeconds, retryDelaysMs) { }

    private static HttpClient CreateDefaultHttpClient(ILogger logger)
    {
        return BdoUaHttpClientConfiguration.CreateHttpClient(
            BdoClient.Update.AppVersionInfo.Detect(),
            logger);
    }

    public async Task<DownloadResult> DownloadReleaseAsync(
        CurrentRelease release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateReleaseMetadata(release);
        if (validationError != null)
            return validationError;

        var downloadUrl = release.DownloadUrl!;
        var expectedSize = release.SizeBytes;
        var expectedSha256 = release.Sha256!;
        _logger.Info(
            $"BDO localization download selected: public_id={release.PublicId}, version={release.Version}, " +
            $"patch={release.Patch}, filename={release.Filename}, size={expectedSize}, sha256={expectedSha256}.");

        DownloadError lastError = default;
        string? lastErrorMessage = null;

        for (var attempt = 0; attempt <= _retryDelaysMs.Length; attempt++)
        {
            if (attempt > 0)
            {
                _logger.Info($"Retry attempt {attempt}/{_retryDelaysMs.Length} for {downloadUrl}");
                await DelayWithCancellation(_retryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }

            var result = await ExecuteReleaseDownloadAsync(
                downloadUrl, expectedSize, expectedSha256, progress, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _logger.Info(
                    $"BDO localization download verified: public_id={release.PublicId}, version={release.Version}, " +
                    $"bytes={result.SizeBytes}, computed_sha256={result.Sha256}.");
                return result;
            }

            lastError = result.Error!.Value;
            lastErrorMessage = result.ErrorMessage;

            if (!result.IsRetryable)
                return result;
        }

        _logger.Error($"Download failed after {_retryDelaysMs.Length + 1} attempts: {downloadUrl}");
        return DownloadResult.Failure(lastError, lastErrorMessage ?? $"Failed after {_retryDelaysMs.Length + 1} attempts");
    }

    public async Task<DownloadResult> DownloadOfficialSourceAsync(
        string officialUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateOfficialUrl(officialUrl);
        if (validationError != null)
            return validationError;

        DownloadError lastError = default;
        string? lastErrorMessage = null;

        for (var attempt = 0; attempt <= _retryDelaysMs.Length; attempt++)
        {
            if (attempt > 0)
            {
                _logger.Info($"Retry attempt {attempt}/{_retryDelaysMs.Length} for official source");
                await DelayWithCancellation(_retryDelaysMs[attempt - 1], cancellationToken).ConfigureAwait(false);
            }

            var result = await ExecuteOfficialDownloadAsync(
                officialUrl, progress, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                return result;

            lastError = result.Error!.Value;
            lastErrorMessage = result.ErrorMessage;

            if (!result.IsRetryable)
                return result;
        }

        _logger.Error($"Official download failed after {_retryDelaysMs.Length + 1} attempts: {officialUrl}");
        return DownloadResult.Failure(lastError, lastErrorMessage ?? $"Failed after {_retryDelaysMs.Length + 1} attempts");
    }

    private async Task<DownloadResult> ExecuteReleaseDownloadAsync(
        string downloadUrl, long expectedSize, string expectedSha256,
        IProgress<DownloadProgress>? progress, CancellationToken callerToken)
    {
        var tempFilePath = CreateTempFilePath();
        var host = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsedUri) ? parsedUri.Host : downloadUrl;
        var attemptSw = Stopwatch.StartNew();

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            _logger.Debug($"Download attempt: {downloadUrl} (timeout={_timeoutSeconds}s)");

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                .ConfigureAwait(false);

            var headersMs = attemptSw.ElapsedMilliseconds;

            LogDownloadCorrelationHeaders(response, "Release download correlation", host);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var isRetryable = statusCode == 408 || statusCode >= 500;

                _logger.Warning($"HTTP {statusCode} from {downloadUrl}{(isRetryable ? " (retryable)" : "")}");
                _logger.Debug($"Release download timing: host={host} status={statusCode} headers_ms={headersMs} total_ms={attemptSw.ElapsedMilliseconds} error=Http");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Http, $"HTTP {statusCode}", isRetryable);
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value != expectedSize)
            {
                _logger.Warning($"Content-Length {contentLength.Value} != expected {expectedSize}");
                _logger.Debug($"Release download timing: host={host} headers_ms={headersMs} total_ms={attemptSw.ElapsedMilliseconds} error=SizeMismatch");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.SizeMismatch,
                    $"Content-Length {contentLength.Value} differs from expected {expectedSize}");
            }

            long bytesDownloaded;
            string computedSha256;

            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                    ReadBufferSize, FileOptions.Asynchronous);

                (bytesDownloaded, computedSha256) = await CopyAndHashAsync(
                    responseStream, fileStream, expectedSize, progress, attemptCts.Token).ConfigureAwait(false);
            }

            var bodyMs = attemptSw.ElapsedMilliseconds - headersMs;
            attemptSw.Stop();

            if (bytesDownloaded != expectedSize)
            {
                _logger.Warning($"Downloaded {bytesDownloaded} bytes != expected {expectedSize}");
                _logger.Debug($"Release download timing: host={host} headers_ms={headersMs} body_ms={bodyMs} total_ms={attemptSw.ElapsedMilliseconds} bytes={bytesDownloaded} error=SizeMismatch");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.SizeMismatch,
                    $"Downloaded {bytesDownloaded} bytes differs from expected {expectedSize}");
            }

            if (!string.Equals(computedSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning($"SHA-256 mismatch: computed {computedSha256} != expected {expectedSha256}");
                _logger.Debug($"Release download timing: host={host} headers_ms={headersMs} body_ms={bodyMs} total_ms={attemptSw.ElapsedMilliseconds} bytes={bytesDownloaded} error=HashMismatch");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.HashMismatch,
                    $"Computed SHA-256 {computedSha256} differs from expected {expectedSha256}");
            }

            var mibPerSec = bodyMs > 0 ? (double)bytesDownloaded / bodyMs / 1024 / 1024 * 1000 : 0;
            _logger.Debug($"Release download timing: host={host} headers_ms={headersMs} body_ms={bodyMs} total_ms={attemptSw.ElapsedMilliseconds} bytes={bytesDownloaded} mib_per_sec={mibPerSec:F1}");
            _logger.Info($"Verified download: {tempFilePath} ({bytesDownloaded} bytes, SHA-256 OK)");
            return DownloadResult.Success(tempFilePath, bytesDownloaded, computedSha256);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _logger.Info("Download cancelled by caller");
            _logger.Debug($"Release download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Cancelled");
            CleanupTempFile(tempFilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Timeout on download attempt: {downloadUrl}");
            _logger.Debug($"Release download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Timeout");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Timeout, $"Download timed out after {_timeoutSeconds}s", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            var diag = Api.NetworkDiagnostics.FormatNetworkError(ex);
            _logger.Warning($"Network error on download attempt: {diag}");
            _logger.Debug($"Release download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Network");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Network, ex.Message, isRetryable: true);
        }
        catch (IOException ex)
        {
            _logger.Warning($"IO error on download attempt: {ex.Message}");
            _logger.Debug($"Release download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Io");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Io, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Unexpected error on download attempt: {ex.Message}");
            _logger.Debug($"Release download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Unexpected");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
        }
    }

    private async Task<DownloadResult> ExecuteOfficialDownloadAsync(
        string officialUrl, IProgress<DownloadProgress>? progress, CancellationToken callerToken)
    {
        var tempFilePath = CreateTempFilePath();
        var host = Uri.TryCreate(officialUrl, UriKind.Absolute, out var parsedUri) ? parsedUri.Host : officialUrl;
        var attemptSw = Stopwatch.StartNew();

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            _logger.Debug($"Official download attempt: {officialUrl} (timeout={_timeoutSeconds}s)");

            using var request = new HttpRequestMessage(HttpMethod.Get, officialUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                .ConfigureAwait(false);

            var headersMs = attemptSw.ElapsedMilliseconds;

            LogDownloadCorrelationHeaders(response, "Official download correlation", host);

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var isRetryable = statusCode == 408 || statusCode >= 500;
                _logger.Warning($"HTTP {statusCode} from official source{(isRetryable ? " (retryable)" : "")}");
                _logger.Debug($"Official download timing: host={host} status={statusCode} headers_ms={headersMs} total_ms={attemptSw.ElapsedMilliseconds} error=Http");
                CleanupTempFile(tempFilePath);
                return DownloadResult.Failure(DownloadError.Http, $"HTTP {statusCode}", isRetryable);
            }

            var contentLength = response.Content.Headers.ContentLength;

            long bytesDownloaded;

            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None,
                    ReadBufferSize, FileOptions.Asynchronous);

                bytesDownloaded = await CopyAndReportAsync(
                    responseStream, fileStream, contentLength, progress, attemptCts.Token).ConfigureAwait(false);
            }

            var bodyMs = attemptSw.ElapsedMilliseconds - headersMs;
            attemptSw.Stop();

            var mibPerSec = bodyMs > 0 ? (double)bytesDownloaded / bodyMs / 1024 / 1024 * 1000 : 0;
            _logger.Debug($"Official download timing: host={host} headers_ms={headersMs} body_ms={bodyMs} total_ms={attemptSw.ElapsedMilliseconds} bytes={bytesDownloaded} mib_per_sec={mibPerSec:F1}");
            _logger.Info($"Official source downloaded: {tempFilePath} ({bytesDownloaded} bytes)");
            return DownloadResult.SuccessWithoutHash(tempFilePath, bytesDownloaded);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            _logger.Info("Official download cancelled by caller");
            _logger.Debug($"Official download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Cancelled");
            CleanupTempFile(tempFilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Timeout on official download attempt: {officialUrl}");
            _logger.Debug($"Official download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Timeout");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Timeout, $"Official download timed out after {_timeoutSeconds}s", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            var diag = Api.NetworkDiagnostics.FormatNetworkError(ex);
            _logger.Warning($"Network error on official download attempt: {diag}");
            _logger.Debug($"Official download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Network");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Network, ex.Message, isRetryable: true);
        }
        catch (IOException ex)
        {
            _logger.Warning($"IO error on official download attempt: {ex.Message}");
            _logger.Debug($"Official download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Io");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Io, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Unexpected error on official download attempt: {ex.Message}");
            _logger.Debug($"Official download timing: host={host} total_ms={attemptSw.ElapsedMilliseconds} error=Unexpected");
            CleanupTempFile(tempFilePath);
            return DownloadResult.Failure(DownloadError.Unexpected, ex.Message);
        }
    }

    internal static DownloadResult? ValidateReleaseMetadata(CurrentRelease release)
    {
        if (string.IsNullOrEmpty(release.DownloadUrl))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "DownloadUrl is required");

        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "DownloadUrl must be a valid HTTPS URL");
        }

        if (release.SizeBytes <= 0)
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "SizeBytes must be positive");

        if (string.IsNullOrEmpty(release.Sha256))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Sha256 is required for release downloads");

        if (string.IsNullOrEmpty(release.PublicId))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "PublicId is required");

        return null;
    }

    internal static DownloadResult? ValidateOfficialUrl(string officialUrl)
    {
        if (string.IsNullOrEmpty(officialUrl))
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Official URL is required");

        if (!Uri.TryCreate(officialUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host))
        {
            return DownloadResult.Failure(DownloadError.InvalidMetadata, "Official URL must be a valid HTTPS URL");
        }

        return null;
    }

    private string CreateTempFilePath()
    {
        var fileName = $"{Guid.NewGuid():N}.tmp";
        return Path.Combine(_appPaths.CacheDir, fileName);
    }

    private void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to cleanup temp file {path}: {ex.Message}");
        }
    }

    private void LogDownloadCorrelationHeaders(HttpResponseMessage response, string label, string host)
    {
        var parts = new List<string> { $"host={host}" };

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

        if (parts.Count > 1)
            _logger.Debug($"{label}: {string.Join(" ", parts)}");
    }

    private static async Task DelayWithCancellation(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task<(long bytesDownloaded, string sha256)> CopyAndHashAsync(
        Stream source, Stream destination, long expectedSize,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var hashAlgorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ReadBufferSize];
        long totalBytesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) break;

            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            hashAlgorithm.AppendData(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;

            progress?.Report(new DownloadProgress(totalBytesRead, expectedSize));
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        var hashBytes = hashAlgorithm.GetHashAndReset();
        var sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (totalBytesRead, sha256);
    }

    private async Task<long> CopyAndReportAsync(
        Stream source, Stream destination, long? totalBytes,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBufferSize];
        long totalBytesRead = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) break;

            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            totalBytesRead += bytesRead;

            progress?.Report(new DownloadProgress(totalBytesRead, totalBytes));
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return totalBytesRead;
    }
}

public sealed class DownloadProgress
{
    public long BytesDownloaded { get; }
    public long? TotalBytes { get; }

    public DownloadProgress(long bytesDownloaded, long? totalBytes)
    {
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }

    public double? Percentage => TotalBytes.HasValue && TotalBytes.Value > 0
        ? (double)BytesDownloaded / TotalBytes.Value * 100
        : null;
}
