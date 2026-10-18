namespace BdoClient.Services;

internal sealed class ApplicationUpdateNotificationTracker
{
    private string? _activeTag;

    public bool Observe(string? candidateTag, bool canNotify)
    {
        if (string.IsNullOrWhiteSpace(candidateTag))
        {
            _activeTag = null;
            return false;
        }

        if (string.Equals(_activeTag, candidateTag, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!canNotify)
            return false;

        _activeTag = candidateTag;
        return true;
    }
}
