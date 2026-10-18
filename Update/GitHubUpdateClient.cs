using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BdoClient;
using BdoClient.Logging;

namespace BdoClient.Update;

public sealed class GitHubUpdateClient
{
    private const string ApiVersion = "2022-11-28";
    private const int DiscoveryTimeoutSeconds = 15;
    private const int ManifestTimeoutSeconds = 15;
    public const int ManifestMaxBytes = 65536;
    public const long ExeMaxBytes = 200_000_000;
    public const int DownloadTimeoutSeconds = 300;
    public const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public GitHubUpdateClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<GitHubResult<List<GitHubRelease>>> FetchReleasesAsync(CancellationToken cancellationToken = default)
    {
        _logger.Debug("GitHub update: fetching releases");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var sw = Stopwatch.StartNew();

        try
        {
            var repositories = new[]
            {
                ApplicationTechnicalIdentity.CanonicalRepositoryName,
                ApplicationTechnicalIdentity.LegacyRepositoryName
            };

            for (var repositoryIndex = 0; repositoryIndex < repositories.Length; repositoryIndex++)
            {
                var repositoryName = repositories[repositoryIndex];
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    ApplicationTechnicalIdentity.BuildReleasesApiUrl(repositoryName));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.ParseAdd(ApplicationTechnicalIdentity.UserAgent);
                request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound && repositoryIndex == 0)
                {
                    _logger.Warning($"GitHub update: canonical repository {ApplicationTechnicalIdentity.CanonicalRepositorySlug} was not found; trying legacy fallback {ApplicationTechnicalIdentity.LegacyRepositorySlug}");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var error = $"GitHub HTTP {statusCode}";
                    _logger.Warning($"GitHub update: {error} from {repositoryName}");
                    return GitHubResult<List<GitHubRelease>>.Failure(error);
                }

                var content = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                List<GitHubRelease>? releases;
                try
                {
                    releases = JsonSerializer.Deserialize<List<GitHubRelease>>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.Warning($"GitHub update: JSON error from {repositoryName}: {ex.Message}");
                    return GitHubResult<List<GitHubRelease>>.Failure($"JSON error: {ex.Message}");
                }

                sw.Stop();
                _logger.Debug($"GitHub update: fetched {releases?.Count ?? 0} releases from {repositoryName} in {sw.ElapsedMilliseconds}ms");

                if (releases == null)
                    return GitHubResult<List<GitHubRelease>>.Failure("Deserialized null");

                return GitHubResult<List<GitHubRelease>>.Success(releases);
            }

