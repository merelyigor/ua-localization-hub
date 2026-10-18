using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class LocalizationNotificationTrackerTests
{
    [Fact]
    public void UpToDate_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.False(tracker.Observe("bdo", "full", "release-a", updateAvailable: false, canNotify: true));
    }

    [Fact]
    public void FirstHiddenUpdateAvailable_Notifies()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void FirstVisibleUpdateAvailable_DoesNotNotifyButLatches()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: false));
        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void RepeatedHiddenUpdateAvailable_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void VisibleThenHiddenUpdateAvailable_DoesNotNotify()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: false);

        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void NonUpdateObservationDoesNotConsumeOrResetReleaseIdentity()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true);
        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: false, canNotify: true));

        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void DifferentReleaseForSameMode_NotifiesAgain()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true);

        Assert.True(tracker.Observe("bdo", "full", "release-c", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void PreviouslyUnavailableMode_NewReleaseNotifies()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-a", updateAvailable: false, canNotify: true);

        Assert.True(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void NewModeReleaseIsEligibleForNotification()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-b", updateAvailable: false, canNotify: true);

        Assert.True(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void LaterReleaseNotifiesEvenWhileEarlierReleaseRemainsUnresolved()
    {
        var tracker = new LocalizationNotificationTracker();

        tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true);

        Assert.True(tracker.Observe("bdo", "full", "release-c", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void SameReleaseIsDeduplicatedPerGameAndMode()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
        Assert.False(tracker.Observe("bdo", "full", "release-b", updateAvailable: true, canNotify: true));
        Assert.True(tracker.Observe("bdo", "items", "release-b", updateAvailable: true, canNotify: true));
        Assert.True(tracker.Observe("wwm", "ukrainian/default", "release-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void NewReleaseForSameInstalledModeNotifiesAgain()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.True(tracker.Observe("wwm", "ukrainian/default", "sha-a", updateAvailable: true, canNotify: true));
        Assert.True(tracker.Observe("wwm", "ukrainian/default", "sha-b", updateAvailable: true, canNotify: true));
    }

    [Fact]
    public void MissingIdentityDoesNotNotifyOrConsumeAnotherRelease()
    {
        var tracker = new LocalizationNotificationTracker();

        Assert.False(tracker.Observe("wwm", "ukrainian/default", "", updateAvailable: true, canNotify: true));
        Assert.True(tracker.Observe("wwm", "ukrainian/default", "sha-a", updateAvailable: true, canNotify: true));
    }
}
