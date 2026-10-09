namespace BdoClient.Services;

/// <summary>Common selected-session ownership only; game services remain game-specific.</summary>
public interface IGameSession : IDisposable
{
    GameDescriptor Descriptor { get; }
    Task StopAsync();
}
