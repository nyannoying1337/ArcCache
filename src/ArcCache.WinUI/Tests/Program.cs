using ArcCache.Core;
using System.Text.Json;

int passed=0;
void Check(bool yes,string message){if(!yes)throw new Exception(message);}
void Test(string name,Action body){body();passed++;Console.WriteLine("PASS "+name);}
void Reject(Action body){try{body();}catch(InvalidDataException){return;}throw new Exception("Expected rejection.");}
Check(GraphicsPageState.StateTable.Select(r=>r.Key).Distinct(StringComparer.Ordinal).Count()==GraphicsPageState.StateTable.Count,"Graphics preview keys must be unique.");
foreach(var row in GraphicsPageState.StateTable) Test("Graphics state table: "+row.Name,()=>{
 var state=GraphicsPageState.Resolve(row.Input);
 Check(state.CurrentStep==row.Step,$"Expected step {row.Step}, got {state.CurrentStep}.");
 Check(state.PrimaryAction==row.Action,$"Expected accent action {row.Action}, got {state.PrimaryAction}.");
 Check(state.AccentButton==row.AccentButton,$"Expected accent button '{row.AccentButton}', got '{state.AccentButton}'.");
 Check(state.ShowAmberDriverNote==row.Amber,$"Amber-note state mismatch for {row.Name}.");
 Check(state.ShowFinish==row.Ready,$"Finish state mismatch for {row.Name}.");
 Check(state.ReadyForGames==(state.Step2Complete&&(row.Input.Driver==GraphicsDriverState.Checked||row.Input.ControlCopyPassed)),"My games readiness must follow the 512 control, Windows setting and the reviewed-driver status.");
 Check(new[]{state.Step1Expanded,state.Step2Expanded,state.Step3Expanded}.Count(x=>x)<=1,"More than one setup step is expanded.");
 Check(state.ShowFinish==(state.PrimaryAction==GraphicsPrimaryAction.ChooseGame),"Finish button and finish state disagree.");
 if(row.Input.Check is GraphicsCheckState.NotRun or GraphicsCheckState.Running)Check(state.HomeTitle=="Checking your graphics…","Home must stay in the checking state until the graphics check finishes.");
 if(row.Action==GraphicsPrimaryAction.ChooseGame)Check(state.AccentButton=="Choose a game"&&state.FinishText=="Ready. Choose a game.","The finish heading and next-action button must use different wording.");
 if(row.Input.Check==GraphicsCheckState.Failed)Check(!state.ShowCheckAgainLink&&state.AccentButton!="","A failed check must offer an accent retry, not a check-again link.");
 if(row.Input.Check==GraphicsCheckState.Passed&&row.Input.Driver!=GraphicsDriverState.Changed&&row.Input.Windows!=GraphicsWindowsState.Error&&!row.Input.MultipleChips)Check(state.ShowCheckAgainLink,"A completed check must offer only a small Check again link.");
 if(row.Key=="fresh-check")Check(state.Step1Result=="Checking graphics chip…"&&state.Step1Body==""&&state.Step2Result=="Waiting for step 1"&&state.Step3Result=="Waiting for step 1","The fresh check must show only its spinner/status and keep later steps waiting.");
 if(row.Key=="no-intel-chip")Check(state.Step1Result=="No Intel graphics found"&&state.Step1Body.Contains("Intel Arc or Xe")&&state.ShowCheckAgainLink&&state.Step2Result=="Waiting for step 1"&&state.Step3Result=="Waiting for step 1","No-Intel state must explain the requirement and keep later steps waiting.");
 if(row.Key is "windows-setting-off" or "restart-pending" or "windows-state-unreadable" or "setting-restored")Check(!state.Step3Complete&&state.Step3Result=="Waiting for step 2","Step 3 cannot appear complete while step 2 is unfinished.");
 if(row.Key=="setting-restored")Check(state.Step2Body.StartsWith("Previous Windows setting restored.",StringComparison.Ordinal),"Restored Windows setting must be identified in step 2.");
 if(row.Key=="test-running")Check(state.ShowStopTest&&state.CopyTestProgress==42&&state.CopyTestStage.Contains("temporary driver copy"),"Running test must expose stage, progress, and stop action.");
 if(row.Key is "test-failed" or "test-stopped")Check(state.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy&&state.AccentButton=="Test again","A failed or stopped test must offer a retry.");
 if(row.Key=="unsupported-file")Check(state.ShowUpdateDriverLink&&state.Step3Body.Contains("Update the Intel graphics driver"),"Unsupported driver state must explain a recovery path.");
 if(row.Key=="multiple-chips")Check(state.Step1Expanded&&state.ShowChipPicker&&state.PrimaryAction==GraphicsPrimaryAction.CheckSelectedChip&&state.Step2Result=="Waiting for step 1","Multiple-chip state must ask which chip to check.");
 if(row.Name.StartsWith("B580",StringComparison.Ordinal)){Check(state.Step3Complete&&state.ShowAmberDriverNote&&state.Warning=="Not pre-reviewed by Arc Cache · passed its test on this PC","Passed unchecked-driver state lost its amber note.");Check(state.CacheSizes=="Default, 1 GB"&&state.Step3Result.Contains("Default, 1 GB"),"Cache sizes do not match this driver's capability list.");}
 if(row.Name=="long graphics names")Check(state.Step1Result.Contains("unusually long adapter name")&&state.Step1Result.Contains("32.0.101.9150.12345"),"Long graphics identity was dropped from the state model.");
});
foreach(var row in GraphicsPageState.ReadinessStateTable)Test("Graphics/My games readiness: "+row.Key,()=>{
 var state=GraphicsPageState.Resolve(row.Input);
 Check(state.ReadyForGames==row.ReadyForGames,$"Unexpected My games readiness for {row.Key} with selected test size {row.Input.SelectedTrialMiB} MiB.");
 if(row.Key=="selected-size-passed-control-not-passed")Check(!state.ShowFinish&&state.Step3Result=="Default driver copy needs its test"&&state.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy,"A larger-size pass without the unchanged control must not show a contradictory ready state.");
});
Test("Automatic game polling does not flash Checking or rerender unchanged results",()=>{
 Check(!GamePollPolicy.ShouldRender(MyGamesLoadResult.Unknown,MyGamesLoadResult.Unknown),"Repeated background observations must not rerender My games.");
 Check(GamePollPolicy.ShouldRender(MyGamesLoadResult.Unknown,MyGamesLoadResult.DriverCopyLoaded),"A changed observation must update the page.");
});
GameLibraryTests.Run(Test,Check);
Check(MyGamesPageStates.StateTable.Select(r=>r.Key).Distinct(StringComparer.Ordinal).Count()==MyGamesPageStates.StateTable.Count,"My games preview keys must be unique.");
foreach(var row in MyGamesPageStates.StateTable)Test("My games state table: "+row.Name,()=>{
 var state=MyGamesPageStates.Resolve(row.Input);
 Check(state.Key==row.Key,$"Expected state {row.Key}, got {state.Key}.");
 Check(state.AccentAction==row.Accent,$"Expected accent action {row.Accent}, got {state.AccentAction}.");
 Check(state.ExpandedStep is >=0 and <=3,"Expanded step is outside the three-step flow.");
 Check(state.ExpandedStep==0||state.ExpandedStep==1||state.ExpandedStep==2||state.ExpandedStep==3,"Invalid expanded step.");
 if(state.ShowProgress)Check(state.AccentAction==MyGamesAction.None&&state.AccentButton=="","A running step must show progress without an accent action.");
 if(row.Key=="setup-running")Check(!state.ShowStop,"Setup copy creation must not offer a stop button when it cannot be canceled safely.");
 if(row.Key is "first-check-running" or "size-check-running")Check(state.ShowStop,"A running launch check must offer Stop.");
 if(row.Key=="problem-normal-driver"||row.Key=="problem-size-normal-driver")Check(state.AccentButton=="Launch again","Normal-driver recovery must use the plain Launch again action.");
 if(row.Key=="not-set-up")Check(state.AccentButton=="Set up this game"&&state.Step1Body.Contains("Default-size"),"Initial setup must create the unchanged Default copy first.");
 if(row.Key=="size-apply-pending")Check(state.ExpandedStep==3&&state.AccentButton=="Apply 1 GB"&&state.Step1Complete&&state.Step2Complete,"Cache size must unlock after the first launch check.");
 if(row.Key=="size-launch-waiting")Check(state.ExpandedStep==3&&state.AccentButton==row.Input.LaunchLabel&&state.Step1Complete&&state.Step2Complete,"A non-default size must require a final launch confirmation.");
 if(row.Key=="default-verified")Check(state.HeaderStatus=="Ready to play · Default"&&state.AccentButton==row.Input.LaunchLabel,"Default verified header/action mismatch.");
 if(row.Key=="size-verified")Check(state.HeaderStatus=="Ready to play · 1 GB"&&state.Step3Complete,"Verified cache size state mismatch.");
 if(row.Key=="default-verified")Check(row.Input.CacheSize=="Default"&&state.ShowSizeChoices&&state.AccentAction==MyGamesAction.Launch,"Default verified must show the applied Default selection and no Apply action.");
 if(row.Key=="profile-not-found")Check(state.HeaderStatus==state.Step1Result&&state.AccentAction==MyGamesAction.LaunchForDiscovery&&state.Step1Body.Contains("Start the game once in DirectX 12"),"Undetected games must offer discovery with matching status text.");
 if(row.Key=="no-selection")Check(state.AccentAction==MyGamesAction.None,"No selection must not offer a useless Choose game action.");
 if(row.Key=="dx12-unavailable")Check(state.ListBadge=="No DirectX 12"&&state.AccentAction==MyGamesAction.None,"A game without DirectX 12 must explain the limit without a dead-end action.");
 if(row.Key=="game-uninstalled")Check(state.ListBadge=="Not installed","Uninstalled games need a specific list badge.");
 if(row.Key=="blocked-by-another-mod")Check(state.HeaderStatus=="Blocked by another mod"&&state.ListBadge=="Blocked"&&!state.ShowGameFlow&&state.AccentAction==MyGamesAction.OpenGameFolder&&state.AccentButton=="Open game folder"&&state.Warning!.Contains("OptiScaler")&&state.Warning.Contains(row.Input.ModBlocker!.Path)&&state.Warning.Contains("combining both can crash")&&state.Warning.Contains("Remove or disable the mod, then check again"),"A local DirectX proxy must have a clear blocked state, identity, path and recovery action.");
 if(row.Key=="anticheat-running")Check(!state.ShowGameFlow&&state.AccentAction==MyGamesAction.None&&state.ListBadge=="Anti-cheat"&&state.Warning!.Contains("Easy Anti-Cheat")&&state.Warning.Contains("single-player")&&state.Warning.Contains("anti-cheat disabled"),"A game seen with a running anti-cheat must not offer setup and must say how to clear it.");
 if(row.Key=="anticheat-files")Check(!state.ShowGameFlow&&state.AccentAction==MyGamesAction.None&&state.Warning!.Contains("Riot Vanguard")&&state.Warning.Contains("banned"),"An always-on anti-cheat must block setup until the user accepts the risk.");
 if(row.Key is "anticheat-running" or "anticheat-files"){var accepted=MyGamesPageStates.Resolve(row.Input with{AntiCheatOverride=new DateTime(2026,10,17,10,0,0,DateTimeKind.Utc)});Check(accepted.Key=="not-set-up"&&accepted.AccentAction==MyGamesAction.SetUp,"After the user accepts the anti-cheat risk, the normal setup must continue.");}
 if(row.Key=="anticheat-with-copy")Check(!state.ShowGameFlow&&state.AccentButton=="Use default settings"&&state.Warning!.Contains("BattlEye"),"An anti-cheat next to a driver copy must lead to default settings.");
 if(row.Key=="setup-needs-attention")Check(state.HeaderStatus=="Setup needs attention"&&state.ListBadge=="Needs attention"&&!state.ShowGameFlow&&!state.ShowSizeChoices&&!state.ShowSizeApply&&state.AccentAction==MyGamesAction.RestoreGame&&state.AccentButton=="Use default settings"&&state.Warning!.Contains("doesn't match the current game files")&&!state.Warning.Contains(row.Input.DiscoveryMessage!),"A saved setup whose game profile no longer matches must explain the safe next step without presenting the raw DX12 discovery instruction as current advice.");
 if(row.Key=="size-verified-after-close"){
  var effective=MyGamesPageStates.EffectiveLoadResult(MyGamesLoadResult.Unknown,true,16384,"size-loading-verified");
  Check(effective==MyGamesLoadResult.DriverCopyLoaded&&state.Key=="size-verified-after-close"&&state.HeaderStatus=="Ready to play · 16 GB","A persisted successful size check must remain verified after closing the game or restarting Arc Cache.");
  var running=MyGamesPageStates.WhileGameRunning(state);
  Check(!running.ShowSizeChoices&&!running.ShowSizeApply&&running.AccentAction==MyGamesAction.None&&running.Warning!.Contains("Close it to change the cache size"),"While the game runs, hide cache-size actions and explain why.");
 }
 if(row.Key=="size-retest-required")Check(state.HeaderStatus=="Retest this size"&&state.AccentAction==MyGamesAction.OpenGraphics&&state.Warning!.Contains("older app version")&&state.Warning.Contains("default settings under Advanced"),"Old large-size test must give a clear retest path and preserve the default-settings recovery path.");
 if(row.Key=="problem-recovery")Check(!state.ShowGameFlow&&state.Warning is not null,"Recovery state must show its reason without placeholder setup steps.");
 if(row.Key is "problem-normal-driver" or "problem-size-normal-driver")Check((state.Step2Body+state.Step3Body).Contains("Make sure the game starts in DirectX 12"),"Normal-driver result needs a specific one-line fix.");
 if(row.Key=="setup-running")Check(state.ExpandedStep==1&&state.ProgressText.StartsWith("Step 1 of 3"),"Setup progress must belong to the running first step.");
 if(row.Key=="size-check-running")Check(state.ExpandedStep==3&&state.ShowStop&&state.ProgressText.StartsWith("Step 3 of 3"),"Size-check progress and Stop must belong to step 3.");
});
Test("Trial setup requires game observed with tested driver copy",()=>{
 var state=MyGamesPageStates.Resolve(new(RequiresTrialProfile:true,TrialProfileFound:false,ProfileFound:true));
 Check(state.Key=="trial-profile-not-found"&&state.AccentAction==MyGamesAction.LaunchForDiscovery&&!state.ShowSetupAction,"A stock-driver profile must not expose setup before the tested driver copy has been observed.");
});
Test("Introduction is shown for a new profile, stays dismissed across updates, and recovers invalid preferences",()=>{
 string root=Path.Combine(Path.GetTempPath(),"ArcWelcomeTest-"+Guid.NewGuid());
 try{var intro=new Introduction(root);Check(intro.ShouldShow,"New profile skipped introduction.");intro.Dismiss();Check(!new Introduction(root).ShouldShow,"Dismissal did not persist.");File.WriteAllText(Path.Combine(root,"introduction-version.txt"),"invalid");Check(new Introduction(root).ShouldShow,"Invalid preference hid introduction.");Check(Introduction.Screens.Length==3&&Introduction.Screens[0].Points.Any(p=>p.Contains("own copy"))&&Introduction.Screens[0].Points.Any(p=>p.Contains("cache limit")),"Introduction does not explain the game-specific setup.");Check(Introduction.Screens.Any(s=>s.Body.Contains("DevOverrideEnable")&&s.Points.Any(p=>p.Contains("whole PC"))&&s.Points.Any(p=>p.Contains("restart"))),"Introduction does not explain the Windows setting.");}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
});
Test("Independent app instances do not collide or fail operations when a log is locked",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcLogTest-"+Guid.NewGuid());
    try{
        var first=new AppStore(root);var second=new AppStore(root);
        Check(first.LogPath!=second.LogPath,"App instances share a log file.");
        first.Log("first");second.Log("second");
        Check(File.ReadAllText(first.LogPath).Contains("first")&&File.ReadAllText(second.LogPath).Contains("second"),"App log entries missing.");
        using(var locked=new FileStream(first.LogPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){first.Log("locked");second.Log("still works");}
        Check(File.ReadAllText(second.LogPath).Contains("still works"),"Another instance stopped logging.");
    }finally{Directory.Delete(root,true);}
});
Test("Home dashboard recents persist newest-first, deduplicate and reject unsafe keys",()=>{
 string root=Path.Combine(Path.GetTempPath(),"ArcRecentTest-"+Guid.NewGuid().ToString("N"));
 try{var store=new AppStore(root);for(uint id=1;id<=10;id++)store.MarkRecentGame("steam-"+id);store.MarkRecentGame("steam-3");var recents=store.ReadRecentGames();Check(recents.Count==8&&recents[0]=="steam-3"&&!recents.Contains("steam-2"),"Recent games did not persist in bounded newest-first order.");try{store.MarkRecentGame("../../unsafe");throw new Exception("Unsafe recent-game key was accepted.");}catch(InvalidDataException){}}
 finally{if(Directory.Exists(root))Directory.Delete(root,true);}
});
Test("The guide provides one next action from discovery to playing and handles older setups",()=>{
 Check(GameGuide.From(null,false,true,false,true).Action=="Launch","Passing a driver test leaves game discovery without an action.");
 Check(GameGuide.From(null,true,true,false,true).Action=="Prepare","Detected game does not offer setup.");
 Check(GameGuide.From(null,true,false,false,true).Action=="Graphics","Windows setup step does not open Graphics.");
 var receipt=new ReceiptState(5,"game.exe","game.exe.local",512,"prepared-runtime-unverified",new(),true);var state=new GameSetupState(true,true,true,true,receipt,null);
 Check(GameGuide.From(state,true,true,true,true).Title=="Check the game setup"&&GameGuide.From(state,true,true,true,true).Action=="Launch","Unchanged game copy does not lead to a loading check.");
 state=state with{Receipt=receipt with{Status="original-control-loading-verified"}};Check(GameGuide.From(state,true,true,true,true).Action=="Apply","Verified game control does not lead to applying the size.");
 state=state with{Receipt=receipt with{MiB=1024}};Check(GameGuide.From(state,true,true,true,true).Title.Contains("ready to try")&&GameGuide.From(state,true,true,true,true).Action=="Launch","Patched game does not offer play.");
 Check(GameGuide.From(state,true,true,false,true).Action=="Restore","Older setup has no safe next action.");
});
Test("Games use the checked driver's per-size patch list",()=>{
 var reviewed=new[]{512,1024,2048,4096,8192,16384}.ToDictionary(n=>n,n=>new DriverLimitCapability(n,true,"reviewed"));
 Check(DriverLimitChoices.IsAvailable(4096,reviewed,false),"A game could not use a size qualified for its driver.");
 Check(DriverLimitChoices.IsAvailable(4096,reviewed,true),"A passed size was incorrectly hidden because a game uses a private driver copy.");
 var oneGiB=new[]{512,1024,2048,4096,8192,16384}.ToDictionary(n=>n,n=>new DriverLimitCapability(n,n<=1024,n<=1024?"tested":"not verified"));
 Check(!DriverLimitChoices.IsAvailable(4096,oneGiB,false)&&DriverLimitChoices.Fallback(oneGiB,false)==512,"A one-gigabyte driver test exposed a larger size or poor fallback.");
 Check(DriverLimitChoices.Fallback(new Dictionary<int,DriverLimitCapability>(),false)==512,"No-capability fallback must be the unchanged Default size.");
});
Test("Plain game checks distinguish an observed copy from cache performance",()=>{
    string exe=@"C:\Games\Example\game.exe";
    Check(PlainLanguage.GameCheck("Game is not running.",exe).StartsWith("Start this game"),"Stopped game falsely reports a setup.");
    string result=PlainLanguage.GameCheck(exe+@".local\igd12um64xe2.dll",exe);
    Check(result.Contains("own graphics files")&&result.Contains("not been measured"),"Loading observation claims a capacity/performance result.");
    Check(PlainLanguage.GameCheck("Disk SHA-256: abc",exe).Contains("outside its Arc Cache setup"),"Different driver treated as a custom copy.");
    Check(PlainLanguage.Error("Manually launch as administrator").Contains("Windows setting"),"Elevation prompt policy lost in plain error.");
    Check(PlainLanguage.Error("Hash mismatch").Contains("Stop here"),"Changed files encouraged unsafe retry.");
});