            sw.Stop();
            return GitHubResult<List<GitHubRelease>>.Failure("GitHub repository discovery failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.Debug("GitHub update: cancelled (caller)");
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: timeout after {DiscoveryTimeoutSeconds}s");
            return GitHubResult<List<GitHubRelease>>.Failure("Timeout");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.Debug("GitHub update: cancelled");
            return GitHubResult<List<GitHubRelease>>.Failure("Cancelled");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: network error: {ex.Message}");
            return GitHubResult<List<GitHubRelease>>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"GitHub update: unexpected error: {ex.Message}");
            return GitHubResult<List<GitHubRelease>>.Failure($"Unexpected: {ex.Message}");
        }
    }

    public async Task<GitHubResult<UpdateManifest>> FetchManifestAsync(
        GitHubReleaseAsset manifestAsset, CancellationToken cancellationToken = default)
    {
        _logger.Debug("GitHub update: fetching manifest");

        if (manifestAsset.BrowserDownloadUrl == null ||
            !Uri.TryCreate(manifestAsset.BrowserDownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https")
        {
            return GitHubResult<UpdateManifest>.Failure("Invalid manifest URL");
        }

        if (manifestAsset.Size <= 0 || manifestAsset.Size > ManifestMaxBytes)
            return GitHubResult<UpdateManifest>.Failure($"Invalid manifest asset size: {manifestAsset.Size}");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ManifestTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var sw = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(ApplicationTechnicalIdentity.UserAgent);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return GitHubResult<UpdateManifest>.Failure($"Manifest HTTP {(int)response.StatusCode}");

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > ManifestMaxBytes)
                return GitHubResult<UpdateManifest>.Failure($"Manifest Content-Length {contentLength.Value} exceeds max");

            if (manifestAsset.Size > 0 && contentLength.HasValue && contentLength.Value != manifestAsset.Size)
                return GitHubResult<UpdateManifest>.Failure($"Content-Length {contentLength.Value} != asset size {manifestAsset.Size}");

            var buffer = new byte[ManifestMaxBytes + 1];
            int totalRead = 0;

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);

            while (totalRead < buffer.Length)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), linkedCts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            if (totalRead > ManifestMaxBytes)
                return GitHubResult<UpdateManifest>.Failure($"Manifest body {totalRead} bytes exceeds max {ManifestMaxBytes}");

            if (manifestAsset.Size > 0 && totalRead != manifestAsset.Size)
                return GitHubResult<UpdateManifest>.Failure($"Manifest size {totalRead} != asset size {manifestAsset.Size}");

            var content = Encoding.UTF8.GetString(buffer, 0, totalRead);

            UpdateManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<UpdateManifest>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                _logger.Warning($"GitHub update: manifest JSON error: {ex.Message}");
                return GitHubResult<UpdateManifest>.Failure($"JSON error: {ex.Message}");
            }

            sw.Stop();
            _logger.Debug($"GitHub update: manifest fetched in {sw.ElapsedMilliseconds}ms ({totalRead} bytes)");

            if (manifest == null)
                return GitHubResult<UpdateManifest>.Failure("Manifest deserialized to null");

            return GitHubResult<UpdateManifest>.Success(manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.Debug("GitHub update: manifest cancelled (caller)");
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: manifest timeout after {ManifestTimeoutSeconds}s");
            return GitHubResult<UpdateManifest>.Failure("Manifest timeout");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.Debug("GitHub update: manifest cancelled");
            throw;
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.Warning($"GitHub update: manifest network error: {ex.Message}");
            return GitHubResult<UpdateManifest>.Failure($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"GitHub update: manifest unexpected error: {ex.Message}");
            return GitHubResult<UpdateManifest>.Failure($"Unexpected: {ex.Message}");
        }
    }

    public async Task<GitHubResult<GitHubAssetDownloadInfo>> DownloadAssetAsync(
        string downloadUrl,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug($"GitHub update: downloading asset to {destinationPath}");

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return GitHubResult<GitHubAssetDownloadInfo>.Failure("Invalid download URL");

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.Debug($"GitHub update: retry {attempt}/{MaxRetries} after {delay.TotalSeconds}s");
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.Debug("GitHub update: retry delay cancelled (caller)");
                    throw;
                }
            }

            try
            {
                return await DownloadAssetCoreAsync(uri, destinationPath, expectedSize, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Debug("GitHub update: download cancelled (caller)");
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("GitHub update: download attempt timed out");
                if (attempt == MaxRetries)
                    return GitHubResult<GitHubAssetDownloadInfo>.Failure($"Download timed out after {MaxRetries + 1} attempts");
            }
            catch (HttpRequestException ex) when (IsRetryable(ex))
            {
                _logger.Warning($"GitHub update: retryable download error: {ex.Message}");
                if (attempt == MaxRetries)
                    return GitHubResult<GitHubAssetDownloadInfo>.Failure($"Download failed after {MaxRetries + 1} attempts: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                _logger.Warning($"GitHub update: non-retryable download error: {ex.Message}");
                return GitHubResult<GitHubAssetDownloadInfo>.Failure($"Download failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"GitHub update: download unexpected error: {ex.Message}");
                return GitHubResult<GitHubAssetDownloadInfo>.Failure($"Download failed: {ex.Message}");
            }
        }

        return GitHubResult<GitHubAssetDownloadInfo>.Failure("Download failed: exhausted retries");
    }

    private async Task<GitHubResult<GitHubAssetDownloadInfo>> DownloadAssetCoreAsync(
        Uri uri,
        string destinationPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(ApplicationTechnicalIdentity.UserAgent);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);

        long? contentLength = response.Content.Headers.ContentLength;

        if (expectedSize > 0 && contentLength.HasValue && contentLength.Value != expectedSize)
            throw new InvalidOperationException($"Content-Length {contentLength.Value} != expected {expectedSize}");

        long maxBytes = expectedSize > 0 ? expectedSize : ExeMaxBytes;

        var sw = Stopwatch.StartNew();
        long totalBytes = 0;
        var buffer = new byte[81920];

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var contentStream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
                throw new InvalidOperationException($"Download exceeded max size ({maxBytes} bytes)");

            sha256.AppendData(buffer, 0, bytesRead);
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCts.Token).ConfigureAwait(false);

            if (progress != null && expectedSize > 0)
                progress.Report((double)totalBytes / expectedSize * 100);
        }

        await fileStream.FlushAsync(linkedCts.Token).ConfigureAwait(false);

        var hashBytes = sha256.GetHashAndReset();
        var sha256Hex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        sw.Stop();
        _logger.Debug($"GitHub update: downloaded {totalBytes} bytes in {sw.ElapsedMilliseconds}ms, SHA-256={sha256Hex[..16]}...");

        if (expectedSize > 0 && totalBytes != expectedSize)
            throw new InvalidOperationException($"Downloaded {totalBytes} bytes != expected {expectedSize}");

        return GitHubResult<GitHubAssetDownloadInfo>.Success(new GitHubAssetDownloadInfo(totalBytes, sha256Hex));
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        // GitHub's download edge may temporarily return 404 for a newly
        // published release asset. Retry a bounded 404 so a transient
        // propagation window does not require the user to restart staging
        // manually; a genuinely missing asset still fails closed after the
        // normal retry budget.
        if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            return true;
        if (ex.StatusCode is >= HttpStatusCode.InternalServerError)
            return true;
        if (ex.StatusCode is null)
            return true;
        return false;
    }
}

public readonly struct GitHubAssetDownloadInfo
{
    public long BytesDownloaded { get; }
    public string Sha256 { get; }

    public GitHubAssetDownloadInfo(long bytesDownloaded, string sha256)
    {
        BytesDownloaded = bytesDownloaded;
        Sha256 = sha256;
    }
}
