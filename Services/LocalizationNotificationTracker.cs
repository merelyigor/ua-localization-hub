namespace BdoClient.Services;

internal sealed class LocalizationNotificationTracker
{
    private readonly HashSet<NotificationKey> _observedReleaseKeys = new();

    public bool Observe(
        string gameId,
        string modeKey,
        string releaseIdentity,
        bool updateAvailable,
        bool canNotify)
    {
        if (!updateAvailable
            || string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(modeKey)
            || string.IsNullOrWhiteSpace(releaseIdentity))
        {
            return false;
        }

        var isFirstObservation = _observedReleaseKeys.Add(new NotificationKey(gameId, modeKey, releaseIdentity));
        return isFirstObservation && canNotify;
    }

    private readonly record struct NotificationKey(string GameId, string ModeKey, string ReleaseIdentity);
}
