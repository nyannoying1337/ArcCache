namespace ArcCache.Core;

public enum MyGamesLoadResult { Unknown, Checking, DriverCopyLoaded, NormalDriverLoaded, Failed }
public enum MyGamesAction { None, AddGame, ChooseGame, ChooseAnotherGame, OpenGraphics, OpenGameFolder, UpdateRecorder, LaunchForDiscovery, SetUp, Launch, LaunchAgain, ApplySize, RestoreGame, ReviewSetup, ReviewSavedSetup }

public sealed record LocalModBlocker(string Path, bool IsDirectory = false, string? ProductName = null, string? FileDescription = null, string? CompanyName = null) {
    public string DisplayName { get { string value=string.Join(" · ",new[]{ProductName,FileDescription,CompanyName}.Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));return value.Length>0?value:IsDirectory?"an existing .local folder":"an existing mod"; } }
}

public sealed record MyGamesPageInputs(
    bool GraphicsReady = true,
    bool HasGames = true,
    bool HasSelection = true,
    bool GameInstalled = true,
    bool ProfileFound = true,
    bool Dx12Unavailable = false,
    bool SetupExists = false,
    bool SetupValid = true,
    bool SetupRunning = false,
    bool ControlVerified = false,
    bool SizeApplyRunning = false,
    bool AppliedDefault = true,
    bool ChosenDefault = true,
    MyGamesLoadResult LoadResult = MyGamesLoadResult.Unknown,
    string LaunchLabel = "Launch through Steam",
    string CacheSize = "1 GB",
    string AvailableSizes = "Default, 1 GB",
    bool AppliedChoiceMatches = true,
    bool TrialNeedsRetest = false,
    string? DiscoveryMessage = null,
    bool SizeVerified = false,
    LocalModBlocker? ModBlocker = null,
    bool RecorderUpdate = false,
    bool RequiresTrialProfile = false,
    bool TrialProfileFound = true,
    AntiCheatSighting? AntiCheatSeen = null,
    AntiCheatHint? AntiCheatFiles = null,
    DateTime? AntiCheatOverride = null);

public sealed record MyGamesPageState(
    string Key, string HeaderStatus, string ListBadge, bool ShowGameFlow, bool ShowEmptyLibrary,
    bool ShowNoSelection, bool ShowGraphicsGate, string Step1Result, string Step1Body, bool Step1Complete,
    string Step2Result, string Step2Body, bool Step2Complete, string Step3Result, string Step3Body,
    bool Step3Complete, int ExpandedStep, bool ShowSizeChoices, bool ShowSizeApply, bool ShowSetupAction,
    bool ShowStep2Launch, bool ShowStep3Launch, bool ShowProgress, bool ShowStop, string ProgressText,
    string AccentButton, MyGamesAction AccentAction, string? Warning);

public static class MyGamesPageStates {
    public static MyGamesLoadResult EffectiveLoadResult(MyGamesLoadResult liveResult, bool configured, int? receiptMiB, string? receiptStatus) {
        if (liveResult != MyGamesLoadResult.Unknown || !configured || receiptMiB is null) return liveResult;
        bool verified = receiptMiB.Value <= 512
            ? receiptStatus == "original-control-loading-verified"
            : receiptStatus == "size-loading-verified";
        return verified ? MyGamesLoadResult.DriverCopyLoaded : liveResult;
    }

    static MyGamesPageState Make(MyGamesPageInputs i, string key, string header, string badge,
        bool flow, bool empty, bool noSelection, bool gate,
        string s1, string b1, bool c1, string s2, string b2, bool c2, string s3, string b3, bool c3,
        int expanded, bool sizes, bool setup, bool launch2, bool launch3, bool progress, bool stop,
        string progressText, MyGamesAction action, string? warning = null) =>
        new(key, header, badge, flow, empty, noSelection, gate, s1, b1, c1, s2, b2, c2, s3, b3, c3,
            expanded, sizes, action == MyGamesAction.ApplySize, setup, launch2, launch3, progress, stop,
            progressText, Button(i, action), action, warning);

