namespace BdoClient.Services;

public sealed record GameDescriptor(string Id, string DisplayName);

public sealed class GameCatalog
{
    private readonly IReadOnlyList<GameDescriptor> _games;

    public GameCatalog(IEnumerable<GameDescriptor> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        var items = games.ToList();
        if (items.Count == 0)
            throw new ArgumentException("At least one game is required.", nameof(games));
        if (items.Any(game => string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.DisplayName)))
            throw new ArgumentException("Game descriptors must have an ID and display name.", nameof(games));
        if (items.Select(game => game.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new ArgumentException("Game IDs must be unique.", nameof(games));

        _games = items.AsReadOnly();
    }

    public IReadOnlyList<GameDescriptor> Games => _games;

    public GameDescriptor DefaultGame => _games[0];

    public GameDescriptor Resolve(string? id)
        => _games.FirstOrDefault(game => string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? DefaultGame;

    public static GameCatalog Create(BdoGameDefinition bdoGame)
    {
        ArgumentNullException.ThrowIfNull(bdoGame);
        return new GameCatalog(new[]
        {
            new GameDescriptor(bdoGame.Id, bdoGame.DisplayName),
            new GameDescriptor(WwmGameDefinition.Default.Id, WwmGameDefinition.Default.DisplayName)
        });
    }
}
