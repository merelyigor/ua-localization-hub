namespace BdoClient.Services;

internal sealed class ApplicationUpdateNotificationTracker
{
    private static readonly TimeSpan UnconfirmedRetryInterval = TimeSpan.FromMinutes(2);
    private string? _submittedTag;
    private string? _pendingTag;
    private DateTimeOffset _pendingAt;

    public bool Observe(string? candidateTag, bool canNotify, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(candidateTag))
        {
            _submittedTag = null;
            _pendingTag = null;
            return false;
        }

        if (string.Equals(_submittedTag, candidateTag, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!canNotify)
            return false;

        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (string.Equals(_pendingTag, candidateTag, StringComparison.OrdinalIgnoreCase)
            && currentTime - _pendingAt < UnconfirmedRetryInterval)
            return false;

        _pendingTag = candidateTag;
        _pendingAt = currentTime;
        return true;
    }

    public void MarkSubmitted(string? candidateTag)
    {
        if (!string.IsNullOrWhiteSpace(candidateTag)
            && string.Equals(_pendingTag, candidateTag, StringComparison.OrdinalIgnoreCase))
        {
            _submittedTag = candidateTag;
            _pendingTag = null;
        }
    }

    public void MarkFailed(string? candidateTag)
    {
        if (string.Equals(_pendingTag, candidateTag, StringComparison.OrdinalIgnoreCase))
            _pendingTag = null;
        if (string.Equals(_submittedTag, candidateTag, StringComparison.OrdinalIgnoreCase))
            _submittedTag = null;
    }
}
