using BdoClient.Api;
using BdoClient.Models;

namespace BdoClient.Services;

public sealed class WwmReleaseFeedPoller : IDisposable
{
    private readonly Winds4UaApiClient _client;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private TimeSpan _interval = TimeSpan.FromSeconds(15);
    private bool _paused;
    private bool _disposed;
    public WwmReleaseFeed? Feed { get; private set; }
    public event Action<WwmReleaseFeed>? FeedUpdated;

    public WwmReleaseFeedPoller(Winds4UaApiClient client) => _client = client;
    public void Start(WwmReleaseFeed? feed)
    {
        if (_disposed || _loop != null) return;
        Feed = feed;
        _loop = RunAsync(_stop.Token);
    }
    public void SetVisible(bool visible) => _interval = visible ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(5);
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
    public void Stop() => _stop.Cancel();
    public async Task RequestImmediatePollAsync(CancellationToken token = default)
    {
        var result = await _client.GetLatestAsync(token).ConfigureAwait(false);
        if (result.IsSuccess && result.Feed != null)
        {
            Feed = result.Feed;
            FeedUpdated?.Invoke(result.Feed);
        }
    }
    public async Task StopAsync()
    {
        Stop();
        if (_loop != null) try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _loop = null;
    }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(_interval, token).ConfigureAwait(false);
            if (_paused) continue;
            var result = await _client.GetLatestAsync(token).ConfigureAwait(false);
            if (!result.IsSuccess || result.Feed == null) continue;
            Feed = result.Feed;
            FeedUpdated?.Invoke(result.Feed);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
    }
}
