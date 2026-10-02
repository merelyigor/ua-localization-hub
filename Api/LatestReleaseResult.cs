using BdoClient.Models;

namespace BdoClient.Api;

public enum LatestReleaseOutcome
{
    Modified,
    NotModified,
    UnknownMode,
    PatchUnconfirmed,
    Failure
}

public sealed class LatestReleaseResult
{
    private LatestReleaseResult(
        LatestReleaseOutcome outcome,
        LatestReleaseData? data = null,
        string? etag = null,
        IReadOnlyList<string>? allowedSlugs = null,
        TimeSpan? retryAfter = null,
        ApiErrorKind? errorKind = null,
        string? errorMessage = null)
    {
        Outcome = outcome;
        Data = data;
        ETag = etag;
        AllowedSlugs = allowedSlugs;
        RetryAfter = retryAfter;
        ErrorKind = errorKind;
        ErrorMessage = errorMessage;
    }

    public LatestReleaseOutcome Outcome { get; }
    public LatestReleaseData? Data { get; }
    public string? ETag { get; }
    public IReadOnlyList<string>? AllowedSlugs { get; }
    public TimeSpan? RetryAfter { get; }
    public ApiErrorKind? ErrorKind { get; }
    public string? ErrorMessage { get; }

    public static LatestReleaseResult Modified(LatestReleaseData data, string? etag) =>
        new(LatestReleaseOutcome.Modified, data, etag);

    public static LatestReleaseResult NotModified(LatestReleaseData cachedData, string? etag) =>
        new(LatestReleaseOutcome.NotModified, cachedData, etag);

    public static LatestReleaseResult UnknownMode(
        IReadOnlyList<string>? allowedSlugs,
        ApiErrorKind errorKind,
        string message) =>
        new(LatestReleaseOutcome.UnknownMode, allowedSlugs: allowedSlugs, errorKind: errorKind, errorMessage: message);

    public static LatestReleaseResult PatchUnconfirmed(
        TimeSpan? retryAfter,
        ApiErrorKind errorKind,
        string message) =>
        new(LatestReleaseOutcome.PatchUnconfirmed, retryAfter: retryAfter, errorKind: errorKind, errorMessage: message);

    public static LatestReleaseResult Failure(ApiErrorKind errorKind, string message) =>
        new(LatestReleaseOutcome.Failure, errorKind: errorKind, errorMessage: message);
}
