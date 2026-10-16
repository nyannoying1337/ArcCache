namespace ArcCache.Core;

public static class GamePollPolicy
{
    public static bool ShouldStop(string message) =>
        message.Contains("not running", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldRender(MyGamesLoadResult previous, MyGamesLoadResult current) =>
        previous != current;
}
