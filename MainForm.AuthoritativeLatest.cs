using BdoClient.Api;
using BdoClient.Models;
using BdoClient.Storage;

namespace BdoClient;

public partial class MainForm
{
    private static readonly TimeSpan AuthoritativeLatestRefreshInterval = TimeSpan.FromMinutes(3);
    private readonly Dictionary<string, AuthoritativeLatestEntry> _authoritativeLatestBySlug = new(StringComparer.Ordinal);

    private async Task<bool> EnsureAuthoritativeLatestAsync(
        string slug,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var aggregateCurrent = FindAggregateCurrent(slug);
        _authoritativeLatestBySlug.TryGetValue(slug, out var cached);
        var aggregateConflicts = cached?.Succeeded == true
            && !string.Equals(cached.AggregatePublicId, aggregateCurrent?.PublicId, StringComparison.Ordinal);
        var isDue = cached == null
            || DateTimeOffset.UtcNow - cached.CheckedAt >= AuthoritativeLatestRefreshInterval;

        if (!forceRefresh && !isDue && !aggregateConflicts)
            return cached!.Succeeded;

        var latest = await _apiClient.GetLatestReleaseAsync(slug, cancellationToken).ConfigureAwait(true);
        var outcome = latest.Outcome.ToString();
        if (latest.Outcome == LatestReleaseOutcome.NotModified
            && !SameReleaseIdentity(latest.Data?.Current, aggregateCurrent))
        {
            _logger.Warning(
                $"BDO latest/aggregate identity conflict after 304: mode={slug}, " +
                $"latest={FormatIdentity(latest.Data?.Current)}, aggregate={FormatIdentity(aggregateCurrent)}. " +
                "Performing unconditional no-cache revalidation.");
            latest = await _apiClient.GetLatestReleaseAsync(
                slug,
                cancellationToken,
                forceUnconditionalRevalidation: true).ConfigureAwait(true);
            outcome = $"{outcome}->unconditional-{latest.Outcome}";
        }

        if (latest.Outcome is (LatestReleaseOutcome.Modified or LatestReleaseOutcome.NotModified)
            && latest.Data != null)
        {
            cached = new AuthoritativeLatestEntry(
                latest.Data,
                outcome,
                DateTimeOffset.UtcNow,
                true,
                latest.ETag,
                aggregateCurrent?.PublicId);
            _authoritativeLatestBySlug[slug] = cached;
            if (!SameReleaseIdentity(latest.Data.Current, aggregateCurrent))
            {
                _logger.Warning(
                    $"BDO latest/aggregate identity disagreement: mode={slug}, " +
                    $"latest_outcome={outcome}, latest={FormatIdentity(latest.Data.Current)}, " +
                    $"aggregate={FormatIdentity(aggregateCurrent)}, etag={latest.ETag ?? "none"}.");
            }
            return true;
        }

        _authoritativeLatestBySlug[slug] = new AuthoritativeLatestEntry(
            null,
            outcome,
            DateTimeOffset.UtcNow,
            false,
            latest.ETag,
            aggregateCurrent?.PublicId);
        _logger.Warning(
            $"BDO authoritative latest unavailable: mode={slug}, outcome={outcome}, " +
            $"error={latest.ErrorKind}, detail={latest.ErrorMessage}. Aggregate metadata will not be used as fallback.");
        return false;
    }

    private async Task<bool> RefreshRelevantAuthoritativeLatestAsync(
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var installed = _stateStore.Load();
        var installedSlug = installed.Status == FileLoadStatus.Valid
            && installed.Value?.Source == InstallationSource.Api
            ? installed.Value.ModeSlug
            : null;
        var slugs = new[] { installedSlug, GetSelectedModeSlug() }
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var changed = false;
        foreach (var slug in slugs)
        {
            _authoritativeLatestBySlug.TryGetValue(slug!, out var previous);
            await EnsureAuthoritativeLatestAsync(slug!, forceRefresh, cancellationToken)
                .ConfigureAwait(true);
            changed |= _authoritativeLatestBySlug.TryGetValue(slug!, out var current)
                && current.CheckedAt != previous?.CheckedAt;
        }

        return changed;
    }

    private CurrentRelease? ResolveCurrentRelease(string? slug, CurrentRelease? aggregateCurrent)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return aggregateCurrent;

        if (!_authoritativeLatestBySlug.TryGetValue(slug, out var latest))
            return aggregateCurrent;

        return latest.Succeeded ? latest.Data?.Current : null;
    }

    private LocalizationMode? WithAuthoritativeCurrent(LocalizationMode? mode)
    {
        if (mode?.Slug == null)
            return mode;

        var current = ResolveCurrentRelease(mode.Slug, mode.Current);
        if (ReferenceEquals(current, mode.Current))
            return mode;

        return new LocalizationMode
        {
            Slug = mode.Slug,
            PublicName = mode.PublicName,
            Description = mode.Description,
            Audience = mode.Audience,
            Current = current,
            History = mode.History
        };
    }

    private CurrentRelease? FindAggregateCurrent(string slug) => _apiResponse?.Data?.Modes?
        .FirstOrDefault(mode => string.Equals(mode.Slug, slug, StringComparison.Ordinal))?.Current;

    private static bool SameReleaseIdentity(CurrentRelease? left, CurrentRelease? right) =>
        string.Equals(left?.PublicId, right?.PublicId, StringComparison.Ordinal);

    private static string FormatIdentity(CurrentRelease? current) => current == null
        ? "none"
        : $"public_id={current.PublicId ?? "missing"},version={current.Version},patch={current.Patch}";

    private sealed record AuthoritativeLatestEntry(
        LatestReleaseData? Data,
        string Outcome,
        DateTimeOffset CheckedAt,
        bool Succeeded,
        string? ETag,
        string? AggregatePublicId);
}
