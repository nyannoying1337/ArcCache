namespace ArcCache.Core;
public static class RoutingPresentation {
    public static string Status(bool enabled,bool restartRequired)=>restartRequired
        ? "Restart required — restart Windows before using custom game settings."
        : enabled ? "On — games can use their own driver copies."
        : "Off — enable game-specific settings and restart Windows to use custom cache limits.";
}
