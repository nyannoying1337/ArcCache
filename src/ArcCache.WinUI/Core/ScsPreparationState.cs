namespace ArcCache.Core;

// One state for preparation's button and adjacent explanation:
// non-Steam / no CLI -> unavailable; busy -> wait;
// Default -> ready; invalid larger copy -> repair;
// valid larger copy without a saved load check -> launch once, no recording required;
// checked copy without CLI handoff -> unsupported; checked supported copy -> ready.
public sealed record ScsPreparationState(bool CanCompile,string Summary,string? BlockReason=null) {
    public static ScsPreparationState From(bool steam,bool hasCli,bool busy,int mib,bool copyValid,bool loadVerified,bool handoffReady,bool toolSupports) {
        if(!steam)return new(false,"SCSKiller integration currently supports Steam games only.","This game library is not supported by SCSKiller integration.");
        if(!hasCli)return new(false,"Choose scskiller.exe in Settings before preparing shaders.","Choose scskiller.exe in Settings first.");
        if(busy)return new(false,"Wait for the current step to finish.","Wait for the current step to finish.");
        if(mib<=512)return new(true,"Prepare shaders with the normal driver and Default cache size. A local recording is not required when a compatible community recording or game-file shader plan is available.");
        if(!copyValid)return new(false,"This game's driver copy needs attention. Review its setup or return it to Default before preparing shaders.","Review this game's driver-copy setup first.");
        if(!loadVerified)return new(false,"1. Launch the game with its "+SetupPresentation.Limit(mib)+" driver copy and leave it at the menu until Arc Cache confirms it loaded.\n2. Close the game, then prepare shaders.\nNo gameplay recording is needed for this check. Compatible community recordings can be used afterwards.","Launch once to verify the driver copy; no gameplay recording needed.");
        if(!toolSupports)return new(false,"The selected SCSKiller cannot prepare shaders with a driver copy. Choose a supported SCSKiller connection in Settings.","Choose a connection that supports driver copies.");
        if(!handoffReady)return new(false,"Arc Cache could not verify the driver-copy details needed for shader preparation. Review this game's setup before continuing.","Verify this game's driver-copy details first.");
        return new(true,"Ready to prepare shaders with this game's verified "+SetupPresentation.Limit(mib)+" driver copy. Close the game first. A local recording is optional when compatible community recordings or a game-file shader plan are available.");
    }
}