Test("Opt-in diagnostics are local, persist preference, rotate and stop when disabled",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcDiagnostics-"+Guid.NewGuid().ToString("N"));
    try {
        var log=new DiagnosticLog(root);log.Write("off",new{Value=1});Check(!File.Exists(log.Path),"Disabled logging wrote data.");
        log.SetEnabled(true);log.Write("fixture",new{Action="Status"});
        Check(File.ReadLines(log.Path).All(line=>JsonDocument.Parse(line).RootElement.TryGetProperty("utc",out _)),"Invalid JSON lines.");
        Check(new DiagnosticLog(root).Enabled,"Preference was not retained.");
        File.WriteAllText(log.Path,new string('x',2*1024*1024));log.Write("rotate",new{Value=1});
        Check(File.Exists(log.Path+".previous")&&new FileInfo(log.Path).Length<1024,"Diagnostics failed to rotate.");
        log.SetEnabled(false);long size=new FileInfo(log.Path).Length;log.Write("off",new{Value=2});Check(new FileInfo(log.Path).Length==size&&!new DiagnosticLog(root).Enabled,"Disabled log kept recording.");
        Check(new DiagnosticLog(root,true).Enabled,"--debug session mode ignored.");
        Check(!new DiagnosticLog(root).Enabled,"Session mode persisted itself.");
    }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
});

