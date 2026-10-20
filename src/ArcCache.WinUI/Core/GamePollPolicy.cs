namespace ArcCache.Core;

public static class GamePollPolicy
{
    public static bool ShouldRender(MyGamesLoadResult previous, MyGamesLoadResult current) =>
        previous != current;
}
