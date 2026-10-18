using System.Net;
using System.Text;
using BdoClient.Logging;
using BdoClient.Update;

namespace BdoClient.Tests.Update;

public class GitHubUpdateClientTests
{
    [Fact]
    public async Task FetchReleasesAsync_ValidList_ReturnsSuccess()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[]}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("v0.1.3", result.Value![0].TagName);
    }

    [Fact]
    public async Task FetchReleasesAsync_ExtraFields_Tolerated()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":"2026-01-01T00:00:00Z","assets":[],"node_id":"abc","url":"https://example.com","extra_field":42}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task FetchReleasesAsync_NullPublishedAt_Tolerated()
    {
        var json = """[{"tag_name":"v0.1.3","draft":false,"prerelease":false,"published_at":null,"assets":[]}]""";
        var client = CreateClient(json);
        var result = await client.FetchReleasesAsync();
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value![0].PublishedAt);
    }

    [Fact]
    public async Task FetchReleasesAsync_HttpError_ReturnsFailure()
    {
        var client = CreateClient("", HttpStatusCode.Forbidden);
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("403", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_MalformedJson_ReturnsFailure()
    {
        var client = CreateClient("not json");
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_Cancellation_Throws()
    {
        var client = CreateClient("[]");
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchReleasesAsync(cts.Token));
    }

    [Fact]
    public async Task FetchReleasesAsync_NetworkError_ReturnsFailure()
    {
        var handler = new NetworkErrorHttpMessageHandler();
        var client = CreateClientWithHandler(handler);
        var result = await client.FetchReleasesAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("Network", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchReleasesAsync_CorrectUrl_PerPage()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        Assert.Single(handler.Requests);
        var url = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("api.github.com", url);
        Assert.Contains("/releases", url);
        Assert.Contains("per_page=100", url);
    }

    [Fact]
    public async Task FetchReleasesAsync_CorrectHeaders()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        var req = handler.Requests[0];
        Assert.False(req.Headers.Contains("Authorization"));
        Assert.Contains("application/vnd.github+json", string.Join(",", req.Headers.Accept.Select(a => a.ToString())));
        Assert.True(req.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task FetchReleasesAsync_ExactlyOneGetRequest()
    {
        var handler = new RecordingHttpMessageHandler("[]");
        var client = CreateClientWithHandler(handler);
        await client.FetchReleasesAsync();
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    // --- Manifest tests ---

    [Fact]
    public async Task FetchManifestAsync_ValidManifest_ReturnsSuccess()
    {
        var manifestJson = """{"schema_version":1,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client.exe","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":"12345"}""";
        var handler = new QueuedHandler(manifestJson);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(manifestJson.Length);
        var result = await client.FetchManifestAsync(asset);
        Assert.True(result.IsSuccess);
        Assert.Equal("0.1.4", result.Value!.Version);
    }

    [Fact]
    public async Task FetchManifestAsync_WorkflowRunIdString_Deserializes()
    {
        var manifestJson = """{"schema_version":1,"version":"0.1.4","tag":"v0.1.4","commit_sha":"74875dfcc6762ec0edb75c40e225150f94fa45e5","asset_name":"BDO-UA-Client.exe","sha256":"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2","platform":"win-x64","workflow_run_id":"32211040254"}""";
        var handler = new QueuedHandler(manifestJson);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(manifestJson.Length);
        var result = await client.FetchManifestAsync(asset);
        Assert.True(result.IsSuccess);
        Assert.Equal("32211040254", result.Value!.WorkflowRunId);
    }

    [Fact]
    public async Task FetchManifestAsync_AssetSizeExceedsMax_RejectsBeforeRead()
    {
        var handler = new QueuedHandler("[]");
        var client = CreateClientWithHandler(handler);
        var asset = new GitHubReleaseAsset
        {
            Name = "release-manifest.json",
            BrowserDownloadUrl = "https://github.com/test/manifest",
            Size = GitHubUpdateClient.ManifestMaxBytes + 1,
            State = "uploaded"
        };
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("size", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchManifestAsync_ContentLengthExceedsMax_Rejects()
    {
        var body = Encoding.UTF8.GetBytes("x");
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
        handler.SetContentLengthOverride(body.Length + 65536);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(body.Length);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("Content-Length", result.ErrorMessage!);
    }

    [Fact]
    public async Task FetchManifestAsync_ContentLengthNotEqualAssetSize_Rejects()
    {
        var body = Encoding.UTF8.GetBytes("x");
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
        handler.SetContentLengthOverride(999);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(999);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("asset size", result.ErrorMessage!);
    }

    [Fact]
    public async Task FetchManifestAsync_BodyExactMax_Accepted()
    {
        var body = new byte[GitHubUpdateClient.ManifestMaxBytes];
        Random.Shared.NextBytes(body);
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(body.Length);
        asset.State = "uploaded";
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.ErrorMessage!);
    }

    [Fact]
    public async Task FetchManifestAsync_BodyMaxPlusOne_Rejected()
    {
        var handler = new SlowStreamHandler(GitHubUpdateClient.ManifestMaxBytes + 1);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(GitHubUpdateClient.ManifestMaxBytes);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds max", result.ErrorMessage!);
    }

    [Fact]
    public async Task FetchManifestAsync_ActualBytesNotEqualAssetSize_Rejects()
    {
        var handler = new MismatchedContentHandler(100, GitHubUpdateClient.ManifestMaxBytes + 100);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(100);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task FetchManifestAsync_CallerCancellation_Propagates()
    {
        var handler = new BlockingHandler();
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(100);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchManifestAsync(asset, cts.Token));
    }

    [Fact]
    public async Task FetchManifestAsync_InternalTimeout_ReturnsFailure()
    {
        var handler = new TimeoutSimulatingHandler(TimeSpan.FromSeconds(16));
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(100);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("timeout", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchManifestAsync_HttpError_ReturnsFailure()
    {
        var handler = new QueuedHandler(HttpStatusCode.Forbidden);
        var client = CreateClientWithHandler(handler);
        var asset = ManifestAsset(100);
        var result = await client.FetchManifestAsync(asset);
        Assert.False(result.IsSuccess);
        Assert.Contains("403", result.ErrorMessage!);
    }

    // --- Direct EXE download tests ---

    [Fact]
    public async Task DownloadAssetAsync_Success_ReturnsByteCount()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var handler = new QueuedHandler(content);
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length, null);
            Assert.True(result.IsSuccess);
            Assert.Equal(content.Length, result.Value!.BytesDownloaded);
            Assert.Equal(64, result.Value.Sha256.Length);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_SHA_MatchesActualBytes()
    {
        var content = Encoding.UTF8.GetBytes("test data for sha");
        var handler = new QueuedHandler(content);
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length, null);
            Assert.True(result.IsSuccess);
            var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
            Assert.Equal(expectedSha, result.Value!.Sha256);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http408_Retries()
    {
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.RequestTimeout));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.RequestTimeout));
        var content = Encoding.UTF8.GetBytes("ok");
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length, null);
            Assert.True(result.IsSuccess);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http500_Retries()
    {
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var content = Encoding.UTF8.GetBytes("ok");
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length, null);
            Assert.True(result.IsSuccess);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http503_Retries()
    {
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var content = Encoding.UTF8.GetBytes("ok");
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length, null);
            Assert.True(result.IsSuccess);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_StatuslessHttpRequestException_Retries()
    {
        var handler = new NetworkErrorHttpResponse();
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        var dest2 = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 100, null);
            Assert.False(result.IsSuccess);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http404ThenAvailable_RetriesAndSucceeds()
    {
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok"))
        });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 2, null);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal("ok", await File.ReadAllTextAsync(dest));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http404_RemainsFailureAfterRetryBudget()
    {
        var handler = new QueuedHandler();
        for (var attempt = 0; attempt <= GitHubUpdateClient.MaxRetries; attempt++)
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 100, null);
            Assert.False(result.IsSuccess);
            Assert.Contains("404", result.ErrorMessage!);
            Assert.Equal(GitHubUpdateClient.MaxRetries + 1, handler.RequestCount);
            Assert.False(File.Exists(dest));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_Http403_NoRetry()
    {
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 100, null);
            Assert.False(result.IsSuccess);
            Assert.Contains("403", result.ErrorMessage!);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_CallerCancellation_NoRetry()
    {
        var handler = new BlockingHttpResponse();
        var client = CreateClientWithHandler(handler);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 100, null, cts.Token));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_MaxAttempts_1InitialPlus3Retries()
    {
        var handler = new QueuedHandler();
        for (int i = 0; i <= GitHubUpdateClient.MaxRetries; i++)
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 100, null);
            Assert.False(result.IsSuccess);
            Assert.Contains("attempts", result.ErrorMessage!);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_ContentLengthMismatch_Throws()
    {
        var content = Encoding.UTF8.GetBytes("actual");
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, 999, null);
            Assert.False(result.IsSuccess);
            Assert.Contains("Content-Length", result.ErrorMessage!);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadAssetAsync_ActualBytesMismatch_Throws()
    {
        var content = Encoding.UTF8.GetBytes("actual");
        var handler = new QueuedHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var client = CreateClientWithHandler(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"bdo-test-{Guid.NewGuid():N}.bin");
        try
        {
            var result = await client.DownloadAssetAsync("https://github.com/test/BDO-UA-Client.exe", dest, content.Length + 1, null);
            Assert.False(result.IsSuccess);
            Assert.Contains("Content-Length", result.ErrorMessage!);
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    // --- Helpers ---

    private static GitHubUpdateClient CreateClient(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new QueuedHandler();
        handler.Enqueue(statusCode == HttpStatusCode.OK
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(statusCode));
        return new GitHubUpdateClient(new HttpClient(handler), new NullLogger());
    }

    private static GitHubUpdateClient CreateClientWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new GitHubUpdateClient(httpClient, new NullLogger());
    }

    private static GitHubReleaseAsset ManifestAsset(int size) => new()
    {
        Name = "release-manifest.json",
        BrowserDownloadUrl = "https://github.com/test/manifest",
        Size = size,
        State = "uploaded"
    };

    // --- Handler infrastructure ---

    private class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        private long? _contentLengthOverride;

        public int RequestCount { get; private set; }

        public QueuedHandler(params object[] responses)
        {
            foreach (var r in responses)
            {
                if (r is byte[] bytes)
                    _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                else if (r is HttpStatusCode status)
                    _responses.Enqueue(new HttpResponseMessage(status));
                else if (r is HttpResponseMessage msg)
                    _responses.Enqueue(msg);
                else
                {
                    var str = r?.ToString() ?? "";
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(strBytes) });
                }
            }
        }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        public void SetContentLengthOverride(long value) => _contentLengthOverride = value;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var response = _responses.Dequeue();
            if (_contentLengthOverride.HasValue && response.Content != null)
                response.Content.Headers.ContentLength = _contentLengthOverride.Value;
            else if (response.Content != null && !response.Content.Headers.ContentLength.HasValue)
            {
                var bytes = response.Content.ReadAsByteArrayAsync().Result;
                response.Content.Headers.ContentLength = bytes.Length;
            }

            return Task.FromResult(response);
        }
    }

    private class SlowStreamHandler : HttpMessageHandler
    {
        private readonly int _size;
        public SlowStreamHandler(int size) => _size = size;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new byte[_size];
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            response.Content.Headers.ContentLength = _size;
            return Task.FromResult(response);
        }
    }

    private class MismatchedContentHandler : HttpMessageHandler
    {
        private readonly int _declaredSize;
        private readonly int _actualSize;

        public MismatchedContentHandler(int declaredSize, int actualSize)
        {
            _declaredSize = declaredSize;
            _actualSize = actualSize;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new byte[_actualSize];
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
            response.Content.Headers.ContentLength = _declaredSize;
            return Task.FromResult(response);
        }
    }

    private class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private class TimeoutSimulatingHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public TimeoutSimulatingHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(_delay, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private class BlockingHttpResponse : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private class NetworkErrorHttpResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private class NetworkErrorHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHttpMessageHandler(string response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }

    private class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