Test("Inactive routing preserves the saved limit and gives an enable action",()=>{
    var receipt=new ReceiptState(3,"game.exe","game.exe.local",4096,"changed-runtime-unverified",new());
    var ui=SetupPresentation.From(new GameSetupState(true,true,true,true,receipt,null),true,false);
    Check(ui.Installed.StartsWith("4 GB saved")&&ui.Title=="Enable game-specific settings"&&ui.Next.Contains("restart Windows"),"Inactive routing lost saved cap or reported ready.");
});
Test("Routing statuses distinguish off, on and pending restart",()=>{
    Check(RoutingPresentation.Status(false,false).StartsWith("Off"),"Disabled routing shown as ready.");
    Check(RoutingPresentation.Status(true,false).StartsWith("On"),"Enabled routing shown as disabled.");
    Check(RoutingPresentation.Status(true,true).StartsWith("Restart Windows"),"Pending enable shown as ready.");
    Check(RoutingPresentation.Status(false,true).StartsWith("Restart Windows"),"Pending restore shown as completed.");
});

Test("Installed wide cap is distinct from unmeasured runtime behavior",()=>{
    var receipt=new ReceiptState(3,"game.exe","game.exe.local",4096,"changed-runtime-unverified",new());
    var state=new GameSetupState(true,true,true,true,receipt,null);
    var ui=SetupPresentation.From(state,true);
    Check(ui.Installed.StartsWith("4 GB set")&&ui.Title=="Ready to play"&&!ui.WaitingForControl,"Applied patch presented as missing or waiting for original control.");
    Check(ui.Next.Contains("usual launcher")&&!ui.WaitingForControl,"Installed patch still shown as needing setup.");
    Check(SetupPresentation.From(state,false).Installed.StartsWith("4 GB set"),"Stale executable erased verified installed limit.");
    Check(SetupPresentation.From(state with{ContentsValid=false,Error="Hash mismatch"},true).Title=="Fix this setup","Invalid disk files shown as applied.");
});
Test("Larger setup distinguishes an experimental test from inactive routing",()=>{
    var receipt=new ReceiptState(4,"game.exe","game.exe.local",8192,"changed-runtime-unverified",new());
    var state=new GameSetupState(true,true,true,true,receipt,null);
    Check(SetupPresentation.From(state,true).Title=="Ready to try this setting","Untested larger patch was presented as proven gameplay.");
    var inactive=SetupPresentation.From(state,true,false);
    Check(inactive.Installed.StartsWith("8 GB saved")&&inactive.Installed.Contains("setting is off")&&inactive.Next.Contains("restart"),"Disabled routing was presented as an active installed patch.");
});
Test("Updated driver preserves saved cap but requires review",()=>{
    var receipt=new ReceiptState(4,"game.exe","game.exe.local",8192,"changed-runtime-unverified",new());
    var state=new GameSetupState(true,true,true,true,receipt,null);
    var ui=SetupPresentation.From(state,true,true,false);
    Check(ui.Installed.Contains("8 GB saved")&&ui.Title=="Check your updated graphics driver"&&ui.Next.Contains("default settings"),"Driver update erased setup or presented outdated private driver as ready.");
});
Test("Old private setup needing compatibility migration blocks patching without erasing disk state",()=>{
    var receipt=new ReceiptState(3,"game.exe","game.exe.local",4096,"changed-runtime-unverified",new());
    var state=new GameSetupState(true,true,true,true,receipt,null,true);
    Check(!state.Configured&&!state.CanPrepare&&state.RecordValid,"Unsafe existing setup allowed patching or duplicate preparation.");
    var ui=SetupPresentation.From(state,true);Check(ui.Installed.StartsWith("4 GB set")&&ui.Next.Contains("set it up again"),"Migration erased installation status or failed to give recovery steps.");
});
Test("Setup workflow advances from detection through control to patch",()=>{
    Check(SetupPresentation.From(null,false).Title.Contains("Start this game"),"Missing detection step.");
    var empty=new GameSetupState(false,false,false,false,null,null);
    Check(SetupPresentation.From(empty,true).Title.Contains("Set up this game"),"Missing prepare step.");
    var receipt=new ReceiptState(3,"game.exe","game.exe.local",512,"prepared-runtime-unverified",new());
    var state=new GameSetupState(true,true,true,true,receipt,null);
    Check(SetupPresentation.From(state,true).WaitingForControl,"Original control check bypassed.");
    Check(SetupPresentation.From(state with{Receipt=receipt with{Status="original-control-loading-verified"}},true).Title.Contains("Apply your cache size"),"Verified control still waiting.");
    Check(SetupPresentation.From(state with{FolderExists=false,Receipt=receipt with{Status="removed"}},true).Title.Contains("Set up this game"),"Removed setup shown as installed.");
});
Test("Steam artwork resolves flat, direct app-ID and hashed folder assets with icon fallbacks",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcArtwork-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jN9sAAAAASUVORK5CYII=");
    string cache=Path.Combine(root,"appcache","librarycache");Directory.CreateDirectory(cache);
    void Image(string relative){string path=Path.Combine(cache,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,png);}
    try {
        Image("1_library_600x900.png");Image("1_icon.png");var flat=SteamArtwork.Find(root,1);Check(flat.Portraits.Count==1&&flat.Icons.Count==1,"Legacy layout missed.");
        Image(Path.Combine("2","library_600x900.jpg"));Image(Path.Combine("2",new string('a',40)+".jpg"));var direct=SteamArtwork.Find(root,2);Check(direct.Portraits.Count==1&&direct.Icons.Count==1,"Direct modern layout or hashed icon missed.");
        Image(Path.Combine("3",new string('b',40),"library_capsule.jpg"));Image(Path.Combine("3",new string('c',40),"library_header.jpg"));var hashed=SteamArtwork.Find(root,3);Check(hashed.Portraits.Count==2&&hashed.Portraits[0].EndsWith("library_capsule.jpg"),"Portrait priority or header fallback wrong.");
        File.WriteAllText(Path.Combine(cache,"2","library_600x900.jpg"),"broken artwork");Image(Path.Combine("2","header.png"));Check(SteamArtwork.Find(root,2).Portraits.Single().EndsWith("header.png"),"Corrupt leading file blocked fallback.");
        Check(SteamArtwork.Find(root,999).Portraits.Count==0,"Missing artwork guessed.");
        var game=new SteamGame(3,"Fixture",root,hashed.Portraits[0],ArtworkFallbacks:hashed.Portraits.Skip(1).ToArray());Check(game.IconSources.SequenceEqual(game.ArtworkSources)&&game.ArtworkSources.Count==2,"Icon has no portrait fallback.");
    }finally{Directory.Delete(root,true);}
});
Test("Game list anti-cheat hints: always-on beats optional, nothing found, missing folder",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcAntiCheatHints-"+Guid.NewGuid().ToString("N"));
    try{
        string clean=Path.Combine(root,"clean","bin");Directory.CreateDirectory(clean);File.WriteAllText(Path.Combine(clean,"game.exe"),"");
        string eac=Path.Combine(root,"mcc","easyanticheat");Directory.CreateDirectory(eac);File.WriteAllText(Path.Combine(root,"mcc","mcclauncher.exe"),"");
        string both=Path.Combine(root,"both","a","b");Directory.CreateDirectory(both);Directory.CreateDirectory(Path.Combine(root,"both","BattlEye"));File.WriteAllText(Path.Combine(both,"vgk.sys"),"");
        var none=AntiCheatHints.Scan(Path.Combine(root,"clean"));Check(none.Level==AntiCheatLevel.NoneFound&&none.Badge=="","Clean folder should show no badge.");
        var optional=AntiCheatHints.Scan(Path.Combine(root,"mcc"));Check(optional.Level==AntiCheatLevel.Optional&&optional.Name=="Easy Anti-Cheat"&&optional.Badge=="Anti-cheat (EAC)","Easy Anti-Cheat folder (case-insensitive) should be optional.");
        var always=AntiCheatHints.Scan(Path.Combine(root,"both"));Check(always.Level==AntiCheatLevel.AlwaysOn&&always.Name=="Riot Vanguard","Always-on marker deeper in the folder must win over an optional one.");
        Check(AntiCheatHints.Scan(Path.Combine(root,"missing")).Level==AntiCheatLevel.NotChecked,"Missing folder is not checked.");
    }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
});
Test("Safety warnings stay brief while retaining multiplayer, anti-cheat and unknown risk",()=>{
    JsonElement Assessment(bool allowed,string? marker,string reason,string multi)=>JsonSerializer.SerializeToElement(new{Allowed=allowed,Marker=marker,Message=reason,Multiplayer=multi});
    Check(!SafetyPresentation.From(Assessment(true,null,"Clear","Steam reports single-player")).Warn,"Positive scan creates warning clutter.");
    var antiCheat=SafetyPresentation.From(Assessment(false,"EasyAntiCheat","Indicator found","Unknown"));
    var multiplayer=SafetyPresentation.From(Assessment(false,null,"Steam declares multiplayer/co-op","Steam reports multiplayer/co-op"));
    var unknown=SafetyPresentation.From(Assessment(false,null,"Unavailable","Unknown"));
    Check(antiCheat.Warn&&antiCheat.Message.Contains("anti-cheat warning")&&antiCheat.Message.Contains("original driver"),"Anti-cheat warning lost its safe next action.");
    Check(multiplayer.Warn&&multiplayer.Message.Contains("account penalties")&&multiplayer.Message.Contains("original driver"),"Online warning lost its safe next action.");
    Check(unknown.Warn&&unknown.Title.Contains("safety is unknown")&&unknown.Message.Contains("Continue only if"),"Unknown game safety was presented as safe.");
    Check(SafetyPresentation.From(Assessment(false,null,"Steam declares Valve Anti-Cheat","Unknown")).Title=="This game uses anti-cheat","VAC declaration treated as generic multiplayer.");
});
Test("SCSKiller bridge uses selected Steam ID, child-only Intel opt-in and no shell",()=>{
    var command=ScsKillerBridge.Command(Path.Combine(Path.GetTempPath(),"scskiller.exe"),"compile",292030);
    Check(command.ArgumentList.SequenceEqual(new[]{"compile","steam:292030","--community"})&&!command.UseShellExecute&&command.RedirectStandardInput&&command.Verb=="","Unexpected bridge command or escalation.");
    Check(command.Environment["SCSKILLER_EXPERIMENTAL_INTEL"]=="1","Missing explicit child opt-in.");
    Check(command.Environment["SCSKILLER_ALLOW_LIVE_DATA"]=="1"&&ScsKillerBridge.RecorderCommand(command.FileName,292030,true).Environment["SCSKILLER_ALLOW_LIVE_DATA"]=="1","SCSKiller 1.4 refuses the real data folder without it.");
    bool rejected=false;try{ScsKillerBridge.Command(command.FileName,"record",292030);}catch(ArgumentException){rejected=true;}Check(rejected,"Recorder installation must not be exposed through bridge.");
});
Test("My games: an older SCSKiller recorder offers Arc Cache's, any other mod the game folder",()=>{
    var recorder=new LocalModBlocker(@"C:\Games\FF7\Win64\d3d12.dll",ProductName:"SCSKiller",FileDescription:"SCSKiller D3D12 proxy",CompanyName:"The SCSKiller authors");
    var update=MyGamesPageStates.Resolve(new MyGamesPageInputs(ModBlocker:recorder,RecorderUpdate:true));
    Check(update.AccentAction==MyGamesAction.UpdateRecorder&&update.AccentButton=="Use Arc Cache's recorder"&&update.ListBadge=="Blocked"&&update.Warning!.Contains("can't run beside a driver copy"),"An older recorder must offer Arc Cache's: "+update.AccentButton);
    var other=MyGamesPageStates.Resolve(new MyGamesPageInputs(ModBlocker:recorder));
    Check(other.AccentAction==MyGamesAction.OpenGameFolder,"Without a newer recorder at hand the folder opens, as for any mod.");
});
Test("SCSKiller's recorder: its own command, recognised by its version information",()=>{
    var on=ScsKillerBridge.RecorderCommand(Path.Combine(Path.GetTempPath(),"scskiller.exe"),1144200,true);
    Check(on.ArgumentList.SequenceEqual(new[]{"record","install","steam:1144200"})&&!on.UseShellExecute&&on.Environment["SCSKILLER_EXPERIMENTAL_INTEL"]=="1","Unexpected recorder command: "+string.Join(" ",on.ArgumentList));
    Check(ScsKillerBridge.RecorderCommand(on.FileName,1144200,false).ArgumentList.SequenceEqual(new[]{"record","uninstall","steam:1144200"}),"Unexpected recorder removal command.");
    bool rejected=false;try{ScsKillerBridge.RecorderCommand(on.FileName,0,true);}catch(ArgumentException){rejected=true;}Check(rejected,"A recorder without a Steam ID was accepted.");
    string folder=Path.Combine(Path.GetTempPath(),"ArcRecorder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    try{
        string exe=Path.Combine(folder,"game.exe");Check(!ScsKillerBridge.RecorderInstalled(exe),"An empty folder has a recorder.");
        File.WriteAllText(Path.Combine(folder,"d3d12.dll"),"another mod");Check(!ScsKillerBridge.RecorderInstalled(exe),"Any d3d12.dll was taken for SCSKiller's recorder.");
        string ff7=@"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII REMAKE\End\Binaries\Win64\d3d12.dll";
        if(ScsKillerBridge.IsRecorder(ff7)){File.Copy(ff7,Path.Combine(folder,"d3d12.dll"),true);Check(ScsKillerBridge.RecorderInstalled(exe),"SCSKiller's recorder was not recognised.");}
        // The official app's 1.3.0 recorder (where it's installed) is the older kind.
        string official=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCSKiller.App","current","native","d3d12.dll");
        if(ScsKillerBridge.IsRecorder(official)&&System.Diagnostics.FileVersionInfo.GetVersionInfo(official).Comments!="exe.local-aware"){File.Copy(official,Path.Combine(folder,"d3d12.dll"),true);Check(ScsKillerBridge.RecorderInstalled(exe)&&!ScsKillerBridge.InstalledRecorderDotLocalAware(exe),"An older recorder was taken for one that runs beside a driver copy.");}
        string? built=null;for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null&&built==null;d=d.Parent){string p=Path.Combine(d.FullName,"out","SCSKiller","native","d3d12.dll");if(File.Exists(p))built=p;}
        if(built!=null){File.Copy(built,Path.Combine(folder,"d3d12.dll"),true);Check(ScsKillerBridge.InstalledRecorderDotLocalAware(exe),"Arc Cache's SCSKiller recorder must run beside a driver copy: "+built);}
    }finally{Directory.Delete(folder,true);}
});
Test("SCSKiller options, progress lines and build support are read correctly",()=>{
    var tuned=ScsKillerBridge.Command(Path.Combine(Path.GetTempPath(),"scskiller.exe"),"compile",1,null,new ScsKillerOptions(8,true,false));
    Check(tuned.ArgumentList.SequenceEqual(new[]{"compile","steam:1","--threads","8","--idle"}),"Compile options were not passed: "+string.Join(" ",tuned.ArgumentList));
    var status=ScsKillerBridge.Command(Path.Combine(Path.GetTempPath(),"scskiller.exe"),"status",1,null,new ScsKillerOptions(8,true,true));
    Check(status.ArgumentList.SequenceEqual(new[]{"status","steam:1","--community"}),"Status took compile-only options.");
    var warming=ScsKillerBridge.ParseProgress("SCSKiller: 20:19:35 steam:1623730 Warming 1200/94165 (3 failed) 450/s");
    Check(warming is {Stage:"Warming",Done:1200,Total:94165}&&warming.Detail.Contains("450/s"),"Warming progress was not parsed.");
    Check(ScsKillerBridge.ParseProgress("20:19:15 steam:1623730 Indexing") is {Stage:"Indexing",Total:0},"Stage-only line was not parsed.");
    Check(ScsKillerBridge.ParseProgress("SCSKiller log:   plan: 94165 PSOs")==null,"A log line was read as progress.");
    var arc=ScsKillerBridge.Describe("C:\\x\\scskiller.exe","","Arc Cache's SCSKiller",0,"{\"tool\":\"SCSKiller-Arc\",\"version\":\"0.0.0-internal.0+f3acf3968a6e\",\"arcCacheHandoff\":1,\"warm\":\"w\",\"warmDriverLocal\":true}\n");
    Check(arc.PrivateDriverSupport&&arc.Version=="development build f3acf39","Arc Cache's SCSKiller was not recognised: "+arc.Version);
    var oldWarm=ScsKillerBridge.Describe("x","","Installed SCSKiller",0,"{\"arcCacheHandoff\":1,\"version\":\"1.2.5\",\"warmDriverLocal\":false}");
    Check(!oldWarm.PrivateDriverSupport&&oldWarm.Note.Contains("scskiller_warm"),"An old warm exe was accepted for driver copies.");
    var upstream=ScsKillerBridge.Describe("x","1.3.0-beta.2","Portable SCSKiller",1,"error: unknown command 'arc-cache-info'");
    var gated=ScsKillerBridge.Describe("x","1.4.1-alpha.1","Installed SCSKiller",1,"error: x\nThis alpha needs a Patreon backer sign-in in the SCSKiller app first.");
    Check(gated.NeedsSignIn&&!gated.PrivateDriverSupport,"An alpha without sign-in must ask for it.");
    var alpha=ScsKillerBridge.Describe("x","1.4.1-alpha.1","Installed SCSKiller",1,"error: unknown command 'arc-cache-info'");
    Check(!alpha.NeedsSignIn&&!alpha.PrivateDriverSupport&&alpha.Note.Contains("traversal"),"A signed-in alpha is a Default-games SCSKiller.");
    Check(!upstream.PrivateDriverSupport&&upstream.Version=="1.3.0-beta.2"&&upstream.Note.Contains("Default only"),"An official build was accepted for driver copies.");
    Check(ScsKillerBridge.Origin(@"C:\Users\Nick\Desktop\scskiller-1.3.0-beta.2-portable\current\cli\scskiller.exe")=="Portable SCSKiller","Portable build not named.");
    string settings=Path.Combine(Path.GetTempPath(),"ArcScsOptions-"+Guid.NewGuid().ToString("N")+".json");
    try{new ScsKillerBridge(settings).SaveOptions(new ScsKillerOptions(4,true,false));var loaded=new ScsKillerBridge(settings).Load().Options;Check(loaded==new ScsKillerOptions(4,true,false),"Options were not kept.");}
    finally{File.Delete(settings);}
});
Test("SCSKiller card reads shader counts, estimates and progress from one model",()=>{
    var warming=ScsKillerBridge.ParseProgress("SCSKiller: 20:19:35 steam:1623730 Warming 1200/94165 (3 failed, 2 skipped: not in this install) 450/s - retrying ray tracing with fewer threads (8)");
    Check(warming is {Failed:3,Skipped:2,PerSecond:450,Note:"retrying ray tracing with fewer threads (8)"},"Warming counts were not parsed.");
    var view=ScsPresentation.Progress(false,warming,TimeSpan.FromSeconds(85),"cache file 528 MB of 16 GB","16 GB driver copy confirmed");
    Check(view.Percent is >1.2 and <1.3&&view.Headline=="Step 4 of 4: Compiling · 1%","Progress headline: "+view.Headline);
    Check(view.Detail.Contains("1,200 of 94,165 compile jobs"),"Counts missing: "+view.Detail);
    Check(view.Detail.Contains("per second")&&view.Detail.Contains("left")&&view.Detail.Contains("3 failed")&&view.Detail.Contains("528 MB of 16 GB")&&view.Detail.Contains("running 1:25"),"Progress detail incomplete: "+view.Detail);
    Check(ScsPresentation.Progress(false,ScsKillerBridge.ParseProgress("20:19:15 steam:1 Indexing"),TimeSpan.FromSeconds(5),null,null) is {Percent:null,Headline:"Step 1 of 4: Reading the game's shaders"},"Early step not indeterminate.");
    Check(ScsPresentation.Progress(false,warming,TimeSpan.FromMinutes(31),null,null,done:true) is {Percent:100,Headline:"Done · 1,200 compile jobs in 31 min"},"Done must read Done at 100%.");
    Check(ScsPresentation.Progress(false,warming,TimeSpan.FromMinutes(3),null,null,stopped:true).Detail.Contains("stays in the cache"),"Stopped must say what is kept.");
    string[] output=["Elden Ring (steam:1245620, Steam)","  install    C:/Games/ELDEN RING","  status     Ready: ready to compile","  anti-cheat None","  shaders    1234567",
        "  plan       90000 recorded + 4165 generated + 0 DirectX 11 shaders + 2 middleware, 12 root sigs, rule verified: True","  estimate   1,2 GB of cache, 0:35:10",
        "  warmed     2026-10-19 22:41 for driver 32.0.101.8132 in 0:31:12; 3 failed (the driver rejected them)",
        "  last play  1:02:03, 5000 pipelines: 4000 from the game's library, 900 cache hits, 100 compiles (worst 45.5 ms)","  1st launch -"];
    var status=ScsPresentation.ParseStatus(output.Select(l=>"SCSKiller: "+l),DateTime.UtcNow);
    Check(status is {Status:"Ready",ShadersInGame:1234567,Recorded:90000,Generated:4165,Pipelines:94165,LastPlayCompiles:100,LastPlayWorstMs:"45.5"},"Status detail not parsed.");
    Check(status!.EstimateBytes is >1200L<<20 and <1300L<<20&&status.EstimateTime==new TimeSpan(0,35,10)&&status.WarmTime==new TimeSpan(0,31,12)&&status.WarmedAt?.Day==19,"Estimate or warm not parsed.");
    var view2=ScsPresentation.DetailView(status,512,528L<<20,true);string Row(ScsDetailView v,string label)=>v.Rows.FirstOrDefault(r=>r.Label==label).Value??"";
    Check(view2.Title=="Ready to prepare"&&view2.Reason=="Ready to compile.","Title or reason: "+view2.Title+" / "+view2.Reason);
    Check(Row(view2,"Cache file")=="528 MB of 512 MB"&&Row(view2,"Estimated need")=="about 1.2 GB"&&Row(view2,"Time to prepare")=="about 35 min","Cache rows: "+string.Join(" | ",view2.Rows));
    Check(Row(view2,"Pipelines")=="94,165 (90,000 recorded, 4,165 generated) from 1,234,567 shaders","Numbers must use one format: "+Row(view2,"Pipelines"));
    Check(Row(view2,"Last prepared").Contains("31 min")&&Row(view2,"Not compiled").Contains("3 failed")&&view2.Warning!.Contains("more than the cache size"),"Last prepared or warning missing.");
    Check(ScsPresentation.DetailView(status,16384).Warning==null,"A 16 GB cache size was warned about a 1.2 GB estimate.");
    var warmed=ScsPresentation.DetailView(status with{Status="Warmed"},16384,801L<<20,true);
    Check(Row(warmed,"Estimated need")==""&&Row(warmed,"Time to prepare")==""&&Row(warmed,"Cache file")=="801 MB of 16 GB","After preparing, the real cache file replaces the estimate.");
    var notChecked=ScsPresentation.DetailView(null,512);
    Check(notChecked.Title=="Not checked yet"&&notChecked.Reason==ScsPresentation.NotChecked&&notChecked.Rows.Single()==("Cache file","not linked yet · limit 512 MB"),"Unchecked game must show what a check tells and the cache file.");
    var noted=warming! with{Note="15 failed (the driver rejected them), 510 skipped"};
    var finished=ScsPresentation.Progress(false,noted,TimeSpan.FromMinutes(3),"cache file 801 MB of 16 GB","16 GB driver copy confirmed",done:true);
    Check(finished.Headline.StartsWith("Done · 1,200 compile jobs")&&finished.Detail.Split("failed").Length==2,"Done must not repeat the failed count: "+finished.Detail);
    Check(ScsPresentation.ParseStatus(["error: no game matches 'steam:1'"],DateTime.UtcNow)==null,"An error was read as a status.");
});
Test("On-screen stats settings keep auto-start and a known corner",()=>{
    string path=Path.Combine(Path.GetTempPath(),"ArcOverlay-"+Guid.NewGuid().ToString("N")+".json");
    try{
        Check(OverlaySettings.Load(path)==new OverlaySettings(),"Missing settings must default to off, top right.");
        new OverlaySettings(true,"bottom-left").Save(path);Check(OverlaySettings.Load(path)==new OverlaySettings(true,"bottom-left"),"Settings were not kept.");
        File.WriteAllText(path,"{\"AutoStart\":true,\"Corner\":\"middle\"}");Check(OverlaySettings.Load(path)==new OverlaySettings(true,"top-right"),"An unknown corner must fall back to top right.");
        bool rejected=false;try{new OverlaySettings(false,"center").Save(path);}catch(ArgumentException){rejected=true;}Check(rejected,"An unknown corner was saved.");
    }finally{File.Delete(path);}
});
Test("Old session logs and overlay CSVs are pruned, nothing else",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcLogs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try{
        string old=Path.Combine(root,"20250101-000000-1-x-winui.log"),csv=Path.Combine(root,"cache-monitor-20250101.csv"),fresh=Path.Combine(root,"20261017-000000-1-y-winui.log"),other=Path.Combine(root,"diagnostics.jsonl");
        foreach(string f in new[]{old,csv,fresh,other}){File.WriteAllText(f,"x");}
        foreach(string f in new[]{old,csv,other})File.SetLastWriteTimeUtc(f,DateTime.UtcNow.AddDays(-40));
        Check(AppStore.PruneLogs(root,TimeSpan.FromDays(30))==2&&!File.Exists(old)&&!File.Exists(csv)&&File.Exists(fresh)&&File.Exists(other),"Pruning removed the wrong files.");
    }finally{Directory.Delete(root,true);}
    Check(SafetyClient.QuickActions.Contains("ObserveGame")&&!SafetyClient.QuickActions.Contains("VerifySize")&&!SafetyClient.QuickActions.Contains("ChangeGameTrial"),"Only display requests may time out.");
});
Test("The cache file a process has open is found exactly, and none or several give no answer",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcOpenFiles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try{
        string a=new string('a',64),b=new string('b',64);File.WriteAllBytes(Path.Combine(root,a),new byte[8]);File.WriteAllBytes(Path.Combine(root,b),new byte[8]);
        Check(CacheFileMap.FileOpenBy(Environment.ProcessId,root)==null,"A file nobody has open was reported.");
        using(var open=new FileStream(Path.Combine(root,b),FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
            Check(CacheFileMap.FileOpenBy(Environment.ProcessId,root)==b,"The open cache file was not found.");
            using var second=new FileStream(Path.Combine(root,a),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            Check(CacheFileMap.FileOpenBy(Environment.ProcessId,root)==null,"Two open files must give no answer.");
        }
    }finally{Directory.Delete(root,true);}
});
Test("Intel cache files are linked to a game only when one file clearly grew",()=>{
    string a=new string('a',64),b=new string('b',64),c=new string('c',64);
    var before=new Dictionary<string,long>{[a]=100L<<20,[b]=5L<<20};
    Check(CacheFileMap.Attribute(before,new Dictionary<string,long>{[a]=900L<<20,[b]=6L<<20})==a,"The growing file was not linked.");
    Check(CacheFileMap.Attribute(before,new Dictionary<string,long>{[a]=101L<<20,[b]=5L<<20,[c]=40L<<20})==c,"A new file was not linked.");
    Check(CacheFileMap.Attribute(before,new Dictionary<string,long>{[a]=200L<<20,[b]=60L<<20})==null,"Two growing files were linked anyway.");
    Check(CacheFileMap.Attribute(before,before)==null,"Nothing grew but a file was linked.");
    Check(!CacheFileMap.IsCacheName("..\\x")&&CacheFileMap.IsCacheName(a),"Cache file names are not validated.");
    string root=Path.Combine(Path.GetTempPath(),"ArcCacheFiles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try{
        File.WriteAllBytes(Path.Combine(root,a),new byte[3]);File.WriteAllText(Path.Combine(root,"notes.txt"),"x");
        var snap=CacheFileMap.Snapshot(root);Check(snap.Count==1&&snap[a]==3&&CacheFileMap.CurrentBytes(a,root)==3,"Snapshot read other files or wrong sizes.");
        var map=new CacheFileMap(Path.Combine(root,"cache-files.json"));map.Remember(@"D:\Games\Palworld-Win64-Shipping.exe",a,3,"test");
        Check(new CacheFileMap(Path.Combine(root,"cache-files.json")).For(@"E:\Other\PALWORLD-Win64-Shipping.exe")?.File==a,"The link is not kept per exe name.");
        Check(new CacheFileMap(Path.Combine(root,"cache-files.json")).For(@"D:\Games\Other.exe")==null,"Another game got the link.");
    }finally{Directory.Delete(root,true);}
});
Test("SCSKiller private driver handoff is compile-only and requires matching loaded-module evidence",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcCache warm handoff");string exe=Path.Combine(root,"game.exe");
    var handoff=new PrivateDriverWarm(exe,exe+".local",4096,"igd12um64xe2.dll",new string('A',64));
    string? oldDir=Environment.GetEnvironmentVariable("SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR");
    try{Environment.SetEnvironmentVariable("SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR",exe+".local");var normal=ScsKillerBridge.Command(Path.Combine(root,"scskiller.exe"),"compile",1);Check(!normal.Environment.ContainsKey("SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR"),"Default compile unexpectedly inherited a caller-provided private path.");}
    finally{Environment.SetEnvironmentVariable("SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR",oldDir);}
    var privateRun=ScsKillerBridge.Command(Path.Combine(root,"scskiller.exe"),"compile",1,handoff);
    Check(privateRun.Environment["SCSKILLER_ARCCACHE_CACHE_MIB"]=="4096"&&privateRun.Environment["SCSKILLER_ARCCACHE_DRIVER_SHA256"]==new string('A',64),"Private handoff fields were not passed to the child only.");
    string path=Path.Combine(root,"stage-1","game.exe.local","igd12um64xe2.dll");string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
    Check(ScsKillerBridge.HasVerifiedPrivateDriverEvidence(new[]{"SCSKiller log: ARC_CACHE_DRIVER_LOADED|"+encoded+"|igd12um64xe2.dll|"+new string('A',64)+"|4096"},handoff),"Matching private driver load evidence was rejected.");
    Check(!ScsKillerBridge.HasVerifiedPrivateDriverEvidence(new[]{"ARC_CACHE_DRIVER_LOADED|"+encoded+"|igd12um64xe2.dll|"+new string('B',64)+"|4096"},handoff),"Wrong loaded hash was accepted.");
    string stock=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(@"C:\Windows\System32\igd12um64xe2.dll"));
    Check(!ScsKillerBridge.HasVerifiedPrivateDriverEvidence(new[]{"ARC_CACHE_DRIVER_LOADED|"+stock+"|igd12um64xe2.dll|"+new string('A',64)+"|4096"},handoff),"Normal system-driver path was accepted as a private-copy load.");
    bool statusRejected=false;try{ScsKillerBridge.Command(Path.Combine(root,"scskiller.exe"),"status",1,handoff);}catch(ArgumentException){statusRejected=true;}Check(statusRejected,"Status command accepted a private driver handoff.");
    bool sizeRejected=false;try{ScsKillerBridge.Command(Path.Combine(root,"scskiller.exe"),"compile",1,handoff with{CacheMiB=512});}catch(ArgumentException){sizeRejected=true;}Check(sizeRejected,"Unsupported private cache size was accepted.");
    bool nameRejected=false;try{ScsKillerBridge.Command(Path.Combine(root,"scskiller.exe"),"compile",1,handoff with{DriverFile="igd12um64xe99.dll"});}catch(ArgumentException){nameRejected=true;}Check(nameRejected,"Unknown Intel DLL family was accepted.");
});
Test("KeyValues comments, escaped paths and nested objects",()=>{
    string text="// comment\n\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\SteamLibrary\" } \"1\" { \"path\" \"E:\\\\Steam Games\" } }";
    Check(KeyValues.Values(text,"path").SequenceEqual(new[]{@"D:\SteamLibrary",@"E:\Steam Games"}),"Paths parsed incorrectly.");
    Reject(()=>KeyValues.Values("\"x\" { \"y\" \"unterminated","x"));
    Reject(()=>KeyValues.Values("\"x\" {","x"));
    Check(KeyValues.Values("\"name\" \"}\"","name").Single()=="}","Quoted brace treated as structure.");
});
Test("Metadata nesting and size bounds",()=>{
    Reject(()=>KeyValues.Values(new string('a',4*1024*1024+1),"x"));
    string deep=string.Concat(Enumerable.Repeat("\"x\" { ",66))+string.Concat(Enumerable.Repeat("} ",66));Reject(()=>KeyValues.Values(deep,"x"));
});
Test("Steam multiple libraries, install flags, duplicate IDs and traversal",()=>{
    string root=Path.Combine(Path.GetTempPath(),"ArcSteamTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
    try {
        string library=Path.Combine(root,"external");Directory.CreateDirectory(Path.Combine(root,"steamapps"));Directory.CreateDirectory(Path.Combine(library,"steamapps","common","The Witcher 3"));
        File.WriteAllText(Path.Combine(root,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+library.Replace("\\","\\\\")+"\" } }");
        string Manifest(uint id,string folder,string flags="4")=>$"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"Fixture {id}\" \"installdir\" \"{folder}\" \"StateFlags\" \"{flags}\" }}";
        File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_292030.acf"),Manifest(292030,"The Witcher 3"));
        File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_2.acf"),Manifest(2,"The Witcher 3","2"));
        File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_3.acf"),Manifest(3,"../.."));
        File.WriteAllText(Path.Combine(library,"steamapps","appmanifest_4.acf"),"broken {");
        var result=SteamLibrary.Discover(root);Check(result.Games.Count==1&&!result.Games[0].Supported,"Installed games need runtime detection, not a title whitelist.");Check(result.Warnings.Count==1,"Malformed manifest should produce a warning.");
        Check(!new SteamGame(123,"Other",root,null).Supported,"Unsupported game enabled.");
        Check(new SteamGame(123,"Other",root,null,Path.Combine(root,"game.exe"),true).Supported,"Observed games should not require an app-ID whitelist.");
    }finally{Directory.Delete(root,true);}
});
if(args.Length>0) {
    var safety=new SafetyClient(Path.GetFullPath(args[0]));
    Test("Non-Steam safety remains unknown without querying Steam categories",()=>{
        string root=Path.Combine(Path.GetTempPath(),"ArcOfflineSafety-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try{var result=safety.RunAsync(new("Safety",InstallDirectory:root,LibrarySource:"Epic",ExternalId:"fixture")).GetAwaiter().GetResult();Check(result.Ok&&!result.Data.GetProperty("Allowed").GetBoolean()&&result.Data.GetProperty("SteamStatus").GetString()!.Contains("No Steam"),"Non-Steam game borrowed Steam safety metadata.");}finally{Directory.Delete(root,true);}
    });
    Test("Non-Steam discovery refuses identity spoofing and read-only scans do not pin a profile",()=>{
        string root=Path.Combine(Path.GetTempPath(),"ArcExternalScan-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try{
            var denied=safety.RunAsync(new("DiscoverGame",SteamAppId:292030,InstallDirectory:root,LibrarySource:"Epic",ExternalId:"fixture",ReadOnly:true)).GetAwaiter().GetResult();Check(!denied.Ok&&denied.Message.Contains("identity"),"Non-Steam game impersonated a Steam identity.");
            string key=ArcCap.GameIdentity.Key(0,"Epic","fixture",root),profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","profiles",key+".json");
            var scan=safety.RunAsync(new("DiscoverGame",InstallDirectory:root,LibrarySource:"Epic",ExternalId:"fixture",ReadOnly:true)).GetAwaiter().GetResult();Check(scan.Ok&&!scan.Data.GetProperty("Validated").GetBoolean()&&!File.Exists(profile),"Read-only discovery trusted a stopped game or wrote a profile.");
        }finally{Directory.Delete(root,true);}
    });

    Test("Retired experiment and inspection actions are no longer accepted",()=>{
        foreach(string action in new[]{"CreateXe3Experiment","RunXe3Experiment","Observe","InspectGameTrial"}){var reply=safety.RunAsync(new(action,Confirmed:true,RiskConfirmed:true)).GetAwaiter().GetResult();Check(!reply.Ok&&reply.Message.StartsWith("Unknown action"),"Retired action still runs: "+action);}
    });
    Test("Separate test requires explicit risk consent and read-only mode forbids its writes",()=>{
        var refused=safety.RunAsync(new("TryDriverTrial",Confirmed:true)).GetAwaiter().GetResult();Check(!refused.Ok&&refused.Message.Contains("Risk acknowledgment"),"Risk consent bypassed.");
        refused=safety.RunAsync(new("TryDriverTrial",Confirmed:true,RiskConfirmed:true,ReadOnly:true)).GetAwaiter().GetResult();Check(!refused.Ok&&refused.Message.Contains("Read-only"),"Read-only test created files.");
    });
    Test("Game trials require separate consent and read-only requests cannot prepare them",()=>{
 foreach(string action in new[]{"PrepareGameTrial","ChangeGameTrial"}){var denied=safety.RunAsync(new(action,Confirmed:true)).GetAwaiter().GetResult();Check(!denied.Ok&&denied.Message.Contains("risk"),"Game trial risk consent was bypassed.");denied=safety.RunAsync(new(action,Confirmed:true,RiskConfirmed:true,ReadOnly:true)).GetAwaiter().GetResult();Check(!denied.Ok&&denied.Message.Contains("Read-only"),"Read-only game trial mutated files.");}
 });
    Test("Graphics statistics separate whole-PC RAM from shared disk files",()=>{var snapshot=safety.RunAsync(new("GraphicsStats",ReadOnly:true)).GetAwaiter().GetResult();Check(snapshot.Ok&&snapshot.Data.GetProperty("RamTotal").GetUInt64()>=snapshot.Data.GetProperty("RamAvailable").GetUInt64()&&snapshot.Data.GetProperty("DiskBytes").GetInt64()>=0,"Invalid memory/cache snapshot.");});
    Test("Worker refuses unknown operations without changes",()=>{var reply=safety.RunAsync(new("UnknownOperation")).GetAwaiter().GetResult();Check(!reply.Ok&&reply.Message.Contains("Unknown action"),"Unknown operation accepted.");});
    Test("Worker refuses all mutation kinds without explicit confirmation",()=>{foreach(string action in new[]{"Prepare","Change","Remove","Import","EnableOverride","RestoreOverride","SelectGraphics"}){var reply=safety.RunAsync(new(action)).GetAwaiter().GetResult();Check(!reply.Ok&&reply.Message.Contains("Explicit confirmation"),"Mutation accepted without confirmation: "+action);}});
    Test("Automatic discovery never guesses an executable for a stopped unknown title",()=>{
        string root=Path.Combine(Path.GetTempPath(),"ArcDiscovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{File.Copy(Environment.ProcessPath!,Path.Combine(root,"possible-game.exe"));var result=safety.RunAsync(new("DiscoverGame",SteamAppId:987654,InstallDirectory:root)).GetAwaiter().GetResult();Check(result.Ok&&!result.Data.GetProperty("Validated").GetBoolean()&&result.Data.GetProperty("Executable").ValueKind==JsonValueKind.Null,"A static executable filename was mistaken for a rendering process.");Check(!Directory.Exists(Path.Combine(root,"possible-game.exe.local")),"Discovery modified the game.");}finally{Directory.Delete(root,true);}
    });
    Test("Fresh worker safety scan blocks nested anti-cheat even for reviewed single-player",()=>{
        string root=Path.Combine(Path.GetTempPath(),"ArcSafetyWorker-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"bin","x64_dx12"));Directory.CreateDirectory(Path.Combine(root,"nested","EasyAntiCheat"));
        try {
            var reply=safety.RunAsync(new("Safety",SteamAppId:292030,InstallDirectory:root)).GetAwaiter().GetResult();Check(reply.Ok&&!reply.Data.GetProperty("Allowed").GetBoolean()&&reply.Data.GetProperty("Marker").GetString()=="EasyAntiCheat","Anti-cheat did not override profile eligibility.");
            string exe=Path.Combine(root,"bin","x64_dx12","witcher3.exe"),receipt=Path.Combine(root,"receipt.json");File.WriteAllText(exe,"temporary fixture");
            reply=safety.RunAsync(new("Prepare",exe,receipt,Confirmed:true)).GetAwaiter().GetResult();Check(!reply.Ok&&reply.Message.Contains("Anti-cheat indicator")&&!Directory.Exists(exe+".local")&&!File.Exists(receipt),"Direct worker request bypassed safety or wrote setup files.");
            reply=safety.RunAsync(new("Prepare",exe,receipt,Confirmed:true,RiskConfirmed:true)).GetAwaiter().GetResult();Check(!reply.Ok&&(reply.Message.Contains("Unsupported Witcher executable")||reply.Message.Contains("Driver compatibility review required"))&&!Directory.Exists(exe+".local")&&!File.Exists(receipt),"Risk consent should pass the warning but never bypass executable validation.");
            if(args.Length>=2) {
                var live=JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(args[1]))!;File.WriteAllText(receipt,JsonSerializer.Serialize(live with{Executable=exe,Folder=exe+".local"}));
                reply=safety.RunAsync(new("Change",ReceiptPath:receipt,MiB:1024,Confirmed:true)).GetAwaiter().GetResult();Check(!reply.Ok&&reply.Message.Contains("Anti-cheat indicator"),"Change bypassed the fresh safety scan.");
            }
        }finally{Directory.Delete(root,true);}
    });
    Test("Worker safety blocks an unavailable install",()=>{var reply=safety.RunAsync(new("Safety",SteamAppId:292030,InstallDirectory:Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")))).GetAwaiter().GetResult();Check(reply.Ok&&!reply.Data.GetProperty("Allowed").GetBoolean()&&!reply.Data.GetProperty("ScanComplete").GetBoolean(),"Missing install classified as safe.");});
    Test("Worker validates existing private set READ ONLY",()=>{
        if(args.Length<2)return;var reply=safety.RunAsync(new("Validate",ReceiptPath:Path.GetFullPath(args[1]))).GetAwaiter().GetResult();Check(reply.Ok,"Live receipt validation failed: "+reply.Message);
        var expected=JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(args[1]))!;Check(reply.Data.GetProperty("MiB").GetInt32()==expected.MiB,"Worker cap differs from validated receipt.");
    });
    Test("Worker requires automatic original-control observation even with patch consent",()=>{
        if(args.Length<2)return;string temporary=Path.Combine(Path.GetTempPath(),"ArcControlGate-"+Guid.NewGuid()+".json");
        try {
            var source=JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(args[1]))!;var hashes=new Dictionary<string,string>(source.Files);hashes["igd12um64xe2.dll"]="1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229";
            File.WriteAllText(temporary,JsonSerializer.Serialize(source with{MiB=512,Files=hashes,Status="prepared-runtime-unverified"}));
            var result=safety.RunAsync(new("Change",ReceiptPath:temporary,MiB:2048,Confirmed:true,RiskConfirmed:true)).GetAwaiter().GetResult();
            Check(!result.Ok&&(result.Message.Contains("checked automatically")||result.Message.Contains("Driver compatibility review required")),"User consent bypassed the original-loading gate.");
        }finally{File.Delete(temporary);}
    });
    Test("Original-control check never verifies a stopped copied executable or changes its receipt",()=>{
        if(args.Length<2)return;string root=Path.Combine(Path.GetTempPath(),"ArcControlStopped-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(root,"bin","x64_dx12"));
        try {
            var source=JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(args[1]))!;string exe=Path.Combine(root,"bin","x64_dx12","witcher3.exe");File.Copy(source.Executable,exe);
            var hashes=new Dictionary<string,string>(source.Files);hashes["igd12um64xe2.dll"]="1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229";
            string receipt=Path.Combine(root,"receipt.json"),before=JsonSerializer.Serialize(source with{Executable=exe,Folder=exe+".local",MiB=512,Files=hashes});File.WriteAllText(receipt,before);
            var result=safety.RunAsync(new("VerifyControl",ReceiptPath:receipt)).GetAwaiter().GetResult();Check((!result.Ok||!result.Data.GetProperty("Verified").GetBoolean())&&File.ReadAllText(receipt)==before,"Stopped/unrelated process falsely verified the original control.");
        }finally{Directory.Delete(root,true);}
    });
    Test("Worker inspects the current driver read-only and blocks versions without a reviewed definition",()=>{var reply=safety.RunAsync(new("Status")).GetAwaiter().GetResult();Check(reply.Ok,"Current driver could not be inspected.");if(reply.Data.GetProperty("Version").GetString()=="32.0.101.9034")Check(reply.Data.GetProperty("Original").GetBoolean(),"Reviewed driver file was not verified.");else Check(!reply.Data.GetProperty("Compatible").GetBoolean(),"An unknown version became game-compatible.");});
    Test("Worker refuses a missing recovery receipt",()=>{var reply=safety.RunAsync(new("Validate",ReceiptPath:Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"))).GetAwaiter().GetResult();Check(!reply.Ok,"Missing receipt accepted.");});
    Test("Game state recognizes the validated live cap and blocks duplicate preparation",()=>{
        if(args.Length<2)return;
        var validated=safety.RunAsync(new("Validate",ReceiptPath:Path.GetFullPath(args[1]))).GetAwaiter().GetResult();string exe=validated.Data.GetProperty("Executable").GetString()!;
        var reply=safety.RunAsync(new("GameState",exe,Path.GetFullPath(args[1]))).GetAwaiter().GetResult();var state=GameSetupState.FromReply(reply);
        Check(state.Configured&&state.Receipt?.MiB==validated.Data.GetProperty("MiB").GetInt32()&&!state.CanPrepare,"Existing setup misclassified as stock.");
        var missing=safety.RunAsync(new("GameState",exe,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"))).GetAwaiter().GetResult();var orphan=GameSetupState.FromReply(missing);
        Check(!orphan.Configured&&!orphan.CanPrepare&&orphan.Error!=null,"Untracked existing folder incorrectly allowed setup.");
    });
    Test("Selected-game observation reports a stopped game instead of unrelated processes",()=>{
        string missing=Path.Combine(Path.GetTempPath(),"ArcCacheMissingGame-"+Guid.NewGuid()+".exe");
        var reply=safety.RunAsync(new("ObserveGame",missing)).GetAwaiter().GetResult();Check(reply.Ok&&reply.Message.Contains("not running")&&!reply.Message.Contains("msedge"),"Stopped game observation is misleading.");
    });
}
Console.WriteLine($"Passed {passed} checks. Steam fixtures were temporary; live checks were read-only.");
if(args.Length>=3) {
    Test("SCSKiller bridge persists selection, streams both channels, propagates failure and cancels owned process",()=>{
        string root=Path.Combine(Path.GetTempPath(),"ArcBridgeTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string cli=Path.Combine(root,"scskiller.exe");File.Copy(args[2],cli);
            var bridge=new ScsKillerBridge(Path.Combine(root,"bridge.json"));bridge.SelectCli(cli);bridge=new ScsKillerBridge(Path.Combine(root,"bridge.json")).Load();
            var lines=new System.Collections.Concurrent.ConcurrentBag<string>();
            int code=bridge.RunAsync("status",292030,lines.Add,CancellationToken.None).GetAwaiter().GetResult();
            Check(code==0&&lines.Any(l=>l.Contains("fixture:status|steam:292030"))&&lines.Any(l=>l.Contains("intel=1")),"Wrong command or lost output.");
            Check(bridge.RunAsync("compile",2,lines.Add,CancellationToken.None).GetAwaiter().GetResult()==7,"Failure code suppressed.");
            using var cancel=new CancellationTokenSource(500);bool cancelled=false;
            try{bridge.RunAsync("status",1,lines.Add,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"Cancellation was not reported.");
        }finally{Directory.Delete(root,true);}
    });
    Console.WriteLine($"Passed {passed} total checks including bridge process lifecycle.");
}

if(args.Length>=4){Test("Worker progress streams before completion and malformed events do not break the result",()=>{
 var client=new SafetyClient(args[3]);var first=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);client.Progress=(value,message)=>first.TrySetResult(value);
 var result=client.RunAsync(new("Fixture"));Check(first.Task.Wait(5000)&&first.Task.Result==100&&!result.IsCompleted,"Progress was buffered until completion or not bounded.");Check(result.GetAwaiter().GetResult().Ok,"Malformed progress broke the worker result.");
});Console.WriteLine($"Passed {passed} total checks including live progress.");}