    static string Button(MyGamesPageInputs i, MyGamesAction action) => action switch {
        MyGamesAction.AddGame => "Add a game",
        MyGamesAction.ChooseGame => "Choose a game",
        MyGamesAction.ChooseAnotherGame => "Choose another game",
        MyGamesAction.OpenGraphics => "Open Graphics",
        MyGamesAction.LaunchForDiscovery or MyGamesAction.Launch => i.LaunchLabel,
        MyGamesAction.LaunchAgain => "Launch again",
        MyGamesAction.SetUp => "Set up this game",
        MyGamesAction.ApplySize => "Apply " + i.CacheSize,
        MyGamesAction.RestoreGame => "Use default settings",
        MyGamesAction.ReviewSetup => "Review setup",
        MyGamesAction.ReviewSavedSetup => "Review saved setup",
        MyGamesAction.OpenGameFolder => "Open game folder",
        MyGamesAction.UpdateRecorder => "Use Arc Cache's recorder",
        _ => string.Empty
    };

    public static MyGamesPageState Resolve(MyGamesPageInputs i) {
        if (!i.HasGames)
            return Make(i,"empty-library","No games yet","",false,true,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.AddGame);
        if (!i.HasSelection)
            return Make(i,"no-selection","Choose a game","",false,false,true,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.None);
        if (!i.GameInstalled)
            return Make(i,"game-uninstalled","Game not found","Not installed",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.ReviewSavedSetup,"The game is no longer installed. Its saved setup can still be reviewed or removed.");
        // SCSKiller's recorder from a build that can't run beside a driver copy, when the selected SCSKiller has one that can.
        if (i.ModBlocker is { } old && i.RecorderUpdate)
            return Make(i,"blocked-by-old-recorder","Blocked by an older recorder","Blocked",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.UpdateRecorder,$"Found {old.DisplayName}. This version can't run beside a driver copy: the game wouldn't start. Arc Cache's SCSKiller has one that can.\nFile: {old.Path}");
        if (i.ModBlocker is { } blocker)
            return Make(i,"blocked-by-another-mod","Blocked by another mod","Blocked",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.OpenGameFolder,$"Found {blocker.DisplayName}. A mod such as OptiScaler or ReShade also hooks into DirectX, and combining both can crash the game.\nFile: {blocker.Path}\nRemove or disable the mod, then check again.");
        // An anti-cheat that runs next to the game sees the changed driver file, whatever mode the game is in.
        // The user can still choose to go ahead (AntiCheatOverride) after a full warning; then the normal flow continues.
        if (i.AntiCheatSeen is null && i.AntiCheatFiles is { AlwaysOn: true } files && i.AntiCheatOverride is null)
            return Make(i,"anticheat-files","This game uses an anti-cheat","Anti-cheat",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",i.SetupExists?MyGamesAction.RestoreGame:MyGamesAction.None,
                $"{files.Name} runs whenever this game starts. It would see the changed Intel driver file, even in single-player, and could block the game or get your account banned. Arc Cache doesn't set this game up unless you accept that risk.");
        if (i.AntiCheatSeen is { } seen && i.AntiCheatOverride is null)
            return i.SetupExists
                ? Make(i,"anticheat-with-copy","Anti-cheat ran with the driver copy","Anti-cheat",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.RestoreGame,
                    $"{seen.Name} was running while this game ran with its driver copy ({seen.When}). The anti-cheat can see the changed Intel driver file and may block the game or flag your account. Choose Use default settings. Set it up again only if the game can start without {seen.Name}.")
                : Make(i,"anticheat-running","Anti-cheat runs with this game","Anti-cheat",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.None,
                    $"{seen.Name} was running while this game ran ({seen.When}). It would see the changed driver file, even in single-player, and could block the game or flag your account, so Arc Cache doesn't set this game up.\nIf the game can start without it (like Halo MCC's \"anti-cheat disabled\" mode), start it that way and leave it running for 30 seconds; Arc Cache checks automatically. If another game with anti-cheat was open at the same time, close that one first.");
        if (i.SetupExists && !i.ProfileFound)
            return Make(i,"setup-needs-attention","Setup needs attention","Needs attention",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.RestoreGame,
                "This game's saved driver copy doesn't match the current game files (for example after a game update). Use default settings first, then start the game once so Arc Cache can find it again.");
        if (i.Dx12Unavailable)
            return Make(i,"dx12-unavailable","DirectX 12 unavailable","No DirectX 12",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.None,"This game does not provide a usable DirectX 12 launch path, so it cannot use a game-specific driver copy.");
        if (i.SetupExists && !i.SetupValid)
            return Make(i,"problem-recovery","Needs attention","Needs attention",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.ReviewSetup,"The saved setup record does not match the game files. Review or undo it before setting this game up again.");
        if (i.SetupExists && i.TrialNeedsRetest)
            return Make(i,"size-retest-required","Retest this size","Retest needed",false,false,false,false,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.OpenGraphics,"The saved "+i.CacheSize+" test is from an older app version. Run a new Graphics test for this size, or use default settings under Advanced.");
        if (!i.GraphicsReady)
            return Make(i,"graphics-not-ready","Finish Graphics setup first","",false,false,false,true,"","",false,"","",false,"","",false,0,false,false,false,false,false,false,"",MyGamesAction.OpenGraphics);
        if (i.SetupRunning)
            return Make(i,"setup-running","Setting up","",true,false,false,false,"Setting up…","Creating the unchanged Default-size driver copy.",false,"Waiting for step 1","",false,"Waiting for step 2","",false,1,false,false,false,false,true,false,"Step 1 of 3 · Creating the Default driver copy",MyGamesAction.None);
        if (i.RequiresTrialProfile && !i.TrialProfileFound && !i.SetupExists)
            return Make(i,"trial-profile-not-found","Game check needed","",true,false,false,false,"Game check needed","Start the game once in DirectX 12 with the normal Intel driver so Arc Cache can identify its files. Close the game before setup.",false,"Waiting for step 1","",false,"Waiting for step 2","",false,1,false,false,false,false,false,false,"",MyGamesAction.LaunchForDiscovery);
        if (!i.ProfileFound && !i.SetupExists)
            return Make(i,"profile-not-found","Game not detected yet","",true,false,false,false,"Game not detected yet","Start the game once in DirectX 12 so Arc Cache can find its files.",false,"Waiting for step 1","",false,"Waiting for step 2","",false,1,false,false,false,false,false,false,"",MyGamesAction.LaunchForDiscovery);
        if (!i.SetupExists)
            return Make(i,"not-set-up","Not set up","",true,false,false,false,"Ready to set up","This creates an unchanged Default-size driver copy. You can choose a larger size after it loads once.",false,"Waiting for step 1","",false,"Waiting for step 2","",false,1,false,false,true,false,false,false,"",MyGamesAction.SetUp);

        if (!i.ControlVerified && i.AppliedDefault) {
            if (i.LoadResult == MyGamesLoadResult.Checking)
                return Make(i,"first-check-running","Set up · checking launch","Set up",true,false,false,false,"Default driver copy created","",true,"Checking what loaded…","Keep the game open while Arc Cache checks its driver copy.",false,"Waiting for step 2","",false,2,false,false,false,false,true,true,"Step 2 of 3 · Checking the loaded driver",MyGamesAction.None);
            if (i.LoadResult == MyGamesLoadResult.NormalDriverLoaded)
                return NormalDriver(i, true);
            if (i.LoadResult == MyGamesLoadResult.Failed)
                return Make(i,"problem-check-failed","Needs attention","Needs attention",true,false,false,false,"Default driver copy created","",true,"Check could not finish","Close the game and try the check again.",false,"Waiting for step 2","",false,2,false,false,false,false,false,false,"",MyGamesAction.LaunchAgain,"Arc Cache could not confirm which driver loaded.");
            return Make(i,"first-launch-waiting","Set up · launch to check","Set up",true,false,false,false,"Default driver copy created","",true,"Launch and check","Start in DirectX 12. Arc Cache checks the loaded driver while the game is open.",false,"Waiting for step 2","",false,2,false,false,false,true,false,false,"",MyGamesAction.Launch);
        }
        if (i.SizeApplyRunning)
            return Make(i,"size-apply-running","Applying cache size","Set up",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,"Applying " + i.CacheSize + "…","",false,3,false,false,false,true,false,true,"Step 3 of 3 · Applying " + i.CacheSize,MyGamesAction.None);
        if (i.AppliedDefault) {
            if (i.LoadResult == MyGamesLoadResult.NormalDriverLoaded) return NormalDriver(i, false);
            if (i.LoadResult == MyGamesLoadResult.Checking)
                return Make(i,"first-check-running","Set up · checking launch","Set up",true,false,false,false,"Default driver copy created","",true,"Checking what loaded…","Keep the game open while Arc Cache checks its driver copy.",false,"Waiting for step 2","",false,2,false,false,false,false,true,true,"Step 2 of 3 · Checking the loaded driver",MyGamesAction.None);
            if (i.LoadResult == MyGamesLoadResult.Failed)
                return Make(i,"problem-check-failed","Needs attention","Needs attention",true,false,false,false,"Default driver copy created","",true,"Check could not finish","Close the game and try the check again.",false,"Waiting for step 2","",false,2,false,false,false,false,false,false,"",MyGamesAction.LaunchAgain,"Arc Cache could not confirm which driver loaded.");
            if (i.ChosenDefault && i.LoadResult == MyGamesLoadResult.DriverCopyLoaded)
                return Make(i,"default-verified","Ready to play · Default","Ready",true,false,false,false,"Default driver copy created","",true,"Driver copy loaded","Arc Cache saw this game's private driver path and matching file.",true,"Default cache size","The unchanged Default-size copy is ready for this game.",true,3,true,false,false,false,false,false,"",MyGamesAction.Launch);
            if (!i.ChosenDefault)
                return Make(i,"size-apply-pending","Set up · choose a cache size","Set up",true,false,false,false,"Default driver copy created","",true,"Driver copy loaded","",true,"Choose a cache size","Only sizes supported by this driver are shown.",false,3,true,false,false,false,false,false,"",MyGamesAction.ApplySize);
            return Make(i,"default-verified","Ready to play · Default","Ready",true,false,false,false,"Default driver copy created","",true,"Driver copy loaded","",true,"Default cache size","The unchanged Default-size copy is ready for this game.",true,3,true,false,false,false,false,false,"",MyGamesAction.Launch);
        }
        if (!i.AppliedChoiceMatches)
            return Make(i,i.AppliedDefault?"size-apply-pending":"size-change-pending","Set up · choose a cache size","Set up",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,"Choose a cache size","Only sizes supported by this driver are shown.",false,3,true,false,false,false,false,false,"",MyGamesAction.ApplySize);
        if (i.LoadResult == MyGamesLoadResult.NormalDriverLoaded) return NormalDriver(i, false);
        if (i.LoadResult == MyGamesLoadResult.Checking)
            return Make(i,"size-check-running","Checking cache size","Set up",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,"Checking what loaded…","Keep the game open while Arc Cache checks this cache size.",false,3,true,false,false,false,true,true,"Step 3 of 3 · Checking the loaded driver",MyGamesAction.None);
        if (i.LoadResult == MyGamesLoadResult.Failed)
            return Make(i,"problem-check-failed","Needs attention","Needs attention",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,"Check could not finish","Close the game and launch again to check this size.",false,3,true,false,false,false,false,false,"",MyGamesAction.LaunchAgain,"Arc Cache could not confirm which driver loaded.");
        if (i.LoadResult == MyGamesLoadResult.DriverCopyLoaded)
            return Make(i,i.SizeVerified?"size-verified-after-close":"size-verified","Ready to play · " + i.CacheSize,"Ready",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,i.CacheSize + " driver copy loaded","Arc Cache saw this game's private driver path and matching file.",true,3,true,false,false,false,false,false,"",MyGamesAction.Launch);
        return Make(i,"size-launch-waiting",i.CacheSize + " set · launch to check","Set up",true,false,false,false,"Default driver copy created","",true,"First launch checked","",true,"Launch once more to confirm","Start in DirectX 12 so Arc Cache can check this cache size.",false,3,true,false,false,true,false,false,"",MyGamesAction.Launch);
    }

