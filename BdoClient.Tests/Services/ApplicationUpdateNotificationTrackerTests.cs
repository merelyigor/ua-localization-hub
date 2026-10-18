using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class ApplicationUpdateNotificationTrackerTests
{
    [Fact]
    public void NullCandidate_DoesNotNotify()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.False(tracker.Observe(null, canNotify: true));
    }

    [Fact]
    public void FirstHiddenTag_Notifies()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
    }

    [Fact]
    public void RepeatedSameHiddenTag_DoesNotNotify()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
        Assert.False(tracker.Observe("v1.2.1", canNotify: true));
    }

    [Fact]
    public void FirstVisibleTag_RemainsPendingAndNotifiesWhenHiddenLater()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.False(tracker.Observe("v1.2.1", canNotify: false));
        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
        Assert.False(tracker.Observe("v1.2.1", canNotify: true));
    }

    [Fact]
    public void RepeatedVisibleChecks_KeepCandidatePendingUntilTrayNotificationCanShow()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.False(tracker.Observe("v1.2.1", canNotify: false));
        Assert.False(tracker.Observe("v1.2.1", canNotify: false));
        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
    }

    [Fact]
    public void DifferentNewerHiddenTag_Notifies()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
        Assert.True(tracker.Observe("v1.2.2", canNotify: true));
    }

    [Fact]
    public void NullReset_AllowsSameTagToNotifyAgain()
    {
        var tracker = new ApplicationUpdateNotificationTracker();

        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
        Assert.False(tracker.Observe(null, canNotify: true));
        Assert.True(tracker.Observe("v1.2.1", canNotify: true));
    }
}
