using System.Net;
using BdoClient.Logging;
using BdoClient.Services;
using BdoClient.Storage;
using BdoClient.Update;

namespace BdoClient.Tests.Services;

public sealed class SelectedGameSessionHostTests
{
    [Fact]
    public async Task CommitCandidate_DisposesPreviousOnlyAfterCallerDrainsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-session-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var logger = new TestLogger();
            var version = AppVersionInfo.FromRawVersion("1.2.7");
            using var initialClient = new HttpClient(new StubHandler());
            using var candidateClient = new HttpClient(new StubHandler());
            var initial = BdoGameSession.CreateForTests(paths, logger, version, initialClient);
            var candidate = BdoGameSession.CreateForTests(
                paths, logger, version, candidateClient, descriptor: new GameDescriptor("synthetic-game", "Synthetic Game"));
            using var host = new SelectedGameSessionHost(
                initial,
                _ => candidate);

            initial.ReleaseFeedPoller.Start(null);
            var previous = host.CommitCandidate(candidate);
            await previous.StopAsync();
            Assert.Same(initial, previous);
            Assert.Same(candidate, host.CurrentSession);
            Assert.False(initial.ReleaseFeedPoller.IsRunning);

            previous.Dispose();
            Assert.True(initial.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchingSyntheticGameAndBack_DrainsEachPreviousSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-session-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var logger = new TestLogger();
            var version = AppVersionInfo.FromRawVersion("1.2.7");
            using var initialClient = new HttpClient(new StubHandler());
            using var secondGameClient = new HttpClient(new StubHandler());
            using var returnClient = new HttpClient(new StubHandler());
            var initial = BdoGameSession.CreateForTests(paths, logger, version, initialClient);
            var secondGame = BdoGameSession.CreateForTests(
                paths, logger, version, secondGameClient,
                descriptor: new GameDescriptor("synthetic-game", "Synthetic Game"));
            var returnToBdo = BdoGameSession.CreateForTests(paths, logger, version, returnClient);
            using var host = new SelectedGameSessionHost(
                initial,
                descriptor => descriptor.Id == "synthetic-game" ? secondGame : returnToBdo);

            initial.ReleaseFeedPoller.Start(null);
            var previousBdo = host.CommitCandidate(secondGame);
            await previousBdo.StopAsync();
            previousBdo.Dispose();
            Assert.Same(secondGame, host.CurrentSession);
            Assert.False(((BdoGameSession)previousBdo).ReleaseFeedPoller.IsRunning);
            Assert.True(((BdoGameSession)previousBdo).IsDisposed);

            secondGame.ReleaseFeedPoller.Start(null);
            var previousSynthetic = host.CommitCandidate(returnToBdo);
            await previousSynthetic.StopAsync();
            previousSynthetic.Dispose();
            Assert.Same(returnToBdo, host.CurrentSession);
            Assert.False(((BdoGameSession)previousSynthetic).ReleaseFeedPoller.IsRunning);
            Assert.True(((BdoGameSession)previousSynthetic).IsDisposed);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchingBdoToWwmAndBack_DrainsAndDisposesGameSpecificSessions()
    {
        var root = Path.Combine(Path.GetTempPath(), "bdo-session-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var logger = new TestLogger();
            using var bdoClient = new HttpClient(new StubHandler());
            using var wwmClient = new HttpClient(new StubHandler());
            using var returnedBdoClient = new HttpClient(new StubHandler());
            var bdo = BdoGameSession.CreateForTests(paths, logger, AppVersionInfo.FromRawVersion("1.2.9"), bdoClient);
            var wwm = WwmGameSession.CreateForTests(paths, logger, wwmClient);
            var returnedBdo = BdoGameSession.CreateForTests(paths, logger, AppVersionInfo.FromRawVersion("1.2.9"), returnedBdoClient);
            using var host = new SelectedGameSessionHost(bdo, descriptor => descriptor.Id switch
            {
                "where-winds-meet" => wwm,
                "black-desert-online" => returnedBdo,
                _ => throw new InvalidOperationException("Unexpected game descriptor.")
            });

            wwm.Poller.Start(null);
            var priorBdo = host.CommitCandidate(host.CreateCandidate(new GameDescriptor("where-winds-meet", "Where Winds Meet")));
            await priorBdo.StopAsync();
            priorBdo.Dispose();
            Assert.Same(wwm, host.CurrentSession);
            Assert.True(((BdoGameSession)priorBdo).IsDisposed);

            var priorWwm = host.CommitCandidate(host.CreateCandidate(new GameDescriptor("black-desert-online", "Black Desert Online")));
            await priorWwm.StopAsync();
            priorWwm.Dispose();
            Assert.Same(returnedBdo, host.CurrentSession);
            Assert.True(((WwmGameSession)priorWwm).IsDisposed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class TestLogger : ILogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}
