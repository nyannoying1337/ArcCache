namespace ArcCache.Core;
public static class RoutingPresentation {
    public static string Status(bool enabled,bool restartRequired)=>restartRequired
        ? "Restart Windows to finish applying this setting."
        : enabled ? "On — Arc Cache can use separate graphics files for games you set up."
        : "Off — turn this on before setting up a game-specific copy.";
}