    // A size can only change while the game is closed (MatchedSet.Change checks this).
    // While it runs, hide the size picker and Apply instead of offering a change that must fail.
    public static MyGamesPageState WhileGameRunning(MyGamesPageState s) =>
        !s.ShowSizeChoices && s.AccentAction != MyGamesAction.ApplySize ? s :
        s with { ShowSizeChoices = false, ShowSizeApply = false, ShowStep3Launch = false, AccentAction = MyGamesAction.None, AccentButton = string.Empty,
                 Warning = "The game is running. Close it to change the cache size." };

    static MyGamesPageState NormalDriver(MyGamesPageInputs i, bool firstCheck) => Make(i,firstCheck?"problem-normal-driver":"problem-size-normal-driver","Needs attention","Needs attention",true,false,false,false,
        "Default driver copy created","",true,firstCheck?"! Normal driver loaded":"First launch checked",firstCheck?"The game used the normal Intel driver, not its driver copy. Make sure the game starts in DirectX 12, then launch again.":"",!firstCheck,
        firstCheck?"Waiting for step 2":"! Normal driver loaded",firstCheck?"":"The game used the normal driver instead of this game's driver copy. Make sure the game starts in DirectX 12, then launch again.",false,
        firstCheck?2:3,false,false,false,false,false,false,"",MyGamesAction.LaunchAgain,"Make sure the game starts in DirectX 12, then launch again.");

