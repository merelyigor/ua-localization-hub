namespace BdoClient.Services;

internal sealed class LocalizationNotificationTracker
{
    private static readonly TimeSpan UnconfirmedRetryInterval = TimeSpan.FromMinutes(2);
    private readonly HashSet<NotificationKey> _submittedReleaseKeys = new();
    private readonly Dictionary<NotificationKey, DateTimeOffset> _pendingReleaseKeys = new();

    public bool Observe(
        string gameId,
        string modeKey,
        string releaseIdentity,
        bool updateAvailable,
        bool canNotify,
        DateTimeOffset? now = null)
    {
        if (!updateAvailable
            || string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(modeKey)
            || string.IsNullOrWhiteSpace(releaseIdentity))
        {
            return false;
        }

        if (!canNotify)
            return false;

        var key = new NotificationKey(gameId, modeKey, releaseIdentity);
        if (_submittedReleaseKeys.Contains(key))
            return false;

        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (_pendingReleaseKeys.TryGetValue(key, out var attemptedAt)
            && currentTime - attemptedAt < UnconfirmedRetryInterval)
            return false;

        _pendingReleaseKeys[key] = currentTime;
        return true;
    }

    public void MarkSubmitted(string gameId, string modeKey, string releaseIdentity)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(modeKey)
            || string.IsNullOrWhiteSpace(releaseIdentity))
            return;

        var key = new NotificationKey(gameId, modeKey, releaseIdentity);
        _pendingReleaseKeys.Remove(key);
        _submittedReleaseKeys.Add(key);
    }

    public void MarkFailed(string gameId, string modeKey, string releaseIdentity)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(modeKey)
            || string.IsNullOrWhiteSpace(releaseIdentity))
            return;

        var key = new NotificationKey(gameId, modeKey, releaseIdentity);
        _pendingReleaseKeys.Remove(key);
        _submittedReleaseKeys.Remove(key);
    }

    private readonly record struct NotificationKey(string GameId, string ModeKey, string ReleaseIdentity);
}
