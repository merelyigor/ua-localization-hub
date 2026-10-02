using System.Net;
using System.Text;
using BdoClient.Api;
using BdoClient.Logging;

namespace BdoClient.Tests.Api;

public class BdoUaApiClientLatestTests
{
    private const string Slug = "full-ukrainian";
    private const string CurrentJson = """
        "current": {
          "public_id": "01ABCDEF0123456789ABCDEFGH",
          "version": 7,
          "filename": "languagedata_en.loc",
          "download_url": "https://bdo-ua.com.ua/download/releases/01ABCDEF0123456789ABCDEFGH",
          "size_bytes": 1234,
          "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "patch": 395,
          "compatible_with_official_patch": true
        }
        """;

    [Fact]
    public async Task GetLatestReleaseAsync_ValidResponse_UsesCorrectGetAndParsesFieldsAndCurrentRelease()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, LatestJson(Slug))));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Modified, result.Outcome);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://bdo-ua.com.ua/api/public/v1/releases/latest/full-ukrainian", handler.Requests[0].Uri);
        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.Equal(395, result.Data!.OfficialPatch);
        Assert.Equal("languagedata_en.loc", result.Data.Filename);
        Assert.Equal("https://bdo-ua.com.ua/download", result.Data.InstallGuideUrl);
        Assert.Equal(Slug, result.Data.Mode!.Slug);
        Assert.Equal("Повна українська", result.Data.Mode.PublicName);
        Assert.Equal("01ABCDEF0123456789ABCDEFGH", result.Data.Current!.PublicId);
        Assert.Equal(7, result.Data.Current.Version);
        Assert.Equal(1234, result.Data.Current.SizeBytes);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_CurrentNull_IsValidMetadata()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, LatestJson(Slug, includeCurrent: false))));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Modified, result.Outcome);
        Assert.Null(result.Data!.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../releases")]
    [InlineData("https://example.test/release")]
    [InlineData("full/ukrainian")]
    [InlineData("full?x=1")]
    [InlineData("full-ukrainian-")]
    public async Task GetLatestReleaseAsync_InvalidSlug_FailsWithoutHttp(string? slug)
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called."));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(slug!);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ReturnedModeSlugMismatch_FailsClosed()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, LatestJson("other-mode"))));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
        Assert.Contains("other-mode", result.ErrorMessage);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_UsesCachedEtagAnd304ReturnsCachedMetadataWithoutParsingBody()
    {
        var requestCount = 0;
        var handler = new RecordingHandler((_, _) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = JsonResponse(HttpStatusCode.OK, LatestJson(Slug));
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"feed-v1\"");
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Content = new StringContent("this is deliberately not JSON")
            });
        });
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var initial = await client.GetLatestReleaseAsync(Slug);
        var unchanged = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Modified, initial.Outcome);
        Assert.Equal("\"feed-v1\"", initial.ETag);
        Assert.Equal(LatestReleaseOutcome.NotModified, unchanged.Outcome);
        Assert.Equal(initial.Data!.Current!.PublicId, unchanged.Data!.Current!.PublicId);
        Assert.Equal("\"feed-v1\"", unchanged.ETag);
        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.Equal("\"feed-v1\"", Assert.Single(handler.Requests[1].IfNoneMatch));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_EtagCacheIsPerSlug()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            var slug = request.RequestUri!.Segments[^1];
            var modeSlug = Uri.UnescapeDataString(slug.TrimEnd('/'));
            var response = JsonResponse(HttpStatusCode.OK, LatestJson(modeSlug));
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{modeSlug}\"");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        await client.GetLatestReleaseAsync("mode-a");
        await client.GetLatestReleaseAsync("mode-b");

        Assert.Empty(handler.Requests[0].IfNoneMatch);
        Assert.Empty(handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ResponseWithoutEtag_IsUsableAndNotConditionallyCached()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, LatestJson(Slug))));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);
        await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Modified, result.Outcome);
        Assert.Null(result.ETag);
        Assert.Empty(handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_MalformedEtag_DoesNotInvalidateSuccessfulResponse()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.OK, LatestJson(Slug));
            response.Headers.TryAddWithoutValidation("ETag", "not-an-entity-tag");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var first = await client.GetLatestReleaseAsync(Slug);
        await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Modified, first.Outcome);
        Assert.Null(first.ETag);
        Assert.Empty(handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_304WithoutCachedMetadata_FailsClosedWithoutBodyParsing()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)
        {
            Content = new StringContent("not JSON")
        }));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_404_CapturesDocumentedAllowedSlugsAndDoesNotRetry()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.NotFound,
            """{"success":false,"error":"unknown_mode","allowed":["full-ukrainian","english-items"]}""")));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync("unknown-mode");

        Assert.Equal(LatestReleaseOutcome.UnknownMode, result.Outcome);
        Assert.Equal(ApiErrorKind.Http, result.ErrorKind);
        Assert.Equal(new[] { "full-ukrainian", "english-items" }, result.AllowedSlugs);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_404WithoutDocumentedErrorShape_KeepsUnknownModeWithoutAllowedValues()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.NotFound, "{}")));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync("unknown-mode");

        Assert.Equal(LatestReleaseOutcome.UnknownMode, result.Outcome);
        Assert.Null(result.AllowedSlugs);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_503_ParsesRetryAfterAndDoesNotRetry()
    {
        var response = JsonResponse(HttpStatusCode.ServiceUnavailable,
            """{"success":false,"error":"official_patch_unconfirmed","message":"Patch not confirmed."}""");
        response.Headers.TryAddWithoutValidation("Retry-After", "300");
        var handler = new RecordingHandler((_, _) => Task.FromResult(response));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.PatchUnconfirmed, result.Outcome);
        Assert.Equal(ApiErrorKind.Http, result.ErrorKind);
        Assert.Equal(TimeSpan.FromSeconds(300), result.RetryAfter);
        Assert.Equal("Patch not confirmed.", result.ErrorMessage);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-seconds")]
    [InlineData("-1")]
    public async Task GetLatestReleaseAsync_503MissingOrMalformedRetryAfterDoesNotInventDelay(string? retryAfter)
    {
        var response = JsonResponse(HttpStatusCode.ServiceUnavailable,
            """{"success":false,"error":"official_patch_unconfirmed","message":"Patch not confirmed."}""");
        if (retryAfter != null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        var handler = new RecordingHandler((_, _) => Task.FromResult(response));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.PatchUnconfirmed, result.Outcome);
        Assert.Null(result.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    public async Task GetLatestReleaseAsync_Invalid200Body_ReturnsInvalidResponse(string body)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.InvalidResponse, result.ErrorKind);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_Timeout_ReturnsTimeoutWithoutRetry()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, LatestJson(Slug));
        });
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger(), timeoutSeconds: 0);

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.Timeout, result.ErrorKind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_CallerCancellation_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new RecordingHandler((_, cancellationToken) => Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug, cancellation.Token);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.Cancelled, result.ErrorKind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NetworkFailure_ReturnsNetwork()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("Connection refused."));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(ApiErrorKind.Network, result.ErrorKind);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_UnexpectedException_ReturnsUnexpected()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected handler failure."));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.Unexpected, result.ErrorKind);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_UnrecognizedServerError_ReturnsGenericHttpFailure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "server error")));
        using var httpClient = new HttpClient(handler);
        var client = new BdoUaApiClient(httpClient, new NullLogger());

        var result = await client.GetLatestReleaseAsync(Slug);

        Assert.Equal(LatestReleaseOutcome.Failure, result.Outcome);
        Assert.Equal(ApiErrorKind.Http, result.ErrorKind);
        Assert.Contains("500", result.ErrorMessage);
        Assert.Single(handler.Requests);
    }

    private static string LatestJson(string slug, bool includeCurrent = true) =>
        $$"""
        {
          "success": true,
          "generated_at": "2026-10-02T09:00:00+03:00",
          "data": {
            "official_patch": 395,
            "filename": "languagedata_en.loc",
            "install_guide_url": "https://bdo-ua.com.ua/download",
            "mode": { "slug": "{{slug}}", "public_name": "Повна українська" },
            {{(includeCurrent ? CurrentJson : "\"current\": null")}}
          }
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.IfNoneMatch.Select(value => value.ToString()).ToArray()));
            return send(request, cancellationToken);
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Uri, IReadOnlyList<string> IfNoneMatch);

    private sealed class NullLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