    public static IReadOnlyList<(string Key,string Name,MyGamesPageInputs Input,MyGamesAction Accent)> StateTable { get; } = [
        ("empty-library","empty library",new(HasGames:false,HasSelection:false),MyGamesAction.AddGame),
        ("no-selection","no game selected",new(HasSelection:false),MyGamesAction.None),
        ("graphics-not-ready","Graphics setup not ready",new(GraphicsReady:false),MyGamesAction.OpenGraphics),
        ("profile-not-found","game profile not found",new(ProfileFound:false),MyGamesAction.LaunchForDiscovery),
        ("trial-profile-not-found","game not observed with the tested driver copy",new(RequiresTrialProfile:true,TrialProfileFound:false,ProfileFound:true),MyGamesAction.LaunchForDiscovery),
        ("blocked-by-another-mod","blocked by local d3d12.dll",new(ModBlocker:new LocalModBlocker(@"D:\SteamLibrary\steamapps\common\Palworld\Pal\Binaries\Win64\d3d12.dll",ProductName:"OptiScaler",FileDescription:"OptiScaler DirectX proxy",CompanyName:"OptiScaler")),MyGamesAction.OpenGameFolder),
        ("anticheat-running","anti-cheat seen running before setup",new(AntiCheatSeen:new("Easy Anti-Cheat",new DateTime(2026,10,17,9,50,0,DateTimeKind.Utc),false)),MyGamesAction.None),
        ("anticheat-files","always-on anti-cheat files found",new(AntiCheatFiles:new(AntiCheatLevel.AlwaysOn,"Riot Vanguard")),MyGamesAction.None),
        ("anticheat-with-copy","anti-cheat seen running with the driver copy",new(SetupExists:true,ControlVerified:true,AntiCheatSeen:new("BattlEye",new DateTime(2026,10,17,9,50,0,DateTimeKind.Utc),true)),MyGamesAction.RestoreGame),
        ("dx12-unavailable","DirectX 12 unavailable",new(Dx12Unavailable:true),MyGamesAction.None),
        ("not-set-up","not set up",new(),MyGamesAction.SetUp),
        ("setup-running","setup running",new(SetupRunning:true),MyGamesAction.None),
        ("first-launch-waiting","first launch waiting",new(SetupExists:true,ControlVerified:false,AppliedDefault:true),MyGamesAction.Launch),
        ("first-check-running","first launch check running",new(SetupExists:true,ControlVerified:false,AppliedDefault:true,LoadResult:MyGamesLoadResult.Checking),MyGamesAction.None),
        ("problem-normal-driver","normal driver loaded",new(SetupExists:true,ControlVerified:false,AppliedDefault:true,LoadResult:MyGamesLoadResult.NormalDriverLoaded),MyGamesAction.LaunchAgain),
        ("default-verified","Default size verified",new(SetupExists:true,ControlVerified:true,AppliedDefault:true,ChosenDefault:true,LoadResult:MyGamesLoadResult.DriverCopyLoaded,CacheSize:"Default"),MyGamesAction.Launch),
        ("size-apply-pending","selected size ready to apply",new(SetupExists:true,ControlVerified:true,AppliedDefault:true,ChosenDefault:false,CacheSize:"1 GB",AppliedChoiceMatches:false),MyGamesAction.ApplySize),
        ("size-change-pending","change from the currently applied size",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,AppliedChoiceMatches:false,CacheSize:"2 GB",AvailableSizes:"Default, 1 GB, 2 GB"),MyGamesAction.ApplySize),
        ("size-apply-running","size apply running",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,SizeApplyRunning:true,CacheSize:"1 GB"),MyGamesAction.None),
        ("size-launch-waiting","selected size waiting for launch",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,CacheSize:"1 GB"),MyGamesAction.Launch),
        ("size-check-running","selected size check running",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,LoadResult:MyGamesLoadResult.Checking,CacheSize:"1 GB"),MyGamesAction.None),
        ("size-verified","selected size verified",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,LoadResult:MyGamesLoadResult.DriverCopyLoaded,CacheSize:"1 GB"),MyGamesAction.Launch),
        ("size-verified-after-close","verified size remains ready after game closes or app restarts",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,SizeVerified:true,LoadResult:MyGamesLoadResult.DriverCopyLoaded,CacheSize:"16 GB"),MyGamesAction.Launch),
        ("problem-size-normal-driver","normal driver loaded after size change",new(SetupExists:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,LoadResult:MyGamesLoadResult.NormalDriverLoaded,CacheSize:"1 GB"),MyGamesAction.LaunchAgain),
        ("problem-recovery","setup needs recovery",new(SetupExists:true,SetupValid:false),MyGamesAction.ReviewSetup),
        ("setup-needs-attention","saved setup but game profile no longer matches (Ready or Not)",new(ProfileFound:false,SetupExists:true,SetupValid:true,ControlVerified:true,AppliedDefault:false,ChosenDefault:false,CacheSize:"16 GB",AvailableSizes:"Default, 1 GB, 2 GB, 4 GB, 8 GB, 16 GB",DiscoveryMessage:"Game executable changed. Return its private set to stock before setting this game up again."),MyGamesAction.RestoreGame),
        ("size-retest-required","older large-size test needs rechecking while Graphics is pending",new(GraphicsReady:false,SetupExists:true,TrialNeedsRetest:true,CacheSize:"16 GB"),MyGamesAction.OpenGraphics),
        ("game-uninstalled","game uninstalled",new(GameInstalled:false,SetupExists:true),MyGamesAction.ReviewSavedSetup)
    ];
}
