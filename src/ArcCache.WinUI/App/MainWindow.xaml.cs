using ArcCache.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
namespace ArcCache;
public partial class MainWindow : Window {
    readonly AppStore store=new();
    DiagnosticLog? diagnostics;
    bool initializingDiagnostics;
    readonly SafetyClient safety=new(Path.Combine(AppContext.BaseDirectory,"ArcCache.SafetyEngine.exe"));
    IReadOnlyList<SteamGame> games=[];
    SteamGame? selected;
    ReceiptState? selectedReceipt;
    GameSetupState? setupState;
    bool fillingGraphics;
    string? successfulTrial,successfulTrialDriver;
    string? loadedCapabilityDriver;
    bool TrialMode=>selectedReceipt?.Trial==true||successfulTrial!=null&&(!driverOriginal||successfulTrialDriver is "igd12um64xe3.dll" or "igd12um64xeh.dll");
    readonly Dictionary<int,DriverLimitCapability> limitCapabilities=[];
    readonly Dictionary<int,DriverLimitCapability> trialLimitCapabilities=[];
    bool GameDriverReady=>selectedReceipt!=null&&selectedReceipt.Status!="removed"?(selectedReceipt.Trial?successfulTrial!=null:driverOriginal&&successfulTrialDriver is not ("igd12um64xe3.dll" or "igd12um64xeh.dll")):driverOriginal||successfulTrial!=null;
    GameGuide? gameGuide;
    int introductionIndex;
    bool introductionVisible;
    bool busy,driverOriginal,routingReady,receiptValid,recordValid;
    bool isAdministrator,routingTurnOffAvailable,routingTurnOffRestart,windowsManageOpen;string[] gamesUsingCopies=[];long driverTestsBytes=-1;readonly bool vanguardInstalled=AntiCheatHints.VanguardInstalled();
    const string VanguardNote="Riot Vanguard is installed on this PC. While game-specific settings are on, VALORANT, League of Legends and 2XKO refuse to start (\"Something unusual happened\"). Turn the setting off before playing them.";
    string? steamRoot;
    Process? cacheMonitor;
    bool safetyAllowed;
    ScsKillerBridge? scs;
    CancellationTokenSource? scsCancellation;
    ScsKillerInfo? scsInfo;bool scsInfoChecking,scsOptionsLoading;string? scsProgressGameKey;
    Dictionary<string,ScsKillerGameStatus> scsStatuses=new(StringComparer.OrdinalIgnoreCase);string? scsStatusPath;
    CacheFileMap? cacheFiles;
    CancellationTokenSource? driverTrialCancellation;
    int trialProgressPercent;
    string trialProgressStage="";
    readonly DispatcherTimer driverWatchTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly DispatcherTimer footerStatusTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    string driverIdentity="";bool checkingDriver,closing;
    string homeAdapter="Intel graphics",homeVersion="";
    GraphicsCheckState graphicsCheckState=GraphicsCheckState.NotRun;
    GraphicsDriverState graphicsDriverState=GraphicsDriverState.Unknown;
    GraphicsWindowsState graphicsWindowsState=GraphicsWindowsState.Unknown;
    GraphicsCopyTestState graphicsCopyTestState=GraphicsCopyTestState.NotRun;
    GraphicsPageState? graphicsPageState;
    GraphicsPageInputs? statePreviewInput=null;
    bool developerStatePreview=false;
#if DEBUG
    bool developerSizePreview;
    string? developerSizePreviewKey;
#endif
    int? previewSizeMiB;
    ulong installedRamBytes;
    bool developerMyGamesPreview=false;
    MyGamesPageInputs? myGamesPreviewInput=null;
    MyGamesPageState? myGamesPageState;
    MyGamesLoadResult gameLoadResult=MyGamesLoadResult.Unknown;
    LocalModBlocker? activeModBlocker;
    string? activeModBlockerGameKey;
    bool setupOperationRunning;
    int myGamesChangeStep;
    int selectedCacheMiB=512;
    bool chipChoiceConfirmed;
    readonly DispatcherTimer discoveryTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    // The game whose driver copy was confirmed loading; while it runs, polling only checks that it is still running.
    string? observedRunningExe; // the selected game's run the page has seen; null = not running (or not seen yet)
    const string GameClosedMessage="This game is not running. Start it through its launcher, leave it at the menu, and check again.";
    string discoveryMessage="";
    bool preview;
    readonly Dictionary<string,int> desiredLimits=new();
    bool updatingChoice, synchronizingSelection, backgroundCheck;
    string? activePageTag;
    string lastBackgroundError="", displayedArtwork="";
    int foregroundRuns, backgroundPolls, previewWatchSeconds;
    public MainWindow() {
        InitializeComponent();ExtendsContentIntoTitleBar=true;SetTitleBar(TitleArea);
        safety.Progress=(percent,message)=>DispatcherQueue.TryEnqueue(()=>TrialUpdate(percent,message));
        graphicsStatsTimer.Tick+=async (_,_)=>{if(!busy)await GraphicsStats();};
        footerStatusTimer.Tick+=(_,_)=>{if(!busy)FooterStatus.Text=string.Empty;footerStatusTimer.Stop();};
#if DEBUG
        string? stateArg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--preview-state=",StringComparison.Ordinal));
        if(stateArg is not null){preview=true;string key=stateArg["--preview-state=".Length..];var gameRow=MyGamesPageStates.StateTable.FirstOrDefault(r=>r.Key==key);if(gameRow.Key is not null){developerStatePreview=true;developerMyGamesPreview=true;myGamesPreviewInput=gameRow.Input;}else if(SizePickerPreviewStates.Contains(key)){developerStatePreview=true;developerSizePreview=true;developerSizePreviewKey=key;}else{developerStatePreview=true;var selectedRow=GraphicsPageState.StateTable.FirstOrDefault(r=>r.Key==key);if(selectedRow.Key is null)selectedRow=GraphicsPageState.StateTable.First(r=>r.Key=="check-failed");statePreviewInput=selectedRow.Input;}}
#endif
#if DEBUG
        AppWindow.Resize(stateArg is null?new Windows.Graphics.SizeInt32(1320,940):new Windows.Graphics.SizeInt32(1614,1007));
        Nav.SelectedItem=developerMyGamesPreview?LibraryNav:HomeNav;
        if(developerMyGamesPreview)HomeNav.UseSystemFocusVisuals=false;
#else
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320,940));
        Nav.SelectedItem=HomeNav;
#endif
        AppWindow.Closing+=(_,e)=>{if(busy)e.Cancel=true;else if(scsRunning&&!closeAfterScs){e.Cancel=true;DispatcherQueue.TryEnqueue(async()=>await CloseDuringScs());}else{closing=true;graphicsStatsTimer.Stop();discoveryTimer.Stop();driverWatchTimer.Stop();footerStatusTimer.Stop();overlayWatchTimer.Stop();StopCacheMonitor();}};
#if DEBUG
        if(stateArg is not null){Root.Loaded+=(_,_)=>{if(developerMyGamesPreview){string key=stateArg["--preview-state=".Length..];var row=MyGamesPageStates.StateTable.First(r=>r.Key==key);LoadMyGamesPreview(row.Input,row.Key);}else if(developerSizePreview)LoadSizePickerPreview(developerSizePreviewKey!);else LoadDeveloperStatePreview(statePreviewInput!,stateArg["--preview-state=".Length..]);};return;}
#endif
        diagnostics=new DiagnosticLog(store.Root,Environment.GetCommandLineArgs().Contains("--debug"));
        safety.Diagnostic=diagnostics.Write;initializingDiagnostics=true;DiagnosticToggle.IsOn=diagnostics.Enabled;initializingDiagnostics=false;
        diagnostics.Write("session-start",new{Version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),OS=Environment.OSVersion.ToString(),Runtime=Environment.Version.ToString(),Variant=typeof(MainWindow).Assembly.GetName().Name});
        Root.Loaded+=async (_,_)=>{await Run("Reading your game libraries…",LoadLibrary);if(!preview){driverWatchTimer.Start();if(new Introduction(store.Root).ShouldShow)ShowIntroduction();else if(Environment.GetCommandLineArgs().Contains("--open-graphics"))NavigateTo("Driver");}};
        driverWatchTimer.Tick+=async (_,_)=>await CheckDriverChanged();
        Activated+=async (_,e)=>{if(e.WindowActivationState!=WindowActivationState.Deactivated)await CheckDriverChanged();};
        discoveryTimer.Tick+=async (_,_)=>await PollGame();
        try{scs=new ScsKillerBridge(Path.Combine(store.Root,"scskiller-bridge.json")).Load();string bundled=Path.Combine(AppContext.BaseDirectory,"SCSKiller","cli","scskiller.exe");if(scs.CliPath==null&&File.Exists(bundled))scs.SelectCli(bundled);ScsPath.Text=scs.CliPath??"Choose the SCSKiller command-line program (scskiller.exe).";}catch(Exception e){Log("SCSKiller configuration could not be read: "+e.Message);scs=new ScsKillerBridge(Path.Combine(store.Root,"scskiller-bridge.json"));}
        scsStatusPath=Path.Combine(store.Root,"scskiller-status.json");scsStatuses=ScsPresentation.LoadStatuses(scsStatusPath);
        overlayWatchTimer.Tick+=async (_,_)=>await WatchForGames();LoadOverlaySettings();
        cacheFiles=new CacheFileMap(Path.Combine(store.Root,"cache-files.json"));sightings=new AntiCheatSightings(Path.Combine(store.Root,"anticheat-sightings.json"));hintCache=new AntiCheatHintCache(Path.Combine(store.Root,"anticheat-hints.json"));overrides=new AntiCheatOverrides(Path.Combine(store.Root,"anticheat-overrides.json"));ShowScsOptions();DispatcherQueue.TryEnqueue(async()=>await RefreshScsInfo());
    }
#if DEBUG
    void LoadDeveloperStatePreview(GraphicsPageInputs input,string requestedKey){
        string selectedKey=GraphicsPageState.StateTable.FirstOrDefault(r=>r.Key==requestedKey).Key;
        graphicsCheckState=input.Check;graphicsDriverState=input.Driver;graphicsWindowsState=input.Windows;graphicsCopyTestState=input.CopyTest;
        chipChoiceConfirmed=input.ChipSelectionComplete;
        homeAdapter=input.ChipName;homeVersion=input.DriverVersion;driverOriginal=input.Driver==GraphicsDriverState.Checked;
        successfulTrial=input.Driver==GraphicsDriverState.UncheckedTestable&&input.CopyTest==GraphicsCopyTestState.Passed?"debug-preview-driver-copy":null;
        successfulTrialDriver="igd12um64xe2.dll";routingReady=input.Windows==GraphicsWindowsState.On;
        string cacheSizes=input.AvailableSizes;
        fillingGraphics=true;try{var chips=input.MultipleChips?new[]{new GraphicsChipChoice("preview-arc-b580","Intel Arc B580","32.0.101.9150"),new GraphicsChipChoice("preview-xe-igpu","Intel Xe integrated graphics","32.0.101.9150")}:new[]{new GraphicsChipChoice("preview-chip",input.ChipName,input.DriverVersion)};GraphicsChoice.ItemsSource=chips;GraphicsChoice.SelectedItem=chips[0];}finally{fillingGraphics=false;}
        var fakeFiles=new[]{new TrialFileChoice("igd12um64xe2.dll", "preview/igd12um64xe2.dll"),new TrialFileChoice("igd12um64xe3.dll", "preview/igd12um64xe3.dll"),new TrialFileChoice("igd12um64xeh.dll", "preview/igd12um64xeh.dll")};fillingTrial=true;try{TrialFile.ItemsSource=fakeFiles;TrialFile.SelectedItem=fakeFiles[0];}finally{fillingTrial=false;}
        DriverDetailsSummary.Text=input.Driver==GraphicsDriverState.Checked
            ?"The exact installed driver is on the checked list. Files marked 'direct setup allowed' can prepare game copies."
            :input.CopyTest==GraphicsCopyTestState.Passed
                ?"The installed files below are unchanged and are not approved for direct game setup. The driver copy passed its check and is available for game-specific copies."
                :"Only a file marked 'direct setup allowed' can prepare a game copy. This preview does not inspect installed driver files.";
        DriverDetails.Text=$"PREVIEW DATA · {input.ChipName} · Driver {input.DriverVersion}\nigd12um64xe2.dll · direct setup allowed: {(input.Driver==GraphicsDriverState.Checked?"Yes":"No")}\nigd12um64xe3.dll · direct setup allowed: No\nigd12um64xeh.dll · direct setup allowed: No\nAvailable cache sizes for this state: {cacheSizes}";
        TrialLog.Text=input.CopyTest is GraphicsCopyTestState.Failed or GraphicsCopyTestState.Stopped
            ?"PREVIEW DATA\nThe driver-copy check did not pass. No files were changed."
            :string.Empty;
        TrialLogTitle.Text=string.IsNullOrWhiteSpace(TrialLog.Text)?"Test log · no entries yet":"Test log";
        TrialLog.Visibility=string.IsNullOrWhiteSpace(TrialLog.Text)?Visibility.Collapsed:Visibility.Visible;
        TrialProgress.Value=input.CopyTest==GraphicsCopyTestState.Passed?100:input.CopyTestProgress;
        TrialProgress.IsIndeterminate=input.CopyTest==GraphicsCopyTestState.Running;
        TrialStage.Text=input.CopyTest==GraphicsCopyTestState.Passed?"Done":input.CopyTest==GraphicsCopyTestState.Running?$"Step 2 of 3 · {input.CopyTestStage} · {input.CopyTestProgress}%":string.Empty;
        DriverCopyActionReason.Text="Preview only. No recovery files were created.";
        DeveloperPreviewNotice.IsOpen=true;HomeDeveloperPreviewNotice.IsOpen=true;
        HomeEmptyGames.Text="No games are loaded in this fake-data preview.";
        if(selectedKey is null){GlobalNotice.Title="Unknown preview state";GlobalNotice.Message=$"'{requestedKey}' is not in the Graphics state table. Showing the check-failed example.";GlobalNotice.IsOpen=true;}
        RenderGraphicsPage();RefreshHome();
        NavigateTo("Driver");
    }
    static readonly HashSet<string> SizePickerPreviewStates=new(StringComparer.OrdinalIgnoreCase){"size-untested","size-test-running","size-passed","size-locked","size-ram-warning-8","size-ram-warning-16"};
    void LoadSizePickerPreview(string key){
        var row=SizePickerPreviewStates.Contains(key)?key:"size-untested";int selectedSize=row switch{"size-test-running"=>8192,"size-passed"=>2048,"size-locked"=>4096,"size-ram-warning-8"=>8192,"size-ram-warning-16"=>16384,_=>2048};
        var buildable=new HashSet<int>{512,1024,2048,4096};if(row is not "size-locked") {buildable.Add(8192);buildable.Add(16384);}
        var tested=new HashSet<int>{512,1024};if(row=="size-passed")tested.Add(2048);
        limitCapabilities.Clear();foreach(int mib in new[]{512,1024,2048,4096,8192,16384}){bool can=buildable.Contains(mib),pass=tested.Contains(mib);string evidence=pass?"Passed its own test on this exact driver.":can?"Needs its own test on this exact driver.":"This driver's reload code couldn't be identified safely; 8/16 GB stay locked.";limitCapabilities[mib]=new(mib,pass,evidence,can,pass);}
        previewSizeMiB=selectedSize;installedRamBytes=16UL*1024*1024*1024;graphicsCheckState=GraphicsCheckState.Passed;graphicsDriverState=GraphicsDriverState.UncheckedTestable;graphicsWindowsState=GraphicsWindowsState.On;chipChoiceConfirmed=true;homeAdapter="Intel Arc B580";homeVersion="32.0.101.9150";driverOriginal=false;routingReady=true;
        var copy=row=="size-test-running"?GraphicsCopyTestState.Running:row=="size-passed"?GraphicsCopyTestState.Passed:GraphicsCopyTestState.NotRun;
        graphicsCopyTestState=copy;trialProgressPercent=copy==GraphicsCopyTestState.Running?57:0;trialProgressStage=copy==GraphicsCopyTestState.Running?"Checking the driver copy":"";
        successfulTrial=copy==GraphicsCopyTestState.Passed?"preview-size-proof":null;successfulTrialDriver="igd12um64xe2.dll";
        statePreviewInput=new(GraphicsCheckState.Passed,GraphicsDriverState.UncheckedTestable,GraphicsWindowsState.On,copy,ChipName:homeAdapter,DriverVersion:homeVersion,AvailableSizes:row=="size-passed"?"Default, 1 GB, 2 GB":"Default, 1 GB",ChipSelectionComplete:true,CopyTestProgress:trialProgressPercent,CopyTestStage:trialProgressStage);
        var fakeFiles=new[]{new TrialFileChoice("igd12um64xe2.dll","preview/igd12um64xe2.dll"),new TrialFileChoice("igd12um64xe3.dll","preview/igd12um64xe3.dll"),new TrialFileChoice("igd12um64xeh.dll","preview/igd12um64xeh.dll")};fillingTrial=true;try{TrialFile.ItemsSource=fakeFiles;TrialFile.SelectedItem=fakeFiles[0];}finally{fillingTrial=false;}
        fillingGraphics=true;try{var chip=new GraphicsChipChoice("preview-b580",homeAdapter,homeVersion);GraphicsChoice.ItemsSource=new[]{chip};GraphicsChoice.SelectedItem=chip;}finally{fillingGraphics=false;}
        DriverDetailsSummary.Text="Preview data only. No installed driver or recovery record was read.";DriverDetails.Text=$"PREVIEW DATA · {homeAdapter} · Driver {homeVersion}\nSelected size: {SetupPresentation.Limit(selectedSize)}\nThis screen does not inspect or change Windows files.";
        TrialLog.Text=copy==GraphicsCopyTestState.Running?"PREVIEW DATA\nThe selected cache-size test is running. No files are being changed.":copy==GraphicsCopyTestState.Passed?"PREVIEW DATA\nThis size passed its own isolated load test. Cache capacity has not been measured.":string.Empty;
        TrialLogTitle.Text=string.IsNullOrWhiteSpace(TrialLog.Text)?"Test log · no entries yet":"Test log";TrialLog.Visibility=string.IsNullOrWhiteSpace(TrialLog.Text)?Visibility.Collapsed:Visibility.Visible;
        DeveloperPreviewNotice.IsOpen=true;HomeDeveloperPreviewNotice.IsOpen=true;HomeEmptyGames.Text="No games are loaded in this fake-data preview.";HomeEmptyGames.Visibility=Visibility.Visible;
        TrialSizeChoice.SelectedItem=null;RenderGraphicsPage();NavigateTo("Driver");
    }
    void LoadMyGamesPreview(MyGamesPageInputs input,string requestedKey){
        driverOriginal=true;successfulTrial=null;successfulTrialDriver="igd12um64xe2.dll";routingReady=true;limitCapabilities.Clear();
        foreach(int mib in new[]{512,1024,2048,4096,8192,16384})limitCapabilities[mib]=new(mib,input.AvailableSizes.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Contains(CacheLabel(mib),StringComparer.OrdinalIgnoreCase),"Preview fixture");
        var fake=new SteamGame(292030,"Assetto Corsa Rally",Path.Combine(Path.GetTempPath(),"ArcCache Preview","Assetto Corsa Rally"),null,DetectedExecutable:Path.Combine(Path.GetTempPath(),"ArcCache Preview","Assetto Corsa Rally","rally.exe"),Detected:true,Source:"Steam",ExternalId:"preview",ManualExecutable:null);
        games=input.HasGames?[fake,new SteamGame(0,"A Very Long Example Game Title Used To Check Two Line Truncation",fake.InstallDirectory,null,DetectedExecutable:fake.SupportedExecutable,Detected:true,Source:"Epic",ExternalId:"preview-epic")]:[];
        selected=input.HasSelection?fake:null;myGamesPreviewInput=input;gameLoadResult=input.LoadResult;selectedCacheMiB=int.TryParse(input.CacheSize.Replace(" GB","",StringComparison.OrdinalIgnoreCase),out int parsed)?parsed*1024:512;
        if(requestedKey is "setup-running" or "size-apply-running")setupOperationRunning=true;else setupOperationRunning=false;
        if(selected is not null)desiredLimits[selected.Key]=input.ChosenDefault?512:selectedCacheMiB;
        GameStatusFilter.ItemsSource=StatusFilters;GameStatusFilter.SelectedIndex=0;
        SourceFilter.ItemsSource=new[]{"All libraries","Epic","Steam"};SourceFilter.SelectedItem="All libraries";
        Search.Text="";Nav.IsEnabled=true;Nav.SelectedItem=LibraryNav;activePageTag=null;NavigateTo("Library");
        GameTitle.Text=selected?.Name??"Choose a game";GameSource.Text=selected?.Source.ToUpperInvariant()+" LIBRARY"??"GAME LIBRARY";
        GameArtwork.Source=null;GamePlaceholder.Visibility=Visibility.Visible;displayedArtwork="";
        LibraryNotice.IsOpen=false;LibraryPanel.Visibility=Visibility.Visible;RenderMyGames();
    }
#endif
    // Read-only visual verification; off-screen so a running game never loses focus.
    public void StartPreview(string path,uint appId=0,int watchSeconds=0,bool simulateDriverUpdate=false) {
        previewWatchSeconds=Math.Clamp(watchSeconds,0,20);
        preview=true;
        AppWindow.IsShownInSwitchers=false;AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));AppWindow.Show(false);
        Root.Loaded+=async (_,_)=>{
            try {
                await Task.Delay(1500);while(busy)await Task.Delay(200);
                if(appId!=0){selected=games.FirstOrDefault(g=>g.AppId==appId);await Run("Showing selected game for read-only preview…",ShowGame);Filter();}
                int sourceAt=Array.IndexOf(Environment.GetCommandLineArgs(),"--preview-source");if(sourceAt>=0&&sourceAt+1<Environment.GetCommandLineArgs().Length){string source=Environment.GetCommandLineArgs()[sourceAt+1];await Run("Showing a library for read-only preview…",async()=>{SourceFilter.SelectedItem=source;selected=games.FirstOrDefault(g=>g.Source==source);Filter();await ShowGame();});}
                await Task.Delay(700);while(busy)await Task.Delay(200);Root.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,32,32,32));Root.UpdateLayout();
                if(simulateDriverUpdate){ShowDriverChange(driverIdentity+"-preview-fixture");File.WriteAllText(path+".driver-checks.json",JsonSerializer.Serialize(new{ApplyEnabled=ApplyButton.IsEnabled,ApplyLabel=ApplyButton.Content?.ToString(),LaunchEnabled=LaunchButton.IsEnabled,RecoveryEnabled=RestoreButton.IsEnabled,NoticeVisible=DriverUpdateNotice.IsOpen}));}
                if(Environment.GetCommandLineArgs().Contains("--preview-trial")){NavigateTo("Driver");await CheckTrialSupport();await Task.Delay(300);Root.UpdateLayout();File.WriteAllText(path+".trial-checks.json",JsonSerializer.Serialize(new{Eligible=trialEligible,State=graphicsPageState,Summary=TrialSummary.Text,Progress=TrialProgress.Value,ReadOnly=preview}));}
                if(Environment.GetCommandLineArgs().Contains("--preview-settings")){NavigateTo("Settings");Root.UpdateLayout();}
                if(Environment.GetCommandLineArgs().Contains("--preview-library")){await ScanAntiCheat();Nav.SelectedItem=LibraryNav;NavigateTo("Library");Root.UpdateLayout();}
                if(Environment.GetCommandLineArgs().Contains("--preview-welcome")){ShowIntroduction();if(Environment.GetCommandLineArgs().Contains("--preview-last")){introductionIndex=Introduction.Screens.Length-1;RenderIntroduction();}}
                File.WriteAllText(path+".libraries.json",JsonSerializer.Serialize(new{Count=games.Count,Sources=games.GroupBy(g=>g.Source).ToDictionary(g=>g.Key,g=>g.Count()),UniqueKeys=games.Select(g=>g.Key).Distinct().Count()==games.Count,ReadOnly=preview}));
                int runsBefore=foregroundRuns,pollsBefore=backgroundPolls;
                if(previewWatchSeconds>0)await Task.Delay(previewWatchSeconds*1000);
                if(previewWatchSeconds>0)File.WriteAllText(path+".checks.json",JsonSerializer.Serialize(new{ForegroundRunsBefore=runsBefore,ForegroundRunsAfter=foregroundRuns,BackgroundPolls=backgroundPolls-pollsBefore,NavigationEnabled=Nav.IsEnabled}));
                var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
                using var file=File.Create(Path.GetFullPath(path));var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,file.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();
            }catch(Exception e){Log("Preview failed: "+e);File.WriteAllText(path+".error.txt",e.ToString());}
            Close();
        };
    }
    void ShowIntroduction(){introductionIndex=0;introductionVisible=true;Nav.IsEnabled=false;WelcomePanel.Visibility=Visibility.Visible;RenderIntroduction();}
    void RenderIntroduction(){var screen=Introduction.Screens[introductionIndex];WelcomeTitle.Text=screen.Title;WelcomeBody.Text=screen.Body;WelcomeCount.Text=$"HOW IT WORKS · {introductionIndex+1} OF {Introduction.Screens.Length}";
        WelcomePoints.Children.Clear();bool numbered=screen.Points.All(point=>point.Length>1&&char.IsDigit(point[0]));foreach(string point in screen.Points)WelcomePoints.Children.Add(new TextBlock{Text=numbered?point:"•  "+point,TextWrapping=TextWrapping.Wrap,FontWeight=numbered?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal});
        WelcomePointsBox.Visibility=screen.Points.Length>0?Visibility.Visible:Visibility.Collapsed;WelcomeNote.Text=screen.Note;WelcomeNote.Visibility=screen.Note.Length>0?Visibility.Visible:Visibility.Collapsed;
        WelcomeBack.Visibility=introductionIndex>0?Visibility.Visible:Visibility.Collapsed;WelcomeNext.Content=introductionIndex==Introduction.Screens.Length-1?"Go to Arc Cache":"Next";WelcomeNext.Focus(FocusState.Programmatic);}
    async void FinishIntroduction(bool graphics){if(!preview)try{new Introduction(store.Root).Dismiss();}catch(Exception error){Log("Introduction preference could not be saved: "+error.Message);}introductionVisible=false;WelcomePanel.Visibility=Visibility.Collapsed;Nav.IsEnabled=!busy;if(graphics){NavigateTo("Driver");await CheckTrialSupport();}}
    void ReplayIntroduction(object sender,RoutedEventArgs e){if(!busy)ShowIntroduction();}
    async void ReviewDriverTestCleanup(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback()||busy)return;
        try{
            var reply=await Engine(new WorkerRequest("PlanDriverTestCleanup",ReadOnly:true));
            int count=reply.Data.GetProperty("Folders").GetArrayLength();long bytes=reply.Data.GetProperty("BytesToFree").GetInt64();int skipped=reply.Data.GetProperty("SkippedUnsafe").GetInt32();
            string message=count==0?"No safely removable old test folders were found.":$"Arc Cache can remove {count} old driver-test folder{(count==1?"":"s")} and free about {bytes/1073741824.0:0.0} GB. The newest passing tests for this driver and any test used by a game setup will be kept.";
            if(skipped>0)message+=$" {skipped} folder{(skipped==1?" was":"s were")} skipped because its ownership could not be verified.";
            if(count==0){await new ContentDialog{XamlRoot=Root.XamlRoot,Title="Driver tests",Content=message,CloseButtonText="Close",RequestedTheme=Root.RequestedTheme}.ShowAsync();return;}
            if(!await Confirm("Clean up old driver tests?",message+" Each selected folder will be checked again before removal. No game setup or shader cache will be changed.","Clean up"))return;
            await Run("Cleaning up old driver tests…",async()=>{var result=await Engine(new WorkerRequest("CleanupDriverTests",Confirmed:true));int removed=result.Data.GetProperty("RemovedFolders").GetArrayLength();long freed=result.Data.GetProperty("BytesFreed").GetInt64();int errors=result.Data.GetProperty("Errors").GetArrayLength();GlobalNotice.Severity=errors==0?InfoBarSeverity.Success:InfoBarSeverity.Warning;GlobalNotice.Title="Driver-test cleanup finished";GlobalNotice.Message=$"Removed {removed} folder{(removed==1?"":"s")}; freed {freed/1073741824.0:0.0} GB."+(errors==0?"":" Some folders were left in place because their contents changed.");GlobalNotice.IsOpen=true;});
            await MeasureDriverTests();
        }catch(Exception error){Log("Driver-test cleanup review failed: "+error.Message);GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Cleanup could not be reviewed";GlobalNotice.Message=PlainLanguage.Error(error.Message);GlobalNotice.IsOpen=true;}
    }
    async void ResetArcCache(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback()||busy||ScsBlocks("reset Arc Cache"))return;
        var panel=new StackPanel{Spacing=8};
        foreach(string line in new[]{"Every game is returned to default by removing only driver copies recorded by Arc Cache.","The saved Windows game-specific setting is restored. Restarting Windows may be needed.",driverTestsBytes>0?$"All driver tests are deleted ({driverTestsBytes/1073741824.0:0.0} GB in {DriverTestsFolder}).":"All driver tests are deleted.","Saved setups, profiles, reports, backups, icons, recent games, manual games and old logs are deleted. The current session log is kept.","Arc Cache starts fresh. This can't be undone.","If a game is running or a game folder contains changed or unfamiliar files, reset stops before restoring Windows or deleting Arc Cache data; it shows the folder that needs attention."})panel.Children.Add(new TextBlock{Text="• "+line,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="Type RESET to continue",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var input=new TextBox{PlaceholderText="RESET",MaxLength=5,Width=220};panel.Children.Add(input);
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=new TextBlock{Text="Reset everything?",Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,255,105,105)),FontSize=22,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold},Content=panel,PrimaryButtonText="Reset everything",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,IsPrimaryButtonEnabled=false,RequestedTheme=Root.RequestedTheme};
        input.TextChanged+=(_,_)=>dialog.IsPrimaryButtonEnabled=string.Equals(input.Text,"RESET",StringComparison.Ordinal);
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        string[] blocked=[];
        await Run("Resetting Arc Cache…",async()=>{var result=await Engine(new WorkerRequest("ResetArcCache",Confirmed:true,CurrentLogPath:store.LogPath,Confirmation:input.Text));bool completed=result.Data.GetProperty("Completed").GetBoolean();long freed=result.Data.GetProperty("BytesFreed").GetInt64();bool restart=result.Data.GetProperty("RestartRequired").GetBoolean();string detail=result.Data.GetProperty("Message").GetString()??result.Message;blocked=result.Data.GetProperty("BlockedFolders").EnumerateArray().Select(x=>x.GetString()).Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();GlobalNotice.Severity=completed?InfoBarSeverity.Success:InfoBarSeverity.Warning;GlobalNotice.Title=completed?"Arc Cache reset":blocked.Length>0?"A game folder needs attention":"Reset could not finish";GlobalNotice.Message=completed?$"Removed Arc Cache data and freed about {freed/1073741824.0:0.0} GB. "+(restart?"Restart Windows to finish restoring the previous setting.":"No restart is needed."):detail;GlobalNotice.IsOpen=true;});
        if(blocked.Length>0){var list=new StackPanel{Spacing=10};list.Children.Add(new TextBlock{Text="Arc Cache stopped before restoring Windows or deleting its saved data. Check this folder:",TextWrapping=TextWrapping.Wrap});foreach(string path in blocked){list.Children.Add(new TextBlock{Text=path,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true});var open=new Button{Content="Open folder",HorizontalAlignment=HorizontalAlignment.Left};open.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={path}});}catch(Exception error){Log("Could not open blocked game folder: "+error.Message);}};list.Children.Add(open);}await new ContentDialog{XamlRoot=Root.XamlRoot,Title="Reset paused",Content=list,CloseButtonText="Close",RequestedTheme=Root.RequestedTheme}.ShowAsync();}
    }
    void IntroductionBack(object sender,RoutedEventArgs e){if(introductionIndex>0){introductionIndex--;RenderIntroduction();}}
    void IntroductionNext(object sender,RoutedEventArgs e){if(introductionIndex<Introduction.Screens.Length-1){introductionIndex++;RenderIntroduction();}else FinishIntroduction(false);}
    void IntroductionSkip(object sender,RoutedEventArgs e)=>FinishIntroduction(false);
    void SelectSize(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback()||sender is not ButtonBase button||busy)return;CapChoice.SelectedItem=CapChoice.Items.OfType<ComboBoxItem>().First(i=>Equals(i.Tag?.ToString(),button.Tag?.ToString()));UpdateButtons();RenderMyGames();}
    void DiagnosticToggled(object sender,RoutedEventArgs e) {
        if(initializingDiagnostics||diagnostics==null)return;
        try{diagnostics.SetEnabled(DiagnosticToggle.IsOn);Log(diagnostics.Enabled?"Detailed diagnostics enabled. Files stay local; review them before sharing.":"Detailed diagnostics disabled. Existing logs are retained.");}
        catch(Exception error){Log("Diagnostic setting could not be saved: "+error.Message);initializingDiagnostics=true;DiagnosticToggle.IsOn=diagnostics.Enabled;initializingDiagnostics=false;}
    }
    void OpenLogs(object sender,RoutedEventArgs e) {
        try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={System.IO.Path.GetDirectoryName(store.LogPath)!}});}catch(Exception error){Log("Could not open logs: "+error.Message);}
    }
    // The Activity page shows the newest lines only (a long SCSKiller compile writes thousands); the session file keeps all.
    const int ActivityLines=500;readonly Queue<string> activityLines=new();
    void Log(string text){store.Log(text);foreach(string line in text.Split('\n')){activityLines.Enqueue(DateTime.Now.ToString("HH:mm:ss ")+line.TrimEnd('\r'));while(activityLines.Count>ActivityLines)activityLines.Dequeue();}ActivityLog.Text=string.Join(Environment.NewLine,activityLines)+Environment.NewLine;}
    public void ShowUnhandledFailure(Exception error){GlobalNotice.Severity=InfoBarSeverity.Error;GlobalNotice.Title="Arc Cache recovered from an error";GlobalNotice.Message="The error was saved in Activity logs. "+PlainLanguage.Error(error.Message);GlobalNotice.IsOpen=true;}
    async Task Run(string label,Func<Task> action,bool showStatus=true) {
        if(busy)return;if(showStatus){footerStatusTimer.Stop();FooterStatus.Text=label;}foregroundRuns++;busy=true;Nav.IsEnabled=false;GlobalNotice.IsOpen=false;BusyRing.Visibility=Visibility.Visible;BusyRing.IsActive=true;Log(label);
        UpdateButtons();RenderMyGames();
        try{await action();if(showStatus)FooterStatus.Text="Complete · "+label.Trim().TrimEnd('…','.');}
        catch(Exception e){diagnostics?.Write("action-error",new{Action=label,Error=e.ToString()});var blocker=BlockerFromError(e.Message,selected?.SupportedExecutable);if(blocker is not null&&selected is not null){activeModBlocker=blocker;activeModBlockerGameKey=selected.Key;discoveryTimer.Stop();Log("Game setup blocked by another mod: "+blocker.Path);if(showStatus)FooterStatus.Text="Blocked by another mod";GlobalNotice.IsOpen=false;}else{Log("Stopped: "+e.Message);if(showStatus)FooterStatus.Text="Stopped · no further changes attempted";GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Could not finish this step";GlobalNotice.Message=PlainLanguage.Error(e.Message);GlobalNotice.IsOpen=true;}}
        finally{busy=false;setupOperationRunning=false;Nav.IsEnabled=!introductionVisible;BusyRing.IsActive=false;BusyRing.Visibility=Visibility.Collapsed;UpdateButtons();if(LibraryPanel?.Visibility==Visibility.Visible)RenderMyGames();if(DriverPanel?.Visibility==Visibility.Visible)RenderGraphicsPage();if(showStatus)footerStatusTimer.Start();}
    }
    bool PreviewOnlyFeedback(){if(!developerStatePreview)return false;footerStatusTimer.Stop();FooterStatus.Text="Preview only · this button makes no changes.";footerStatusTimer.Start();return true;}
    async Task<WorkerReply> Engine(WorkerRequest request,CancellationToken cancellationToken=default) {
        if(preview)request=request with{ReadOnly=true};
        WorkerReply reply=await safety.RunAsync(request,cancellationToken);Log(reply.Message);if(!reply.Ok)throw new InvalidOperationException(reply.Message);return reply;
    }
    async Task LoadLibrary() {
        string? previousId=selected?.Key;var previousGames=games.ToDictionary(g=>g.Key);
        LibraryResult result=await Task.Run(()=>GameLibraries.Discover(steamRoot,store.Root));games=result.Games.Select(g=>previousGames.TryGetValue(g.Key,out var old)?g with{Detected=old.Detected,DetectedExecutable=old.DetectedExecutable}:g).ToArray();steamRoot=result.SteamRoot;
        LibraryCount.Text=$"{games.Count} installed games · local discovery";
        LibraryNotice.IsOpen=result.Warnings.Count>0;LibraryNotice.Title="Library discovery";LibraryNotice.Message=string.Join(Environment.NewLine,result.Warnings.Take(3));
        var source=SourceFilter.SelectedItem as string??"All libraries";SourceFilter.ItemsSource=new[]{"All libraries"}.Concat(games.Select(g=>g.Source).Distinct().OrderBy(x=>x)).ToArray();SourceFilter.SelectedItem=SourceFilter.Items.Contains(source)?source:"All libraries";
        if(GameStatusFilter.Items.Count==0){GameStatusFilter.ItemsSource=StatusFilters;GameStatusFilter.SelectedIndex=0;}
        Filter();_=ScanAntiCheat();await LoadDriver();
        var visible=GamesList.Items.OfType<SteamGame>().ToArray();SteamGame? next=previousId is null?visible.FirstOrDefault(g=>g.AppId==292030)??visible.FirstOrDefault():visible.FirstOrDefault(g=>g.Key==previousId);
        if(previousId is not null&&next is null&&previousGames.TryGetValue(previousId,out var missing))next=missing;
        GamesList.SelectedItem=visible.FirstOrDefault(g=>g.Key==next?.Key);selected=next;await ShowGame();RefreshHome();
    }
    static readonly string[] StatusFilters=["All games","Set up","Needs attention","No anti-cheat found","Anti-cheat"];
    readonly Dictionary<string,AntiCheatHint> antiCheatHints=new();
    AntiCheatSightings? sightings;AntiCheatHintCache? hintCache;AntiCheatOverrides? overrides;string? acSessionKey;DateTime acSessionStart,lastAcSample;bool acSessionSeen;
    static readonly TimeSpan CleanRunNeeded=TimeSpan.FromSeconds(30);
    // Watches the selected game while it runs: an anti-cheat process next to it blocks setup (or, with a driver copy,
    // asks for default settings); 30 seconds without one clear an earlier sighting, e.g. Halo MCC without anti-cheat.
    async Task SampleAntiCheat(SteamGame game){
        if(preview||sightings is null||DateTime.UtcNow-lastAcSample<TimeSpan.FromSeconds(4))return;lastAcSample=DateTime.UtcNow;
        var (running,name)=await Task.Run(()=>(AntiCheatRuntime.AnyProcessUnder(game.InstallDirectory),AntiCheatRuntime.RunningName()));
        if(!running){if(acSessionKey==game.Key)acSessionKey=null;return;}
        var now=DateTime.UtcNow;if(acSessionKey!=game.Key){acSessionKey=game.Key;acSessionStart=now;acSessionSeen=false;}
        if(name is not null){
            if(acSessionSeen)return;acSessionSeen=true;
            bool withCopy=selected?.Key==game.Key&&selectedReceipt is not null&&selectedReceipt.Status!="removed";
            sightings.Record(game.Key,new AntiCheatSighting(name,now,withCopy));
            Log(withCopy?$"{name} is running while {game.Name} uses its driver copy. Close the game and choose Use default settings.":$"{name} is running with {game.Name}. Arc Cache won't set this game up while an anti-cheat runs.");
            if(withCopy&&overrides?.Get(game.Key) is null){GlobalNotice.Severity=InfoBarSeverity.Error;GlobalNotice.Title=$"{name} is running with {game.Name}'s driver copy";GlobalNotice.Message="The anti-cheat can see the changed Intel driver file. Close the game and choose Use default settings for it.";GlobalNotice.IsOpen=true;}
            Filter();RenderMyGames();
        }else if(!acSessionSeen&&now-acSessionStart>=CleanRunNeeded&&sightings.Get(game.Key) is { } old&&!old.WithDriverCopy){
            sightings.Clear(game.Key);Log($"{game.Name} ran for 30 seconds without {old.Name}. Setup is available again.");Filter();RenderMyGames();
        }
    }

    // A quick, bounded look into each game folder so the list can mark anti-cheat games before one is opened.
    async Task ScanAntiCheat(){
        if(preview&&developerMyGamesPreview||hintCache is null)return;
        var pending=games.Where(g=>!antiCheatHints.ContainsKey(g.Key)).Select(g=>(g.Key,g.InstallDirectory)).ToArray();if(pending.Length==0)return;
        // Cached results (folder unchanged since the last scan) show at once; only new or changed folders are walked.
        var stamps=await Task.Run(()=>pending.Select(g=>(g.Key,g.InstallDirectory,Stamp:AntiCheatHints.Stamp(g.InstallDirectory))).ToArray());
        var toScan=new List<(string Key,string InstallDirectory,long Stamp)>();
        foreach(var g in stamps){if(hintCache.Get(g.Key,g.Stamp) is { } cached)antiCheatHints[g.Key]=cached;else toScan.Add(g);}
        Filter();if(toScan.Count==0)return;
        int done=0;var progress=new Progress<int>(n=>{if(n>=toScan.Count||activePageTag!="Library")return;LibraryCount.Text=$"{GamesList.Items.Count} of {games.Count} games · checking for anti-cheat {n}/{toScan.Count}";});
        var found=await Task.Run(()=>{var results=new System.Collections.Concurrent.ConcurrentBag<(string Key,AntiCheatHint Hint,long Stamp)>();IProgress<int> report=progress;Parallel.ForEach(toScan,new ParallelOptions{MaxDegreeOfParallelism=4},game=>{AntiCheatHint hint;try{hint=AntiCheatHints.Scan(game.InstallDirectory);}catch(Exception){hint=AntiCheatHint.NotChecked;}results.Add((game.Key,hint,game.Stamp));report.Report(Interlocked.Increment(ref done));});return results.ToArray();});
        foreach(var r in found)antiCheatHints[r.Key]=r.Hint;
        await Task.Run(()=>hintCache.Put(found));
        Filter();if(selected is not null)RenderMyGames();
    }
    // The user may go ahead with an anti-cheat game, but only after reading what can happen and confirming it twice.
    async void AcceptAntiCheatRisk(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback()||busy||selected is null||overrides is null)return;var game=selected;
        var seen=sightings?.Get(game.Key);string name=seen?.Name??(antiCheatHints.TryGetValue(game.Key,out var hint)&&hint.Level is AntiCheatLevel.AlwaysOn or AntiCheatLevel.Optional?hint.Name:"The anti-cheat");
        var panel=new StackPanel{Spacing=8};
        panel.Children.Add(new TextBlock{Text=$"{name} checks every file that is loaded into {game.Name}. With Arc Cache, one of them is a changed Intel driver file whose signature is no longer valid. To an anti-cheat, that looks like a cheat hooking the renderer.",TextWrapping=TextWrapping.Wrap});
        foreach(string line in new[]{
            "The game may refuse to start, crash, or kick you from online sessions.",
            "Your account can be banned. The game's developer decides, often days or weeks later, and bans can be permanent. Some anti-cheats ban the whole PC (hardware ban), so a new account doesn't help.",
            "Single-player, offline or practice modes don't protect you while the anti-cheat runs.",
            "Neither Arc Cache nor its developers can undo or appeal a ban.",
            "Use default settings removes the driver copy, but it can't take back anything the anti-cheat already reported."})
            panel.Children.Add(new TextBlock{Text="• "+line,TextWrapping=TextWrapping.Wrap});
        var check=new CheckBox{Content=new TextBlock{Text="I understand that my account can be banned because of this, and that it can't be undone.",TextWrapping=TextWrapping.Wrap}};panel.Children.Add(check);
        panel.Children.Add(new TextBlock{Text="Type BAN RISK to continue",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        var input=new TextBox{PlaceholderText="BAN RISK",MaxLength=8,Width=220,HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(input);
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=new TextBlock{Text=$"Use Arc Cache with {game.Name} anyway?",Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,255,105,105)),FontSize=22,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap},Content=new ScrollViewer{Content=panel,MaxHeight=460},PrimaryButtonText="Accept the risk",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,IsPrimaryButtonEnabled=false,RequestedTheme=Root.RequestedTheme};
        void Update(){dialog.IsPrimaryButtonEnabled=check.IsChecked==true&&string.Equals(input.Text.Trim(),"BAN RISK",StringComparison.Ordinal);}
        check.Checked+=(_,_)=>Update();check.Unchecked+=(_,_)=>Update();input.TextChanged+=(_,_)=>Update();
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        overrides.Set(game.Key);Log($"Anti-cheat risk accepted for {game.Name} ({name}). Arc Cache now allows its setup; the account can be banned.");
        if(selected?.Key==game.Key){if(!game.Supported)discoveryTimer.Start();RenderMyGames();}
    }
    void RevokeAntiCheatRisk(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback()||selected is null||overrides is null)return;overrides.Clear(selected.Key);
        Log($"Anti-cheat risk no longer accepted for {selected.Name}."+(selectedReceipt is not null&&selectedReceipt.Status!="removed"?" Choose Use default settings to remove its driver copy.":""));RenderMyGames();
    }
    string ListBadge(SteamGame game)=>GameSummary(game).Badge;
    /// <summary>The one short description of a game used by its list badge and Home's recent games. The selected game uses
    /// its page state (the same words as its header); others only have their saved setup record to go by.
    /// The anti-cheat pill already shows anti-cheat, so the badge doesn't repeat it.</summary>
    (string Badge,string Line) GameSummary(SteamGame game){
        if(selected?.Key==game.Key&&myGamesPageState is {} state)return (state.ListBadge=="Anti-cheat"?"":state.ListBadge,state.HeaderStatus);
        if(developerMyGamesPreview)return ("","");
        string path=store.Receipt(game);
        if(!File.Exists(path))return ("",game.Supported?"Not set up":"Game not detected yet");
        try{
            using var doc=JsonDocument.Parse(File.ReadAllText(path));var root=doc.RootElement;
            if(root.TryGetProperty("Status",out var status)&&status.GetString()=="removed")return ("",game.Supported?"Not set up":"Game not detected yet");
            // The setup record keeps whether the game's last check saw its driver copy load: verified = Ready, same words
            // as the game's header. Opening the game re-checks its files and can still turn this into Needs attention.
            int size=root.TryGetProperty("MiB",out var mib)&&mib.TryGetInt32(out int m)?m:0;
            string label=size<=512?"Default":SetupPresentation.Limit(size);
            string recorded=root.TryGetProperty("Status",out var st)?st.GetString()??"":"";
            bool verified=size>0&&recorded==(size<=512?"original-control-loading-verified":"size-loading-verified");
            return verified?("Ready","Ready to play · "+label):("Set up",size>0?"Set up · "+label:"Set up");
        }catch(Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException){return ("Needs attention","Setup needs attention");}
    }
    // Filter rebuilds the list to refresh badges after every selection. A new ItemsSource scrolls a ListView to the top,
    // so when the same games are listed in the same order, the scroll position is put back.
    static T? FindDescendant<T>(DependencyObject root) where T:DependencyObject{
        for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);i++){
            var child=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root,i);
            if(child is T hit)return hit;if(FindDescendant<T>(child) is T deeper)return deeper;
        }
        return null;
    }
    void Filter(){
        synchronizingSelection=true;try{
            var scroll=FindDescendant<ScrollViewer>(GamesList);double offset=scroll?.VerticalOffset??0;
            var shownBefore=(GamesList.ItemsSource as IEnumerable<SteamGame>)?.Select(g=>g.Key).ToArray();
            string? id=selected?.Key;string query=Search.Text??"";string? source=SourceFilter.SelectedItem as string;string? status=GameStatusFilter.SelectedItem as string;
            var visible=games.Where(g=>g.Name.Contains(query,StringComparison.OrdinalIgnoreCase)&&(source is null||source=="All libraries"||g.Source==source))
                .Select(g=>g with{SetupBadge=ListBadge(g),AntiCheat=sightings?.Get(g.Key) is { } seen?AntiCheatHint.Seen(seen):antiCheatHints.TryGetValue(g.Key,out var hint)?hint:AntiCheatHint.NotChecked})
                .Where(g=>status is null||status=="All games"||status=="No anti-cheat found"&&g.AntiCheat.Level==AntiCheatLevel.NoneFound||status=="Anti-cheat"&&g.AntiCheat.Level is AntiCheatLevel.AlwaysOn or AntiCheatLevel.Optional||status=="Set up"&&g.HasSetupBadge&&g.SetupBadge!="Needs attention"&&g.SetupBadge!="Blocked"||status=="Needs attention"&&(g.SetupBadge=="Needs attention"||g.SetupBadge=="Blocked")).ToArray();
            GamesList.ItemsSource=visible;if(id!=null)GamesList.SelectedItem=visible.FirstOrDefault(g=>g.Key==id);
            if(scroll!=null&&offset>0&&shownBefore!=null&&shownBefore.SequenceEqual(visible.Select(g=>g.Key))){
                GamesList.UpdateLayout();scroll.ChangeView(null,offset,null,true);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>scroll.ChangeView(null,offset,null,true)); // after virtualized items are measured
            }
            LibraryCount.Text=$"{visible.Length} of {games.Count} games";LibraryEmptyState.Visibility=Visibility.Collapsed;SearchEmptyState.Visibility=games.Count>0&&visible.Length==0?Visibility.Visible:Visibility.Collapsed;GamesList.Visibility=visible.Length==0?Visibility.Collapsed:Visibility.Visible;
        }finally{synchronizingSelection=false;}
    }
    // Filtering only changes what the list shows, so it works while a check runs in the background.
    void StatusFilterChanged(object sender,SelectionChangedEventArgs e){if(GameStatusFilter is null||GamesList is null)return;Filter();}
    void FillGraphics(JsonElement data) {
        fillingGraphics=true;try{
            var choices=data.GetProperty("Adapters").Deserialize<GraphicsChipChoice[]>()??[];GraphicsChoice.ItemsSource=choices;
            string? id=data.GetProperty("AdapterId").GetString();GraphicsChoice.SelectedItem=choices.FirstOrDefault(c=>c.Id==id);
        }finally{fillingGraphics=false;}
    }
    async void GraphicsChanged(object sender,SelectionChangedEventArgs e) {
        if(fillingGraphics||busy||GraphicsChoice.SelectedItem is not GraphicsChipChoice)return;
        if(PreviewOnlyFeedback())return;
        chipChoiceConfirmed=false;RenderGraphicsPage();
    }
    async Task LoadDriver(bool detailed=false) {
        graphicsCheckState=GraphicsCheckState.Running;graphicsDriverState=GraphicsDriverState.Unknown;graphicsWindowsState=GraphicsWindowsState.Unknown;graphicsCopyTestState=GraphicsCopyTestState.NotRun;RenderGraphicsPage();
        driverOriginal=false;successfulTrial=null;routingReady=false;
        try {
            var reply=await Engine(new(detailed?"CheckDriverCompatibility":"Status"));var d=reply.Data;
            driverOriginal=d.GetProperty("Compatible").GetBoolean();successfulTrial=d.GetProperty("SuccessfulTrial").GetString();successfulTrialDriver=d.GetProperty("SuccessfulTrialDriver").GetString();loadedCapabilityDriver=d.GetProperty("CapabilityDriver").GetString();driverIdentity=d.GetProperty("IdentityTag").GetString()??"";
            limitCapabilities.Clear();foreach(var capability in d.GetProperty("LimitCapabilities").EnumerateArray()){var parsed=capability.Deserialize<DriverLimitCapability>();if(parsed!=null)limitCapabilities[parsed.MiB]=parsed;}
            trialLimitCapabilities.Clear();foreach(var capability in d.GetProperty("TrialCapabilities").EnumerateArray()){var parsed=capability.Deserialize<DriverLimitCapability>();if(parsed!=null)trialLimitCapabilities[parsed.MiB]=parsed;}
            trialEligible=d.GetProperty("TrialInspection").ValueKind==JsonValueKind.Object&&d.GetProperty("TrialInspection").GetProperty("Eligible").GetBoolean();
            bool routingEnabled=d.GetProperty("DeveloperOverride").GetBoolean();bool routingReboot=d.GetProperty("RebootRequired").GetBoolean();
            routingReady=routingEnabled&&!routingReboot;
            if(d.TryGetProperty("SmartAppControl",out var sac)&&sac.ValueKind==JsonValueKind.Number&&sac.GetInt32()==1&&!smartAppControlNoted){smartAppControlNoted=true;Log("Smart App Control is on. Windows may block the changed Intel driver file in a game's driver copy; this hasn't been tested. If a set-up game fails to start, use default settings for it.");}
            EnableRouteButton.IsEnabled=(driverOriginal||successfulTrial!=null||!string.IsNullOrWhiteSpace(d.GetProperty("Version").GetString()))&&!routingEnabled&&!routingReboot;ApplyRoutingFacts(d);
            homeAdapter=d.GetProperty("Adapter").GetString()??"Intel graphics";homeVersion=d.GetProperty("Version").GetString()??"";
            // FillTrial resets trialEligible while it rebuilds the file list. Keep the Status
            // inspection result when it describes the selected file; otherwise inspect again.
            // Without this, the test button stayed disabled once any size had passed.
            bool statusTrialEligible=trialEligible;FillGraphics(d);FillTrial(d);
            trialEligible=statusTrialEligible&&TrialFile.SelectedItem is TrialFileChoice statusTrialChoice&&String.Equals(statusTrialChoice.Name,loadedCapabilityDriver,StringComparison.OrdinalIgnoreCase);
            if(!driverOriginal&&!trialEligible&&TrialFile.SelectedItem is TrialFileChoice)await InspectTrial();
            var adapterChoices=d.GetProperty("Adapters").Deserialize<GraphicsChipChoice[]>()??[];
            graphicsCheckState=adapterChoices.Length==0?GraphicsCheckState.NoIntel:GraphicsCheckState.Passed;
            graphicsWindowsState=routingReboot?GraphicsWindowsState.RestartRequired:routingEnabled?GraphicsWindowsState.On:GraphicsWindowsState.Off;
            graphicsDriverState=driverOriginal?GraphicsDriverState.Checked:successfulTrial!=null?GraphicsDriverState.UncheckedTestable:TrialFile.SelectedItem is TrialFileChoice?(trialEligible?GraphicsDriverState.UncheckedTestable:GraphicsDriverState.Unsupported):GraphicsDriverState.Unknown;
            graphicsCopyTestState=successfulTrial!=null?GraphicsCopyTestState.Passed:GraphicsCopyTestState.NotRun;
            DriverSummary.Text=$"{d.GetProperty("Adapter").GetString()} · Driver {d.GetProperty("Version").GetString()}\n{CapabilitySummary()}";
            var details=new List<string>{"Package: "+d.GetProperty("DriverFolder").GetString()};
            DriverDetailsSummary.Text=successfulTrial!=null
                ?"The installed files below are unchanged and are not approved for direct game setup. The driver copy passed its check and is available for game-specific copies."
                :"Only an installed file marked 'direct setup allowed' can be used to prepare a game copy. A separate tested driver copy is checked independently.";
            foreach(var f in d.GetProperty("Files").EnumerateArray())details.Add($"{f.GetProperty("Name").GetString()} · {f.GetProperty("State").GetString()}\nSHA-256: {f.GetProperty("Hash").GetString()}\nConstructor matches: {f.GetProperty("CandidateCount").GetInt32()} · direct setup allowed: {(f.GetProperty("PatchEnabled").GetBoolean()?"Yes":"No")}\n{f.GetProperty("Signature").GetString()}");
            DriverDetails.Text=string.Join("\n\n",details);
            RoutingHint.Text=routingReboot?"Restart Windows to finish. The setting is already enabled; no more changes are needed.":driverOriginal||successfulTrial!=null?"This setting is ready to use when you set up a game. You can restore the previous Windows setting here after removing game-specific copies.":"Check graphics support first. This setting is only needed when you set up a game-specific copy.";
            bool changedSinceLastReview=false;
            string baseline=Path.Combine(store.Root,"last-qualified-driver.json");
            if(File.Exists(baseline)){try{if(new FileInfo(baseline).Length>65536)throw new InvalidDataException("Previous review is too large.");using var old=JsonDocument.Parse(File.ReadAllText(baseline));changedSinceLastReview=old.RootElement.GetProperty("IdentityTag").GetString()!=driverIdentity;}catch(Exception ex){Log("Previous driver review unavailable: "+ex.Message);}}
            if(!preview){try{
                if(driverOriginal)File.WriteAllText(baseline,JsonSerializer.Serialize(new{IdentityTag=driverIdentity,Version=d.GetProperty("Version").GetString(),CheckedUtc=DateTime.UtcNow}));
                if(detailed){string reports=Path.Combine(store.Root,"driver-reports");Directory.CreateDirectory(reports);string report=Path.Combine(reports,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");File.WriteAllText(report,JsonSerializer.Serialize(d,new JsonSerializerOptions{WriteIndented=true}));Log("Read-only compatibility report: "+report);}
            }catch(Exception ex){Log("Driver review could not be saved: "+ex.Message);}}
            DriverUpdateNotice.IsOpen=!driverOriginal&&successfulTrial==null;DriverUpdateNotice.Title=driverOriginal?"Driver checked":changedSinceLastReview?"Your graphics driver has changed":"Check your graphics driver before continuing";
            DriverUpdateNotice.Message=driverOriginal?"The installed files match a supported version. Your game setup can be checked against this version.":"Your saved setup may use an older driver. Check support before using it again, or return the game to default settings. Its shader cache will not be deleted.";
            RenderGraphicsPage();
            if(selected!=null)RenderSetup();UpdateButtons();RenderMyGames();RefreshHome();
        }catch(Exception e){EnableRouteButton.IsEnabled=false;bool noIntel=e.Message.Contains("no Intel",StringComparison.OrdinalIgnoreCase)||e.Message.Contains("Intel graphics device was not found",StringComparison.OrdinalIgnoreCase);graphicsCheckState=noIntel?GraphicsCheckState.NoIntel:GraphicsCheckState.Failed;graphicsDriverState=noIntel?GraphicsDriverState.Unsupported:GraphicsDriverState.Unknown;graphicsWindowsState=GraphicsWindowsState.Error;DriverSummary.Text=PlainLanguage.Error(e.Message);DriverUpdateNotice.IsOpen=true;DriverUpdateNotice.Title="Graphics check could not finish";DriverUpdateNotice.Message="Finish installing the graphics driver, then check again. Saved game setups can still be returned to default settings.";Log("Driver inspection stopped: "+e.Message);RenderGraphicsPage();if(selected!=null)RenderSetup();UpdateButtons();RenderMyGames();RefreshHome();}
    }
    bool LimitAvailable(int mib)=>DriverLimitChoices.IsAvailable(mib,limitCapabilities,TrialMode||selectedReceipt?.Trial==true);
    string CapabilitySummary(){
        var available=limitCapabilities.Where(p=>p.Value.Available).Select(p=>SetupPresentation.Limit(p.Key)).ToArray();
        if(available.Length==0)return "No game-specific patch sizes are verified for this exact driver file. Windows keeps using the normal driver.";
        if(driverOriginal)return "Verified patch sizes for this exact driver: "+string.Join(", ",available)+". This checks the patch files; game behavior and cache growth still need testing.";
        if(successfulTrial!=null)return "This exact driver passed isolated load checks for "+string.Join(", ",available)+". Cache capacity has not been measured.";
        return "No game-specific patch sizes are verified for this exact driver file. Windows keeps using the normal driver.";
    }
    string CacheSizesForGame(){
        var sizes=new SortedSet<int>{512};
        foreach(var capability in limitCapabilities.Values.Where(c=>c.Available&&DriverLimitChoices.IsAvailable(c.MiB,limitCapabilities,TrialMode)))sizes.Add(capability.MiB);
        return string.Join(", ",sizes.Select(CacheLabel));
    }
    async Task CheckDriverChanged() {
        if(busy||checkingDriver||closing||preview||driverIdentity.Length==0)return;checkingDriver=true;
        try {
            var reply=await safety.RunAsync(new("DriverIdentity",ReadOnly:true));if(!reply.Ok)throw new IOException(reply.Message);
            string current=reply.Data.GetProperty("IdentityTag").GetString()??"";if(closing)return;
            if(current==driverIdentity){
                if(DriverPanel.Visibility==Visibility.Visible){var status=await safety.RunAsync(new("Routing",ReadOnly:true));if(status.Ok){bool enabled=status.Data.GetProperty("DeveloperOverride").GetBoolean(),reboot=status.Data.GetProperty("RebootRequired").GetBoolean();var route=reboot?GraphicsWindowsState.RestartRequired:enabled?GraphicsWindowsState.On:GraphicsWindowsState.Off;if(route!=graphicsWindowsState){graphicsWindowsState=route;routingReady=enabled&&!reboot;RenderGraphicsPage();RefreshHome();}}}
                return;
            }
            ShowDriverChange(current);
        }catch(Exception e){if(!closing)Log("Driver update check unavailable: "+e.Message);}finally{checkingDriver=false;}
    }
    void ShowDriverChange(string current) {
        driverIdentity=current;driverOriginal=false;successfulTrial=null;successfulTrialDriver=null;trialEligible=false;graphicsCheckState=GraphicsCheckState.Running;graphicsDriverState=GraphicsDriverState.Changed;graphicsCopyTestState=GraphicsCopyTestState.Stale;
        DriverUpdateNotice.IsOpen=true;DriverUpdateNotice.Title="Your graphics driver has changed";
        DriverUpdateNotice.Message="Set-up games still load their copy of the old driver, also when you start them from Steam. Check support for the new driver, then return those games to default settings and set them up again.";
        DriverSummary.Text="The installed graphics software has changed. Your game setup files have not been changed.";
        homeVersion="";Log("Driver change detected from registry/file metadata. Rechecking automatically.");if(selected!=null)RenderSetup();UpdateButtons();RefreshHome();if(DriverPanel.Visibility==Visibility.Visible)_=CheckTrialSupport();
    }
    async Task PollGame() {
        if(busy||backgroundCheck||selected==null||activeModBlocker is not null||LibraryPanel.Visibility!=Visibility.Visible)return;
        var game=selected;string id=game.Key;backgroundCheck=true;backgroundPolls++;
        try {
            await SampleAntiCheat(game);if(selected?.Key!=id)return;
            if(TrialMode&&!game.TrialProfileReady) {
                await DiscoverSelected(true);
                if(selected?.Key==id&&selected.TrialProfileReady&&!busy)await ShowGame();
                else if(activeModBlocker is not null&&selected?.Key==id&&!busy)RenderMyGames(); // blocked: polling stops, so show it now
            }else if(!game.Supported) {
                if(await DiscoverSelected(true)&&selected?.Key==id&&!busy)await ShowGame();
                else if(activeModBlocker is not null&&selected?.Key==id&&!busy)RenderMyGames();
            }else if(NeedsControlObservation()) {
                // Polling never enters global busy mode, clears artwork, or rechecks Steam safety. The worker only runs
                // once the game does: Steam can take a while to start it.
                if(!GameProcessRunning(game.SupportedExecutable))return;
                var result=await safety.RunAsync(new("VerifyControl",ReceiptPath:store.Receipt(id),ReadOnly:preview));
                if(!result.Ok)throw new InvalidOperationException(result.Message);
                if(selected?.Key!=id||busy)return;
                if(result.Data.GetProperty("Verified").GetBoolean()) {
                    if(!await ReadSetup(game))return;
                    gameLoadResult=MyGamesLoadResult.DriverCopyLoaded;
                    RenderSetup();Log("Original private-driver loading checked automatically.");
                }
            }else if(selectedReceipt is not null&&game.Supported){
                // Watches the game as long as its page is open, however it was started: a cheap in-process check every
                // tick, and the worker's module scan and driver hash only while a run isn't classified yet.
                if(!GameProcessRunning(game.SupportedExecutable)){
                    if(observedRunningExe is null)return;
                    observedRunningExe=null;var before=gameLoadResult;gameLoadResult=MyGamesLoadResult.Unknown;RuntimeDetails.Text=GameClosedMessage;
                    if(GamePollPolicy.ShouldRender(before,gameLoadResult))RenderMyGames();
                    return;
                }
                if(observedRunningExe is not null&&gameLoadResult!=MyGamesLoadResult.Unknown)return; // this run is classified
                observedRunningExe=game.SupportedExecutable;
                var observed=await safety.RunAsync(new("ObserveGame",game.SupportedExecutable,ReadOnly:preview));
                if(selected?.Key!=id||busy)return;
                string message=observed.Message;RuntimeDetails.Text=message;
                var previous=gameLoadResult;ClassifyGameObservation(game,message);
                bool saved=false;
                if(gameLoadResult==MyGamesLoadResult.DriverCopyLoaded&&!ReceiptLoadVerified()&&!preview) {
                    // Persist the check so it survives closing the game. The worker re-verifies by itself.
                    try{var verify=await safety.RunAsync(new("VerifySize",ReceiptPath:store.Receipt(id)));
                        if(verify.Ok&&verify.Data.GetProperty("Verified").GetBoolean()&&selected?.Key==id&&!busy&&await ReadSetup(game)){saved=true;RenderSetup();Log("Cache size "+CacheLabel(selectedReceipt?.MiB??512)+" loaded in game; check saved.");}}
                    catch(Exception ex){Log("Saving the load check failed: "+ex.Message);}
                }
                if(gameLoadResult==MyGamesLoadResult.DriverCopyLoaded&&ReceiptLoadVerified())await LinkCacheFileFromGame(game);
                if(saved||GamePollPolicy.ShouldRender(previous,gameLoadResult))RenderMyGames();
            }
            lastBackgroundError="";
        }catch(Exception e){if(lastBackgroundError!=e.Message){Log("Automatic check paused: "+e.Message);lastBackgroundError=e.Message;}}
        finally{backgroundCheck=false;}
    }
    void ClassifyGameObservation(SteamGame game,string message){
        if(message.Contains("not running",StringComparison.OrdinalIgnoreCase)||message.Contains("could not be fully inspected",StringComparison.OrdinalIgnoreCase)||message.Contains("No Xe2 driver path was observed",StringComparison.OrdinalIgnoreCase)){gameLoadResult=MyGamesLoadResult.Unknown;return;}
        string[] lines=message.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries);
        string? moduleLine=lines.FirstOrDefault(line=>line.Contains(".dll",StringComparison.OrdinalIgnoreCase)&&line.Contains("PID ",StringComparison.OrdinalIgnoreCase));
        string? path=moduleLine?.Split(": ",2,StringSplitOptions.None).LastOrDefault();
        string? diskHash=lines.FirstOrDefault(line=>line.StartsWith("Disk SHA-256:",StringComparison.OrdinalIgnoreCase))?.Split(':',2)[1].Trim();
        bool copyPath=path?.StartsWith(game.SupportedExecutable+".local\\",StringComparison.OrdinalIgnoreCase)==true;
        bool matching=false;
        if(copyPath&&path is not null&&diskHash is not null&&selectedReceipt is not null&&selectedReceipt.Files.TryGetValue(Path.GetFileName(path),out string? expected))matching=expected.Equals(diskHash,StringComparison.OrdinalIgnoreCase);
        gameLoadResult=matching?MyGamesLoadResult.DriverCopyLoaded:moduleLine is not null?MyGamesLoadResult.NormalDriverLoaded:MyGamesLoadResult.Unknown;
    }
    async Task<bool> ReadSetup(SteamGame game) {
        GameSetupState? state=null;
        if(game.SupportedExecutable!=null)try {
            var reply=await safety.RunAsync(new("GameState",game.SupportedExecutable,store.Receipt(game)));
            if(!reply.Ok)throw new InvalidOperationException(reply.Message);state=GameSetupState.FromReply(reply);
        }catch(Exception e){var blocker=BlockerFromError(e.Message,game.SupportedExecutable);if(blocker is not null){activeModBlocker=blocker;activeModBlockerGameKey=game.Key;discoveryTimer.Stop();}state=new(false,false,false,false,null,e.Message);}
        if(selected?.Key!=game.Key)return false;
        setupState=state;selectedReceipt=state?.Receipt;recordValid=state?.RecordValid==true;receiptValid=state?.ContentsValid==true;
        return true;
    }
    void RenderSetup() {
        if(selected==null)return;
        var presentation=SetupPresentation.From(setupState,selected.Supported,routingReady,GameDriverReady);if(successfulTrial!=null&&setupState?.Configured==true&&selectedReceipt?.Trial!=true&&!GameDriverReady)presentation=new SetupPresentation($"{SetupPresentation.Limit(selectedReceipt!.MiB)} saved · older setup","Update this game’s setup","The driver check passed. Use default settings below, then follow the guide to set up the tested copy.",false);
        GameStatus.Text=presentation.Installed;CurrentCap.Text=presentation.Installed;
        GameNotice.Title=presentation.Title;GameNotice.Message=presentation.Next;
        GameNotice.Severity=setupState?.Error!=null||setupState?.CompatibilityRepairRequired==true?InfoBarSeverity.Warning:InfoBarSeverity.Informational;
        GameNotice.IsOpen=setupState?.Error!=null||setupState?.CompatibilityRepairRequired==true;
        gameGuide=GameGuide.From(setupState,selected.Supported,routingReady,GameDriverReady,successfulTrial!=null);GuideTitle.Text=gameGuide.Title;GuideBody.Text=gameGuide.Body;GuideButton.Content=gameGuide.Action=="Launch"?GameLaunch.For(selected).Caption:gameGuide.Button;
        GameSource.Text=selected.Source.ToUpperInvariant()+" LIBRARY";
        bool ready=setupState?.Configured==true&&selectedReceipt?.MiB>512&&GameDriverReady&&routingReady&&setupState?.Error==null;
        GuideCard.Visibility=ready?Visibility.Collapsed:Visibility.Visible;
        SetupStage.Text=gameGuide.Action switch{"Graphics"=>"Before game setup","Prepare"=>"Step 2 of 4 · prepare game files","Apply"=>"Step 4 of 4 · choose a cache size","Launch" when NeedsControlObservation()=>"Step 3 of 4 · check the game files","Launch"=>"Step 1 of 4 · find the game files",_=>"Setup needs attention"};
        bool inGameSetup=gameGuide.Action is "Launch" or "Prepare" or "Apply";int progress=gameGuide.Action switch{"Launch" when NeedsControlObservation()=>50,"Prepare"=>25,"Apply"=>75,_=>0};SetupProgress.Value=progress;SetupProgress.Visibility=inGameSetup?Visibility.Visible:Visibility.Collapsed;SetupProgress.IsIndeterminate=false;
        GuideButton.Visibility=gameGuide.Action is "Apply" or "Prepare" or "Launch" or "Restore"?Visibility.Collapsed:Visibility.Visible;
        updatingChoice=true;
        int desired=desiredLimits.GetValueOrDefault(selected.Key,selectedReceipt?.MiB??512);if(!LimitAvailable(desired)&&desired!=512&&!(setupState?.Configured==true&&selectedReceipt?.MiB==desired)){desired=DriverLimitChoices.Fallback(limitCapabilities,TrialMode||selectedReceipt?.Trial==true);desiredLimits[selected.Key]=desired;}foreach(var item in CapChoice.Items.OfType<ComboBoxItem>())item.IsEnabled=LimitAvailable(int.Parse(item.Tag.ToString()!));
        CapChoice.SelectedItem=CapChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>int.Parse(i.Tag.ToString()!)==desired);
        updatingChoice=false;UpdateButtons();RenderMyGames();RefreshHome();
    }
    string LaunchThroughLabel()=>selected is null?"Launch game":selected.Source=="Manual"?"Start game":"Launch through "+selected.Source;
    static string CacheLabel(int mib)=>mib<=512?"Default":SetupPresentation.Limit(mib);
    void RenderMyGames(){
        RenderMonitorButton();
        if(GameStep1==null||LibraryPanel.Visibility!=Visibility.Visible)return;
        MyGamesPageInputs input;
        if(developerMyGamesPreview&&myGamesPreviewInput is not null) input=myGamesPreviewInput;
        else {
            int chosen=selected is null?512:desiredLimits.GetValueOrDefault(selected.Key,selectedReceipt?.MiB is >512?selectedReceipt.MiB:512);
            selectedCacheMiB=chosen;
            bool installed=selected is not null&&Directory.Exists(selected.InstallDirectory);
            bool exists=setupState?.FolderExists==true||setupState?.ReceiptExists==true;
            bool valid=!exists||setupState?.RecordValid==true&&setupState?.ContentsValid==true;
            input=new(
                GraphicsReady:graphicsPageState?.ReadyForGames==true,
                HasGames:games.Count>0,HasSelection:selected is not null,GameInstalled:selected is not null&&games.Any(g=>g.Key==selected.Key),
                ProfileFound:selected?.Supported==true,Dx12Unavailable:false,
                SetupExists:exists,SetupValid:valid,SetupRunning:setupOperationRunning,
                ControlVerified:!NeedsControlObservation(),SizeApplyRunning:setupOperationRunning&&setupState?.Configured==true,
                AppliedDefault:selectedReceipt?.MiB is null or <=512,ChosenDefault:chosen<=512,
                LoadResult:EffectiveLoadResult(),LaunchLabel:LaunchThroughLabel(),CacheSize:CacheLabel(chosen),AvailableSizes:CacheSizesForGame(),AppliedChoiceMatches:selectedReceipt?.MiB==chosen,TrialNeedsRetest:setupState?.TrialNeedsRetest==true,DiscoveryMessage:discoveryMessage,SizeVerified:setupState?.Configured==true&&selectedReceipt?.MiB>512&&selectedReceipt.Status=="size-loading-verified",
                ModBlocker:selected?.Key==activeModBlockerGameKey?activeModBlocker:null,
                RecorderUpdate:selected?.Key==activeModBlockerGameKey&&activeModBlocker is {IsDirectory:false} blockingDll&&ScsKillerBridge.IsRecorder(blockingDll.Path)&&ScsKillerBridge.IsDotLocalAware(scs?.RecorderSource()),
                RequiresTrialProfile:TrialMode,TrialProfileFound:selected?.TrialProfileReady==true,
                AntiCheatSeen:selected is null?null:sightings?.Get(selected.Key),
                AntiCheatFiles:selected is not null&&antiCheatHints.TryGetValue(selected.Key,out var acFiles)&&acFiles.AlwaysOn?acFiles:null,
                AntiCheatOverride:selected is null?null:overrides?.Get(selected.Key));
        }
        var state=MyGamesPageStates.Resolve(input);
        if(myGamesChangeStep==2&&state.Step2Complete)state=state with{HeaderStatus="Set up · launch to check",ListBadge="Set up",Step2Result="Ready to check again",Step2Body="Start the game in DirectX 12. Arc Cache checks its loaded driver.",Step2Complete=false,Step3Result="Waiting for step 2",Step3Body="",Step3Complete=false,ExpandedStep=2,ShowSizeChoices=false,ShowSizeApply=false,ShowStep2Launch=true,ShowStep3Launch=false,AccentButton=input.LaunchLabel,AccentAction=MyGamesAction.Launch,Warning=null};
        if(myGamesChangeStep==3&&state.Step3Complete&&state.ShowGameFlow)state=state with{ExpandedStep=3,ShowSizeChoices=true,ShowStep3Launch=true,ShowSizeApply=false,AccentButton=input.LaunchLabel,AccentAction=MyGamesAction.Launch};
        if(!developerMyGamesPreview&&GameProcessRunning(selected?.SupportedExecutable))state=MyGamesPageStates.WhileGameRunning(state);myGamesPageState=state;
        GameTitle.Text=selected?.Name??"Choose a game";GameSource.Text=selected is null?"GAME LIBRARY":selected.Source.ToUpperInvariant()+" LIBRARY";
        GameStatus.Text=state.HeaderStatus;
        GameFlowCard.Visibility=state.ShowGameFlow?Visibility.Visible:Visibility.Collapsed;
        GraphicsGateCard.Visibility=state.ShowGraphicsGate?Visibility.Visible:Visibility.Collapsed;
        GameHeaderCard.Visibility=selected is null?Visibility.Collapsed:Visibility.Visible;
        bool message=state.ShowEmptyLibrary||state.ShowNoSelection||state.Key is "dx12-unavailable" or "game-uninstalled" or "problem-recovery" or "size-retest-required" or "blocked-by-another-mod" or "setup-needs-attention" or "anticheat-running" or "anticheat-files" or "anticheat-with-copy";
        LibraryMessageCard.Visibility=message?Visibility.Visible:Visibility.Collapsed;
        LibraryMessageTitle.Text=state.HeaderStatus;
        LibraryMessageText.Text=state.Warning??(state.ShowNoSelection?"Select a game from the list to see its setup steps.":state.ShowEmptyLibrary?"Add a game to start setting up game-specific driver copies.":string.Empty);
        SetupAttentionDetails.Visibility=state.Key=="setup-needs-attention"&&!string.IsNullOrWhiteSpace(input.DiscoveryMessage)?Visibility.Visible:Visibility.Collapsed;
        if(state.Key!="setup-needs-attention")SetupAttentionDetails.IsExpanded=false;SetupAttentionRawDetails.Text=input.DiscoveryMessage??string.Empty;
        LibraryMessageButton.Content=state.AccentButton;LibraryMessageButton.Visibility=state.AccentButton.Length>0?Visibility.Visible:Visibility.Collapsed;LibraryMessageButton.IsEnabled=!busy;
        MyGamesAdvancedCard.Visibility=state.ShowEmptyLibrary||state.ShowNoSelection||state.Key is "blocked-by-another-mod" or "setup-needs-attention"?Visibility.Collapsed:Visibility.Visible;
        BlockerCheckAgainLink.Visibility=state.Key=="blocked-by-another-mod"?Visibility.Visible:Visibility.Collapsed;
        AntiCheatOverrideButton.Visibility=state.Key is "anticheat-running" or "anticheat-files" or "anticheat-with-copy"?Visibility.Visible:Visibility.Collapsed;AntiCheatOverrideButton.IsEnabled=!busy;
        AntiCheatOverrideButton.Content=state.Key=="anticheat-with-copy"?"Keep using the driver copy anyway…":"Use Arc Cache with this game anyway…";
        var accepted=selected is null?null:overrides?.Get(selected.Key);
        AntiCheatOverrideBar.IsOpen=accepted is not null;
        if(accepted is not null&&selected is not null){string who=input.AntiCheatSeen?.Name??input.AntiCheatFiles?.Name??(antiCheatHints.TryGetValue(selected.Key,out var acHint)&&acHint.Level is AntiCheatLevel.AlwaysOn or AntiCheatLevel.Optional?acHint.Name:"An anti-cheat");AntiCheatOverrideBar.Message=$"You accepted the risk on {accepted.Value.ToLocalTime():g}. {who} can see this game's changed driver file and your account can be banned, even in single-player. Arc Cache can't undo a ban.";}
        FlowWarningBorder.Visibility=string.IsNullOrWhiteSpace(state.Warning)||state.Key is "problem-normal-driver" or "problem-size-normal-driver" or "problem-check-failed"?Visibility.Collapsed:Visibility.Visible;
        FlowWarningText.Text=state.Warning??string.Empty;
        GameStep1Mark.Text=state.Step1Complete?"✓":"1";GameStep2Mark.Text=state.Step2Complete?"✓":"2";GameStep3Mark.Text=state.Step3Complete?"✓":"3";
        GameStep1Result.Text=state.Step1Result;GameStep2Result.Text=state.Step2Result;GameStep3Result.Text=state.Step3Result;
        ChangeCheckLink.Visibility=state.Step2Complete?Visibility.Visible:Visibility.Collapsed;ChangeCheckLink.Content="Check again";ChangeSizeLink.Visibility=state.Step3Complete?Visibility.Visible:Visibility.Collapsed;
        GameStep1Body.Text=state.ExpandedStep==1?state.Step1Body:string.Empty;
        GameStep2Body.Text=state.ExpandedStep==2?state.Step2Body:string.Empty;
        GameStep3Body.Text=state.ExpandedStep==3?state.Step3Body:string.Empty;
        GameStep1Body.Visibility=string.IsNullOrWhiteSpace(GameStep1Body.Text)?Visibility.Collapsed:Visibility.Visible;
        GameStep2Body.Visibility=string.IsNullOrWhiteSpace(GameStep2Body.Text)?Visibility.Collapsed:Visibility.Visible;
        GameStep3Body.Visibility=string.IsNullOrWhiteSpace(GameStep3Body.Text)?Visibility.Collapsed:Visibility.Visible;
        GameStep1.Visibility=state.ShowGameFlow?Visibility.Visible:Visibility.Collapsed;
        GameStep2.Visibility=state.ShowGameFlow?Visibility.Visible:Visibility.Collapsed;
        GameStep3.Visibility=state.ShowGameFlow?Visibility.Visible:Visibility.Collapsed;
        SetupGameButton.Visibility=state.AccentAction is MyGamesAction.SetUp or MyGamesAction.LaunchForDiscovery?Visibility.Visible:Visibility.Collapsed;
        SetupGameButton.Content=state.AccentButton;
        SetupGameButton.IsEnabled=!busy;
        bool launch2=state.ExpandedStep==2&&state.AccentAction is MyGamesAction.Launch or MyGamesAction.LaunchAgain;
        bool launch3=state.ExpandedStep==3&&state.AccentAction is MyGamesAction.Launch or MyGamesAction.LaunchAgain;
        LaunchInitialButton.Visibility=launch2?Visibility.Visible:Visibility.Collapsed;
        LaunchConfirmButton.Visibility=launch3?Visibility.Visible:Visibility.Collapsed;
        LaunchInitialButton.Content=state.AccentAction==MyGamesAction.LaunchAgain?"Launch again":input.LaunchLabel;
        LaunchConfirmButton.Content=state.AccentAction==MyGamesAction.LaunchAgain?"Launch again":input.LaunchLabel;
        LaunchInitialButton.IsEnabled=!busy;LaunchConfirmButton.IsEnabled=!busy;
        ApplySizeButton.Visibility=state.AccentAction==MyGamesAction.ApplySize?Visibility.Visible:Visibility.Collapsed;
        ApplySizeButton.Content=state.AccentButton;ApplySizeButton.IsEnabled=!busy;
        GameSizeOptions.Children.Clear();GameSizeOptions.Visibility=state.ShowSizeChoices&&state.ExpandedStep==3?Visibility.Visible:Visibility.Collapsed;
        if(state.ShowSizeChoices){
            var supported=new SortedSet<int>{512};
            if(developerMyGamesPreview){foreach(string item in input.AvailableSizes.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){if(item.Equals("Default",StringComparison.OrdinalIgnoreCase))supported.Add(512);else if(int.TryParse(item.Replace(" GB",string.Empty,StringComparison.OrdinalIgnoreCase),out int gb))supported.Add(gb*1024);}}
            else foreach(var c in limitCapabilities.Values.Where(c=>c.Available&&DriverLimitChoices.IsAvailable(c.MiB,limitCapabilities,TrialMode)))supported.Add(c.MiB);
            foreach(int mib in supported){
                var choice=CapChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>int.TryParse(x.Tag?.ToString(),out int value)&&value==mib);
                if(choice is null)continue;
                bool isSelected=mib==selectedCacheMiB;
                var option=new Button{Content=CacheLabel(mib),Tag=mib.ToString(),IsEnabled=!busy,MinWidth=86,MinHeight=40,Margin=new Thickness(0,0,5,0),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,isSelected?(byte)48:(byte)55,isSelected?(byte)59:(byte)58,isSelected?(byte)63:(byte)62)),BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,isSelected?(byte)82:(byte)82,isSelected?(byte)199:(byte)88,isSelected?(byte)242:(byte)94)),BorderThickness=new Thickness(isSelected?2:1)};
                option.Click+=SelectSize;GameSizeOptions.Children.Add(option);
            }
        }
        GameStep1Progress.Visibility=state.ShowProgress&&state.ExpandedStep==1?Visibility.Visible:Visibility.Collapsed;
        GameStep1Progress.IsIndeterminate=false;GameStep1Progress.Value=state.ShowProgress?45:0;
        GameStep1ProgressText.Text=state.ProgressText;GameStep1ProgressText.Visibility=state.ShowProgress&&state.ExpandedStep==1?Visibility.Visible:Visibility.Collapsed;
        GameFlowProgress.Visibility=state.ShowProgress&&state.ExpandedStep==2?Visibility.Visible:Visibility.Collapsed;
        GameFlowProgress.IsIndeterminate=false;GameFlowProgress.Value=state.ShowProgress?45:0;
        GameFlowProgressText.Text=state.ProgressText;GameFlowProgressText.Visibility=state.ShowProgress&&state.ExpandedStep==2?Visibility.Visible:Visibility.Collapsed;
        GameStep3Progress.Visibility=state.ShowProgress&&state.ExpandedStep==3?Visibility.Visible:Visibility.Collapsed;
        GameStep3Progress.IsIndeterminate=false;GameStep3Progress.Value=state.Key=="size-apply-running"?70:state.ShowProgress?45:0;
        GameStep3ProgressText.Text=state.ProgressText;GameStep3ProgressText.Visibility=state.ShowProgress&&state.ExpandedStep==3?Visibility.Visible:Visibility.Collapsed;
        StopFlowButton.Visibility=state.ShowStop&&state.ExpandedStep==2?Visibility.Visible:Visibility.Collapsed;StopFlowButton.IsEnabled=developerMyGamesPreview||!busy;
        StopSizeCheckButton.Visibility=state.ShowStop&&state.ExpandedStep==3?Visibility.Visible:Visibility.Collapsed;StopSizeCheckButton.IsEnabled=developerMyGamesPreview||!busy;
        ScsCard.Visibility=state.ShowGameFlow&&state.Step2Complete?Visibility.Visible:Visibility.Collapsed;
        ScsStatusButton.IsEnabled=!busy&&!scsRunning&&selected!=null&&scs?.CliPath!=null&&selected?.Source=="Steam"&&!developerMyGamesPreview;
        var scsPrivateWarm=CreateScsPrivateWarm();
        ScsCompileButton.IsEnabled=ScsStatusButton.IsEnabled&&(selectedReceipt?.MiB<=512||scsPrivateWarm!=null&&scsInfo?.PrivateDriverSupport==true);
        ScsSummary.Text=selected?.Source!="Steam"?"SCSKiller is currently available for Steam games.":selectedReceipt?.MiB>512
            ?scsPrivateWarm!=null?$"Compiles this game's shaders ahead of time with its {SetupPresentation.Limit(scsPrivateWarm.CacheMiB)} driver copy, so they stay in the cache.":"This game's larger driver copy is not verified for shader preparation. Fix the game setup or return it to Default first."
            :scs?.CliPath is null||developerMyGamesPreview?"Choose the SCSKiller command-line program in Settings first. Default uses the normal Intel driver.":"Optional: prepare shaders with your existing SCSKiller account. Default uses the normal Intel driver.";
        RenderScsTool();
        RestoreButton.Visibility=setupState?.ReceiptExists==true&&state.Key is not "empty-library" and not "no-selection"?Visibility.Visible:Visibility.Collapsed;
        RestoreButton.IsEnabled=!busy&&recordValid&&selected?.SupportedExecutable is not null&&!developerMyGamesPreview;
        RemoveManualButton.Visibility=selected?.Source=="Manual"?Visibility.Visible:Visibility.Collapsed;
        RemoveManualButton.IsEnabled=!busy&&!developerMyGamesPreview;
        ImportButton.IsEnabled=!busy&&selected is not null&&!developerMyGamesPreview;
        ObserveButton.IsEnabled=!busy&&selected?.SupportedExecutable is not null&&!developerMyGamesPreview;
        MonitorButton.IsEnabled=!busy&&selected?.Supported==true&&!developerMyGamesPreview;
        RuntimeStatus.Text=gameLoadResult switch{MyGamesLoadResult.DriverCopyLoaded=>"Driver copy loaded. The process path and on-disk file were observed; in-memory bytes and cache growth are not measured.",MyGamesLoadResult.NormalDriverLoaded=>"Normal Intel driver loaded. Close the game, then launch again through its store.",MyGamesLoadResult.Checking=>"Checking the loaded driver…",MyGamesLoadResult.Failed=>"The check could not confirm the loaded driver.",_=>"No loaded-driver check has been run for this launch."};
        if(!string.IsNullOrWhiteSpace(input.AvailableSizes))LimitAvailability.Text="Cache sizes available: "+input.AvailableSizes;
        GameDetails.Text=selected is null?"Select a game to see its folders and saved setup record.":$"Store: {selected.Source} · ID: {selected.ExternalId??selected.AppId.ToString()}\nInstall folder: {selected.InstallDirectory}\nRecovery record: {store.Receipt(selected)}";
        if(developerMyGamesPreview){
            HomePanel.Visibility=Visibility.Collapsed;DriverPanel.Visibility=Visibility.Collapsed;ActivityPanel.Visibility=Visibility.Collapsed;SettingsPanel.Visibility=Visibility.Collapsed;LibraryPanel.Visibility=Visibility.Visible;
            GlobalNotice.IsOpen=false;FooterStatus.Text=string.Empty;
        }
        Filter();
        if(developerMyGamesPreview){if(GamesList.Visibility==Visibility.Visible)GamesList.Focus(FocusState.Programmatic);else if(LibraryMessageButton.Visibility==Visibility.Visible)LibraryMessageButton.Focus(FocusState.Programmatic);}
    }
    void Step1Action(object sender,RoutedEventArgs e){if(myGamesPageState?.AccentAction==MyGamesAction.LaunchForDiscovery)LaunchGame(sender,e);else ApplyGame(sender,e);}
    void ChangeGameStep1(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;GamesList.Focus(FocusState.Programmatic);}
    void ChangeGameStep2(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;myGamesChangeStep=2;gameLoadResult=MyGamesLoadResult.Unknown;RenderMyGames();}
    void ChangeGameStep3(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;myGamesChangeStep=3;RenderMyGames();}
    void LibraryMessageAction(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;
        switch(myGamesPageState?.AccentAction){case MyGamesAction.AddGame:AddManualGame(sender,e);break;case MyGamesAction.OpenGraphics:OpenGraphics(sender,e);break;case MyGamesAction.OpenGameFolder:OpenSelectedGameFolder();break;case MyGamesAction.UpdateRecorder:UpdateBlockingRecorder();break;case MyGamesAction.LaunchForDiscovery:LaunchGame(sender,e);break;case MyGamesAction.RestoreGame:RestoreGame(sender,e);break;case MyGamesAction.ChooseAnotherGame:GamesList.Focus(FocusState.Programmatic);break;case MyGamesAction.ReviewSetup:case MyGamesAction.ReviewSavedSetup:MyGamesAdvanced.IsExpanded=true;break;}
    }
    void OpenSelectedGameFolder(){if(selected is null)return;try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={selected.InstallDirectory}});}catch(Exception e){Log("Could not open game folder: "+e.Message);}}
    async void RecheckBlockedGame(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;activeModBlocker=null;activeModBlockerGameKey=null;discoveryTimer.Stop();await ShowGame();}
    void StopGameCheck(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;discoveryTimer.Stop();gameLoadResult=MyGamesLoadResult.Failed;RenderMyGames();
    }
    async Task ShowGame() {
        discoveryTimer.Stop();var game=selected;if(!string.Equals(observedRunningExe,game?.SupportedExecutable,StringComparison.OrdinalIgnoreCase))observedRunningExe=null;if(activeModBlockerGameKey!=game?.Key){activeModBlocker=null;activeModBlockerGameKey=null;}Details.Visibility=Visibility.Visible;if(game==null){GameTitle.Text="Choose a game";GameStatus.Text="Choose a game from the list.";UpdateButtons();RenderMyGames();RefreshHome();return;}
        if(!games.Any(g=>g.Key==game.Key)){
            GameTitle.Text=game.Name;GameSource.Text=game.Source.ToUpperInvariant()+" LIBRARY";setupState=null;recordValid=false;receiptValid=false;selectedReceipt=null;
            try{string receipt=store.Receipt(game);if(File.Exists(receipt))selectedReceipt=JsonSerializer.Deserialize<ReceiptState>(File.ReadAllText(receipt),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}catch(Exception error){Log("Saved setup could not be read: "+error.Message);}
            RenderMyGames();UpdateButtons();return;
        }
        await DiscoverSelected();if(selected?.Key!=game.Key)return;game=selected;
        GameTitle.Text=game.Name;
        string artKey=string.Join("|",game.ArtworkSources);
        if(displayedArtwork!=artKey){displayedArtwork=artKey;GamePlaceholder.Visibility=Visibility.Visible;GameArtwork.Source=ArtworkImages.Get(game.ArtworkSources,240);}
        GameDetails.Text=$"Library: {game.Source} · ID: {game.ExternalId??game.AppId.ToString()}\nInstall folder: {game.InstallDirectory}\nRecovery record: {store.Receipt(game)}";
        safetyAllowed=false;
        try {
            var assessment=(await safety.RunAsync(new("Safety",SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory))).Data;
            if(selected?.Key!=game.Key)return;
            var notice=SafetyPresentation.From(assessment,game.Source);safetyAllowed=!notice.Warn;
            SafetyPanel.Visibility=notice.Warn?Visibility.Visible:Visibility.Collapsed;
            SafetyNotice.IsOpen=notice.Warn;SafetyNotice.Title=notice.Title;SafetyNotice.Message=notice.Message;SafetyNotice.Severity=InfoBarSeverity.Warning;
            SafetyDetails.Text=assessment.GetProperty("Message").GetString()+"\n"+assessment.GetProperty("Multiplayer").GetString()+"\n"+assessment.GetProperty("SteamStatus").GetString();
            Log("Safety: "+SafetyDetails.Text);
        }catch(Exception e){if(selected?.Key!=game.Key)return;SafetyPanel.Visibility=Visibility.Visible;SafetyNotice.IsOpen=true;SafetyNotice.Title="Game safety could not be confirmed";SafetyNotice.Message="A modified driver may cause launch failures or account penalties. Continue only if you accept that risk.";SafetyNotice.Severity=InfoBarSeverity.Warning;SafetyDetails.Text=e.Message;Log("Safety check: "+e.Message);}
        if(!await ReadSetup(game))return;
        if(selectedReceipt?.MiB==512&&selectedReceipt.Status=="original-control-loading-verified")gameLoadResult=MyGamesLoadResult.DriverCopyLoaded;
        if(setupState?.Configured==true&&selectedReceipt?.MiB>512)desiredLimits[game.Key]=selectedReceipt.MiB;
        RenderSetup();
        if(activeModBlocker is null&&(!preview||previewWatchSeconds>0)&&(!game.Supported||TrialMode&&!game.TrialProfileReady||NeedsControlObservation()||selectedReceipt is not null||sightings?.Get(game.Key) is not null))discoveryTimer.Start();RenderMyGames();RefreshHome();
    }
    void ArtworkOpened(object sender,RoutedEventArgs e)=>GamePlaceholder.Visibility=Visibility.Collapsed;
    void ArtworkFailed(object sender,ExceptionRoutedEventArgs e)=>GamePlaceholder.Visibility=Visibility.Visible;
    // A load check saved in the receipt counts after the game closes or the app restarts.
    MyGamesLoadResult EffectiveLoadResult()=>MyGamesPageStates.EffectiveLoadResult(gameLoadResult,setupState?.Configured==true,selectedReceipt?.MiB,selectedReceipt?.Status);
    bool ReceiptLoadVerified()=>setupState?.Configured==true&&selectedReceipt is not null&&(selectedReceipt.MiB<=512?selectedReceipt.Status=="original-control-loading-verified":selectedReceipt.Status=="size-loading-verified");
    // By full path: SCSKiller's compile process runs under the game's exe name from its own folder and is not the game.
    // A process whose path can't be read (anti-cheat protected) counts as the game.
    /// <summary>Links the Intel cache file the running game itself has open (Restart Manager): exact, unlike watching which
    /// file grows. Runs once per game start, when its driver copy is confirmed.</summary>
    async Task LinkCacheFileFromGame(SteamGame game){
        if(cacheFiles==null||game.SupportedExecutable is not string exe)return;
        try{
            int? pid=GameProcessId(exe);if(pid==null)return;
            string? file=await Task.Run(()=>CacheFileMap.FileOpenBy(pid.Value));if(file==null)return;
            var current=cacheFiles.For(exe);if(current?.File==file)return;
            var link=cacheFiles.Remember(exe,file,CacheFileMap.CurrentBytes(file)??0,"running game");
            Log(current==null?$"Linked {game.Name} to Intel cache file {file[..8]}… ({Size(link.Bytes)}), the file the running game has open.":$"Corrected {game.Name}'s Intel cache file: the running game uses {file[..8]}… ({Size(link.Bytes)}), not {current.File[..8]}….");
            RenderScsTool();
        }catch(Exception e){Log("The game's Intel cache file could not be checked: "+e.Message);}
    }
    static int? GameProcessId(string executable){
        string full=Path.GetFullPath(executable);
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))using(process){
            try{if(string.Equals(process.MainModule?.FileName,full,StringComparison.OrdinalIgnoreCase))return process.Id;}
            catch(Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException){}
        }
        return null;
    }
    static bool GameProcessRunning(string? executable){
        if(string.IsNullOrWhiteSpace(executable))return false;
        try{
            string full=Path.GetFullPath(executable);bool running=false;
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))using(process){
                if(running)continue;
                try{running=string.Equals(process.MainModule?.FileName,full,StringComparison.OrdinalIgnoreCase);}
                catch(Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException){running=true;}
            }
            return running;
        }catch(Exception e) when (e is ArgumentException or IOException or InvalidOperationException){return false;}
    }
    bool NeedsControlObservation()=>setupState?.Configured==true&&selectedReceipt?.MiB==512&&selectedReceipt.Status!="original-control-loading-verified";
    async Task<bool> DiscoverSelected(bool quiet=false) {
        if(selected==null)return false;var game=selected;
        try {
            var request=new WorkerRequest("DiscoverGame",ReceiptPath:store.Receipt(game),SourceReceipt:successfulTrial,SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory,ReadOnly:preview,LibrarySource:game.Source,ExternalId:game.ExternalId,SelectedExecutable:game.ManualExecutable);
            var reply=quiet?await safety.RunAsync(request):await Engine(request);if(!reply.Ok)throw new InvalidOperationException(reply.Message);
            if(selected?.Key!=game.Key)return false;
            activeModBlocker=ReadBlocker(reply.Data);activeModBlockerGameKey=activeModBlocker is null?null:game.Key;
        if(activeModBlocker is not null)discoveryTimer.Stop();
            bool found=reply.Data.GetProperty("Validated").GetBoolean();string? exe=reply.Data.GetProperty("Executable").GetString();
            discoveryMessage=reply.Data.GetProperty("Message").GetString()??"";bool trialProfileReady=reply.Data.TryGetProperty("TrialProfileReady",out var trialProfile)&&trialProfile.ValueKind==JsonValueKind.True;bool changed=game.Detected!=found||game.DetectedExecutable!=exe||game.TrialProfileReady!=trialProfileReady;
            var updated=game with{Detected=found,DetectedExecutable=exe,TrialProfileReady=trialProfileReady,Icon=found?(game.Icon??GameIconResolver.FromExecutable(exe)):game.Icon};games=games.Select(g=>g.Key==game.Key?updated:g).ToArray();selected=updated;
            if(changed)Filter();
            if(found||activeModBlocker is not null)discoveryTimer.Stop();return found;
        }catch(Exception e){
            var blocker=BlockerFromError(e.Message,game.SupportedExecutable);
            if(blocker is not null){activeModBlocker=blocker;activeModBlockerGameKey=game.Key;discoveryMessage=e.Message;discoveryTimer.Stop();if(selected?.Key==game.Key)selected=game with{Detected=false,TrialProfileReady=false};return false;}
            if(quiet)throw;discoveryMessage="Automatic detection unavailable: "+e.Message;Log(discoveryMessage);if(selected?.Key==game.Key)selected=game with{Detected=false,TrialProfileReady=false};return false;
        }
    }
    static LocalModBlocker? ReadBlocker(JsonElement data) {
        if(!data.TryGetProperty("BlockedByAnotherMod",out var flag)||flag.ValueKind!=JsonValueKind.True)return null;
        string Text(string name)=>data.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
        bool directory=data.TryGetProperty("BlockerIsDirectory",out var isDirectory)&&isDirectory.ValueKind==JsonValueKind.True;
        return new LocalModBlocker(Text("BlockerPath"),directory,Text("BlockerProductName"),Text("BlockerFileDescription"),Text("BlockerCompanyName"));
    }
    static LocalModBlocker? BlockerFromError(string message,string? executable=null) {
        bool proxyError=message.Contains(".local setup is blocked",StringComparison.OrdinalIgnoreCase)||message.Contains("already has a local",StringComparison.OrdinalIgnoreCase);
        bool foreignFolderError=message.Contains("private folder exists without this app's recovery record",StringComparison.OrdinalIgnoreCase);
        if(!proxyError&&!foreignFolderError)return null;
        string? path=message.Split(new[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries).LastOrDefault(line=>line.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||line.EndsWith(".local",StringComparison.OrdinalIgnoreCase));
        if((string.IsNullOrWhiteSpace(path)||!File.Exists(path)&&!Directory.Exists(path))&&executable is not null){
            string directory=Path.GetDirectoryName(Path.GetFullPath(executable))!;
            if(proxyError)path=new[]{"d3d12.dll","dxgi.dll"}.Select(name=>Path.Combine(directory,name)).FirstOrDefault(File.Exists);
            if(path is null&&foreignFolderError){string local=executable+".local";if(File.Exists(local)||Directory.Exists(local))path=local;}
        }
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)&&!Directory.Exists(path))return null;
        bool isDirectory=Directory.Exists(path);string? product=null,description=null,company=null;
        if(!isDirectory)try{var info=FileVersionInfo.GetVersionInfo(path);product=info.ProductName;description=info.FileDescription;company=info.CompanyName;}catch{}
        return new LocalModBlocker(path,isDirectory,product,description,company);
    }
    void CapChanged(object sender,SelectionChangedEventArgs e) {
        if(updatingChoice||selected==null||CapChoice.SelectedItem is not ComboBoxItem item)return;
        int selectedLimit=int.Parse(item.Tag.ToString()!);desiredLimits[selected.Key]=selectedLimit;selectedCacheMiB=selectedLimit;
        if(selectedReceipt?.MiB!=selectedLimit)gameLoadResult=MyGamesLoadResult.Unknown;
        UpdateButtons();RenderMyGames();
    }
    void UpdateButtons() {
        TrialButton.IsEnabled=!busy&&trialEligible;TrialFile.IsEnabled=!busy;GuideButton.IsEnabled=!busy&&selected!=null;
        if(ApplyButton==null)return;
        if(selected!=null){LaunchButton.Content=GameLaunch.For(selected).Caption;RemoveManualButton.Visibility=selected.Source=="Manual"?Visibility.Visible:Visibility.Collapsed;}
        SetupProgress.IsIndeterminate=busy&&SetupProgress.Visibility==Visibility.Visible;
        bool supported=selected?.Supported==true;bool configured=setupState?.Configured==true;
        bool chosenLimitAvailable=CapChoice.SelectedItem is ComboBoxItem selectedCap&&LimitAvailable(int.Parse(selectedCap.Tag.ToString()!));
        ApplyButton.IsEnabled=!busy&&supported&&GameDriverReady&&routingReady&&chosenLimitAvailable&&(configured||setupState?.CanPrepare==true)&&!NeedsControlObservation();
        RestoreButton.IsEnabled=!busy&&selected?.SupportedExecutable!=null&&recordValid&&selectedReceipt?.Status!="removed";LaunchButton.IsEnabled=!busy&&selected!=null;ImportButton.IsEnabled=!busy&&selected!=null;CapChoice.IsEnabled=!busy&&supported;
        RestoreButton.Content="Use default settings…";
        if(setupState?.FolderExists==true&&!routingReady)LaunchButton.IsEnabled=false;
        if(setupState?.FolderExists==true&&(!configured||!GameDriverReady))LaunchButton.IsEnabled=false;
        ApplyButton.Content=NeedsControlObservation()?"Start the game to check setup":configured?"Apply this cache size":"Set up this game";

        bool same=configured&&CapChoice.SelectedItem is ComboBoxItem choice&&int.Parse(choice.Tag.ToString()!)==selectedReceipt?.MiB;
        if(same&&!NeedsControlObservation()){ApplyButton.IsEnabled=false;ApplyButton.Content="This size is already set";}
        if(!GameDriverReady&&supported)ApplyButton.Content="Check graphics support first";
        if(selected!=null)SafetyPanel.Visibility=!safetyAllowed?Visibility.Visible:Visibility.Collapsed;
        foreach(var button in SizeButtons.Children.OfType<ToggleButton>()){
            var option=CapChoice.Items.OfType<ComboBoxItem>().First(i=>Equals(i.Tag?.ToString(),button.Tag?.ToString()));
            button.IsEnabled=CapChoice.IsEnabled&&option.IsEnabled;button.IsChecked=Equals(CapChoice.SelectedItem,option);
            ToolTipService.SetToolTip(button,option.IsEnabled?"Choose "+button.Content:LimitEvidence(int.Parse(button.Tag.ToString()!)));
        }
        LimitAvailability.Text=!GameDriverReady?"Open Graphics and choose Check support before choosing a size.":!supported?"Cache sizes will be available when Arc Cache finds the game.":selectedReceipt?.Trial==true?TrialSetupSummary():CapabilitySummary();
        if((selectedCacheMiB>=4096||selectedReceipt?.MiB>=4096)&&selectedReceipt?.Trial==true)LimitAvailability.Text+=" Larger sizes are experimental: the driver copy loads, but cache reload is not verified.";
        if(configured&&!same&&GameDriverReady&&routingReady&&!NeedsControlObservation()&&CapChoice.SelectedItem is ComboBoxItem cap)ApplyButton.Content="Apply "+SetupPresentation.Limit(int.Parse(cap.Tag.ToString()!));
        ScsStatusButton.IsEnabled=!busy&&!scsRunning&&selected!=null&&scs?.CliPath!=null&&selected?.Source=="Steam";
        var scsPrivateWarm=CreateScsPrivateWarm();
        ScsCompileButton.IsEnabled=ScsStatusButton.IsEnabled&&(!configured||selectedReceipt?.MiB<=512||scsPrivateWarm!=null&&scsInfo?.PrivateDriverSupport==true);
        ScsSummary.Text=selected?.Source!="Steam"?"SCSKiller integration currently supports Steam games only.":configured&&selectedReceipt?.MiB>512
            ?scsPrivateWarm!=null?$"Compiles this game's shaders ahead of time with its {SetupPresentation.Limit(scsPrivateWarm.CacheMiB)} driver copy, so they stay in the cache.":"This larger driver copy is not verified for shader preparation. Fix the game setup or return it to Default first."
            : "SCSKiller prepares shaders with the normal driver and Default size. Community downloads need your existing SCSKiller account and settings.";
        RenderScsTool();
    }
    string LimitEvidence(int mib){
        if(limitCapabilities.TryGetValue(mib,out var capability)&&!string.IsNullOrWhiteSpace(capability.Evidence))return capability.Evidence;
        return "Check support for this exact driver before using this size.";
    }
    string TrialSetupSummary()=>driverOriginal
        ?"This game still uses an older test copy. Use default settings, then set it up again with the currently checked driver to use that driver's verified sizes."
        :"This game uses a private copy of this driver. Sizes are available only after each one passes its own test on this exact file.";
    async Task<bool> Confirm(string title,string message,string primary,bool requireOriginalTest=false,bool requireRisk=false) {
        var panel=new StackPanel{Spacing=12};panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap});
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=title,Content=panel,PrimaryButtonText=primary,CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,RequestedTheme=Root.RequestedTheme};
        CheckBox? original=null,risk=null;
        void Update()=>dialog.IsPrimaryButtonEnabled=(!requireOriginalTest||original?.IsChecked==true)&&(!requireRisk||risk?.IsChecked==true);
        if(requireOriginalTest){original=new CheckBox{Content="I checked the unchanged copy first. I understand this option can use lots of memory or cause crashes."};panel.Children.Add(original);original.Checked+=(_,_)=>Update();original.Unchecked+=(_,_)=>Update();}
        if(requireRisk){risk=new CheckBox{Content=new TextBlock{Text="I understand this may stop the game from starting or cause account penalties.",TextWrapping=TextWrapping.Wrap}};panel.Children.Add(risk);risk.Checked+=(_,_)=>Update();risk.Unchecked+=(_,_)=>Update();}
        Update();
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    sealed record TrialFileChoice(string Name,string File){public string DisplayName=>Name switch{"igd12um64xeh.dll"=>"Intel Arc A-series · Xe1","igd12um64xe2.dll"=>"Intel Xe2 graphics · cards and built-in","igd12um64xe3.dll"=>"Intel Xe3 graphics",_=>Name};}
    bool fillingTrial,trialEligible,graphicsStatsBusy;
    long? graphicsCacheBaseline;
    readonly DispatcherTimer graphicsStatsTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    static string DefaultTrialDriver(string? adapter)=>System.Text.RegularExpressions.Regex.IsMatch(adapter??"",@"\bA\d{2,3}(?:M)?\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase)?"igd12um64xeh.dll":"igd12um64xe2.dll";
    void FillTrial(JsonElement data){
        string? previous=(TrialFile.SelectedItem as TrialFileChoice)?.File;fillingTrial=true;
        try{string folder=data.GetProperty("DriverFolder").GetString()??"";var choices=data.GetProperty("Files").EnumerateArray().Where(f=>f.GetProperty("Hash").ValueKind==JsonValueKind.String).Select(f=>new TrialFileChoice(f.GetProperty("Name").GetString()!,Path.Combine(folder,f.GetProperty("Name").GetString()!))).Where(f=>f.Name=="igd12um64xe2.dll"||f.Name=="igd12um64xe3.dll"||f.Name=="igd12um64xeh.dll").ToArray();TrialFile.ItemsSource=choices;TrialFile.SelectedItem=choices.FirstOrDefault(c=>c.File.Equals(previous,StringComparison.OrdinalIgnoreCase))??choices.FirstOrDefault(c=>c.Name==successfulTrialDriver)??choices.FirstOrDefault(c=>c.Name==DefaultTrialDriver(data.GetProperty("Adapter").GetString()))??choices.FirstOrDefault();trialEligible=false;TrialFilesButton.IsEnabled=Directory.Exists(Path.Combine(store.Root,"driver-trials"));}
        finally{fillingTrial=false;}
    }
    async void TrialFileChanged(object sender,SelectionChangedEventArgs e){
        if(fillingTrial||busy||PreviewOnlyFeedback())return;trialEligible=false;graphicsCopyTestState=GraphicsCopyTestState.Stale;TrialProgress.Value=0;TrialProgress.IsIndeterminate=false;TrialStage.Text=string.Empty;trialProgressPercent=0;trialProgressStage=string.Empty;RenderGraphicsPage();
        if(graphicsDriverState==GraphicsDriverState.UncheckedTestable)await Run("Checking selected driver file…",InspectTrial);
    }
    void TrialUpdate(int percent,string message){
        int safePercent=Math.Clamp(percent,0,100);TrialProgress.Visibility=Visibility.Visible;TrialProgress.IsIndeterminate=false;TrialProgress.Value=safePercent;
        trialProgressPercent=safePercent;trialProgressStage=safePercent>=100?"Done":PlainLanguage.Progress(message);
        TrialStage.Text=safePercent>=100?"Done":$"Step 2 of 3 · {trialProgressStage} · {safePercent}%";TrialLog.Text+=DateTime.Now.ToString("HH:mm:ss ")+message+Environment.NewLine;RenderTrialLog();Log(message);
    }
    void RenderTrialLog(){bool hasLog=!string.IsNullOrWhiteSpace(TrialLog.Text);TrialLogTitle.Text=hasLog?"Test log":"Test log · no entries yet";TrialLog.Visibility=hasLog?Visibility.Visible:Visibility.Collapsed;}
    async Task InspectTrial(){
        if(driverOriginal){graphicsDriverState=GraphicsDriverState.Checked;graphicsCopyTestState=GraphicsCopyTestState.NotRun;trialEligible=false;RenderGraphicsPage();return;}
        if(TrialFile.SelectedItem is not TrialFileChoice file){trialEligible=false;graphicsDriverState=GraphicsDriverState.Unsupported;graphicsCopyTestState=GraphicsCopyTestState.NotRun;TrialSummary.Text="No supported Intel driver file was found in this package.";RenderGraphicsPage();return;}
        var result=await Engine(new("InspectDriverTrial",DriverFile:file.File));var inspected=result.Data.GetProperty("Inspection");trialEligible=inspected.GetProperty("Eligible").GetBoolean();graphicsDriverState=trialEligible?GraphicsDriverState.UncheckedTestable:GraphicsDriverState.Unsupported;graphicsCopyTestState=GraphicsCopyTestState.NotRun;
        trialLimitCapabilities.Clear();foreach(var capability in result.Data.GetProperty("Capabilities").EnumerateArray()){var parsed=capability.Deserialize<DriverLimitCapability>();if(parsed!=null)trialLimitCapabilities[parsed.MiB]=parsed;}previewSizeMiB=null;
        TrialSummary.Text=trialEligible?"Arc Cache can test this driver copy without replacing the installed driver.":"Arc Cache cannot safely test this driver file.";RenderGraphicsPage();
    }
    int SelectedTrialMiB=>TrialSizeChoice.SelectedItem is ComboBoxItem selectedItem&&int.TryParse(selectedItem.Tag?.ToString(),out int selectedMiB)?selectedMiB:1024;
    void FillTrialSizes(){
        if(TrialSizeChoice==null)return;
        int previous=previewSizeMiB??SelectedTrialMiB;
        var choices=trialLimitCapabilities.Values.Where(c=>c.MiB>512&&c.Buildable).OrderBy(c=>c.MiB).ToArray();
        var existing=TrialSizeChoice.Items.OfType<ComboBoxItem>().ToArray();
        bool sameItems=existing.Length==choices.Length&&existing.Select(i=>i.Tag?.ToString()).SequenceEqual(choices.Select(c=>c.MiB.ToString()));
        fillingTrial=true;
        try{
            if(sameItems){
                // A size change calls RenderGraphicsPage. Keep the active ComboBox
                // selection transaction intact; only its status labels may have changed.
                for(int i=0;i<choices.Length;i++)existing[i].Content=SetupPresentation.Limit(choices[i].MiB)+(choices[i].Tested?" · test passed":" · test needed");
            }else{
                TrialSizeChoice.Items.Clear();
                foreach(var c in choices)TrialSizeChoice.Items.Add(new ComboBoxItem{Content=SetupPresentation.Limit(c.MiB)+(c.Tested?" · test passed":" · test needed"),Tag=c.MiB.ToString()});
                existing=TrialSizeChoice.Items.OfType<ComboBoxItem>().ToArray();
            }
            if(TrialSizeChoice.SelectedItem is null){var keep=existing.FirstOrDefault(i=>i.Tag?.ToString()==previous.ToString());TrialSizeChoice.SelectedItem=keep??existing.FirstOrDefault();}
        }finally{fillingTrial=false;}
    }
    void RenderTrialSizeGuidance(int selectedMiB){
        var locked=trialLimitCapabilities.Values.Where(c=>c.MiB>512&&!c.Buildable).OrderBy(c=>c.MiB).ToArray();
        TrialSizeReason.Text=locked.Length==0?string.Empty:"Locked sizes: "+string.Join(", ",locked.Select(c=>SetupPresentation.Limit(c.MiB)))+". "+(locked.Select(c=>c.Evidence).FirstOrDefault(e=>!string.IsNullOrWhiteSpace(e))??"This driver file could not be identified safely.");
        TrialSizeReason.Visibility=locked.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(selectedMiB>=4096){if(installedRamBytes==0)installedRamBytes=ReadInstalledRam();string memoryLine=installedRamBytes==0?"Installed memory could not be read.":"This PC has "+(installedRamBytes/1073741824.0).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+" GB installed.";TrialRamWarning.Text=selectedMiB>=8192?$"Experimental: this driver copy loads, but its cache reload has not been verified. It may use up to {selectedMiB/1024} GB of system RAM. {memoryLine}":"Experimental: this driver copy loads, but its cache reload has not been verified.";TrialRamWarning.Visibility=Visibility.Visible;return;}
        if(selectedMiB<8192){TrialRamWarning.Text=string.Empty;TrialRamWarning.Visibility=Visibility.Collapsed;return;}
        if(installedRamBytes==0)installedRamBytes=ReadInstalledRam();
        string installed=installedRamBytes==0?"Installed memory could not be read.":"This PC has "+(installedRamBytes/1073741824.0).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+" GB installed.";
        TrialRamWarning.Text=$"Highly experimental: this path may use up to {selectedMiB/1024} GB of system RAM. {installed}";
        TrialRamWarning.Visibility=Visibility.Visible;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] struct MemoryStatusEx {public uint Length;public uint MemoryLoad;public ulong TotalPhys,AvailPhys,TotalPageFile,AvailPageFile,TotalVirtual,AvailVirtual,AvailExtendedVirtual;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    static ulong ReadInstalledRam(){try{var s=new MemoryStatusEx{Length=(uint)Marshal.SizeOf<MemoryStatusEx>()};return GlobalMemoryStatusEx(ref s)?s.TotalPhys:0;}catch{return 0;}}
    bool trialSizeRenderQueued;
    void TrialSizeChanged(object sender,SelectionChangedEventArgs e){
        if(fillingTrial||busy||developerStatePreview||trialSizeRenderQueued)return;
        graphicsCopyTestState=GraphicsCopyTestState.NotRun;
        // RenderGraphicsPage refreshes this ComboBox's items and selection. Doing that
        // synchronously inside SelectionChanged can invalidate WinUI's active selection
        // transaction and terminate the process in Microsoft.UI.Xaml.dll.
        trialSizeRenderQueued=true;
        if(!DispatcherQueue.TryEnqueue(()=>{
            trialSizeRenderQueued=false;
            if(!closing&&!busy&&!developerStatePreview)RenderGraphicsPage();
        }))trialSizeRenderQueued=false;
    }
    string AvailableCacheSizes(){
        if(developerStatePreview&&statePreviewInput is not null)return statePreviewInput.AvailableSizes;
        var names=limitCapabilities.Values.Where(c=>DriverLimitChoices.IsAvailable(c.MiB,limitCapabilities,successfulTrial!=null)).OrderBy(c=>c.MiB).Select(c=>c.MiB<=512?"Default":SetupPresentation.Limit(c.MiB)).Distinct().ToArray();
        return names.Length==0?"None":string.Join(", ",names);
    }
    void RenderStep3Result(GraphicsPageState state,GraphicsCopyTestState copyState){
        Step3Result.Inlines.Clear();
        Step3Result.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=state.Step3Result});
        if(!state.ShowAmberDriverNote||!state.Step3Complete)return;
        var amber=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,250,190,95));
        Step3Result.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=" · Not pre-reviewed by Arc Cache",Foreground=amber});
        if(copyState==GraphicsCopyTestState.Passed)Step3Result.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=" · passed its test on this PC"});
    }
    void RenderGraphicsPage(){
        if(DriverPanel==null)return;
        FillTrialSizes();int testSize=SelectedTrialMiB;GraphicsCopyTestState copyState=developerStatePreview&&statePreviewInput is not null?statePreviewInput.CopyTest:trialLimitCapabilities.TryGetValue(testSize,out var selectedSizeCapability)&&selectedSizeCapability.Tested?GraphicsCopyTestState.Passed:graphicsCopyTestState==GraphicsCopyTestState.Running?GraphicsCopyTestState.Running:GraphicsCopyTestState.NotRun;
        bool controlCopyPassed=trialLimitCapabilities.TryGetValue(512,out var controlCapability)&&controlCapability.Tested;
        var input=developerStatePreview&&statePreviewInput is not null?statePreviewInput:new GraphicsPageInputs(graphicsCheckState,graphicsDriverState,graphicsWindowsState,copyState,GraphicsChoice.Items.Count>1,homeAdapter,homeVersion,AvailableCacheSizes(),chipChoiceConfirmed||GraphicsChoice.Items.Count<=1,trialProgressPercent,trialProgressStage,controlCopyPassed,SelectedTrialMiB);
        var state=GraphicsPageState.Resolve(input);graphicsPageState=state;
        Step1Mark.Text=state.Step1Complete?"✓":"1";Step2Mark.Text=state.Step2Complete?"✓":"2";Step3Mark.Text=state.Step3Complete?"✓":"3";
        Step1Result.Text=state.Step1Result;Step2Result.Text=state.Step2Result;RenderStep3Result(state,copyState);
        bool hasStep1Details=!string.IsNullOrWhiteSpace(state.Step1Body)||state.ShowChipPicker;
        Step1Details.Visibility=state.Step1Expanded&&hasStep1Details?Visibility.Visible:Visibility.Collapsed;Step2Details.Visibility=state.Step2Expanded||windowsManageOpen&&state.Step2Complete?Visibility.Visible:Visibility.Collapsed;Step3Details.Visibility=state.Step3Expanded?Visibility.Visible:Visibility.Collapsed;
        DriverSummary.Text=state.Step1Body;SupportProgress.Visibility=input.Check is GraphicsCheckState.Running or GraphicsCheckState.NotRun?Visibility.Visible:Visibility.Collapsed;SupportProgress.IsActive=SupportProgress.Visibility==Visibility.Visible;
        GraphicsChoice.Visibility=state.ShowChipPicker?Visibility.Visible:Visibility.Collapsed;GraphicsChoice.IsEnabled=!busy;
        CheckChipButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.CheckSelectedChip?Visibility.Visible:Visibility.Collapsed;CheckChipButton.Content=state.AccentButton;CheckChipButton.IsEnabled=!busy;
        ChipChangeLink.Visibility=state.Step1Complete&&input.MultipleChips?Visibility.Visible:Visibility.Collapsed;CheckAgainLink.Visibility=state.ShowCheckAgainLink?Visibility.Visible:Visibility.Collapsed;
        RetryCheckButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.CheckAgain&&state.CurrentStep==GraphicsStep.Chip?Visibility.Visible:Visibility.Collapsed;RetryCheckButton.Content=state.AccentButton;RetryCheckButton.IsEnabled=!busy;
        WindowsChangeLink.Visibility=state.Step2Complete?Visibility.Visible:Visibility.Collapsed;RoutingHint.Text=state.Step2Body;
        EnableRouteButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.EnableWindowsSetting?Visibility.Visible:Visibility.Collapsed;EnableRouteButton.Content=state.AccentButton;EnableRouteButton.IsEnabled=!busy;
        RetryWindowsButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.CheckAgain&&state.CurrentStep==GraphicsStep.Windows?Visibility.Visible:Visibility.Collapsed;RetryWindowsButton.Content=state.AccentButton;RetryWindowsButton.IsEnabled=!busy;
        RestartCheckButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.RestartAndCheck?Visibility.Visible:Visibility.Collapsed;RestartCheckButton.Content=state.AccentButton;RestartCheckButton.IsEnabled=!busy;
        bool anotherSize=trialLimitCapabilities.Values.Any(c=>c.MiB>512&&c.Buildable&&!c.Tested);Step3ChangeLink.Content=anotherSize?"Test another size":"Details";Step3ChangeLink.Visibility=state.Step3Complete&&anotherSize?Visibility.Visible:Visibility.Collapsed;TrialSizeChoice.Visibility=state.Step3Expanded&&!state.Step3Complete&&TrialSizeChoice.Items.Count>0?Visibility.Visible:Visibility.Collapsed;RenderTrialSizeGuidance(testSize);TrialSizeReason.Visibility=state.Step3Expanded&&!state.Step3Complete&&TrialSizeReason.Text.Length>0?Visibility.Visible:Visibility.Collapsed;TrialRamWarning.Visibility=state.Step3Expanded&&!state.Step3Complete&&testSize>=8192?Visibility.Visible:Visibility.Collapsed;
        bool hasTrialFiles=developerStatePreview?copyState is GraphicsCopyTestState.Passed or GraphicsCopyTestState.Failed or GraphicsCopyTestState.Stopped:Directory.Exists(Path.Combine(store.Root,"driver-trials"));
        TrialFileSelector.Visibility=input.Driver==GraphicsDriverState.UncheckedTestable&&!state.Step3Complete?Visibility.Visible:Visibility.Collapsed;TrialFile.IsEnabled=!busy;
        TrialFilesButton.Visibility=hasTrialFiles&&!developerStatePreview?Visibility.Visible:Visibility.Collapsed;DiscardTrialButton.Visibility=hasTrialFiles&&!developerStatePreview?Visibility.Visible:Visibility.Collapsed;RenderWindowsSetting();
        DriverCopyActionReason.Text=developerStatePreview?"Preview only. No test files or recovery records were created.":hasTrialFiles?string.Empty:"No driver copy is present.";DriverCopyActionReason.Visibility=developerStatePreview||!hasTrialFiles?Visibility.Visible:Visibility.Collapsed;
        DriverCopyStatus.Text=copyState switch{GraphicsCopyTestState.Passed=>$"The driver copy passed. Available cache sizes: {state.CacheSizes}. The installed driver stays unchanged.",GraphicsCopyTestState.Failed=>"The driver-copy test did not pass. No game files were changed.",GraphicsCopyTestState.Stopped=>"The driver-copy test stopped. No game files were changed.",_=>"A driver copy keeps the installed Windows driver unchanged."};
        DriverWarning.IsOpen=state.ShowAmberDriverNote&&state.Step3Expanded;DriverWarning.Title="Not pre-reviewed by Arc Cache";DriverWarning.Message=copyState is GraphicsCopyTestState.Failed or GraphicsCopyTestState.Stopped?"This exact driver hasn't been reviewed by Arc Cache.":"This exact driver hasn't been reviewed by Arc Cache. Test its driver copy before using it for a game.";
        TrialSummary.Text=state.Step3Body;TrialButton.Visibility=state.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy?Visibility.Visible:Visibility.Collapsed;TrialButton.Content=state.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy?"Test "+SetupPresentation.Limit(testSize)+" first":state.AccentButton;TrialButton.IsEnabled=!busy&&(trialEligible||developerStatePreview);
        TrialStopButton.Visibility=state.ShowStopTest?Visibility.Visible:Visibility.Collapsed;TrialStopButton.IsEnabled=developerStatePreview||driverTrialCancellation is not null;
        UpdateDriverLink.Visibility=state.ShowUpdateDriverLink?Visibility.Visible:Visibility.Collapsed;
        TrialProgress.Visibility=copyState==GraphicsCopyTestState.Running?Visibility.Visible:Visibility.Collapsed;
        if(developerStatePreview&&copyState==GraphicsCopyTestState.Running){TrialProgress.IsIndeterminate=false;TrialProgress.Value=state.CopyTestProgress;TrialStage.Text=$"Step 2 of 3 · {state.CopyTestStage} · {state.CopyTestProgress}%";}
        else if(copyState==GraphicsCopyTestState.Running){TrialStage.Text=TrialStage.Text=="Done"?string.Empty:TrialStage.Text;}
        else TrialStage.Text=TrialProgress.Value>=100?"Done":string.Empty;
        RenderTrialLog();
        GraphicsFinishCard.Visibility=state.ShowFinish?Visibility.Visible:Visibility.Collapsed;GraphicsFinishTitle.Text=state.FinishText;GraphicsFinishSizes.Text="Cache sizes available: "+state.CacheSizes;ChooseGameButton.Content=state.AccentButton;ChooseGameButton.IsEnabled=!busy;
    }    async Task GraphicsStats(){if(graphicsStatsBusy||closing)return;graphicsStatsBusy=true;try{var reply=await safety.RunAsync(new("GraphicsStats",ReadOnly:true));if(!reply.Ok)throw new IOException(reply.Message);var d=reply.Data;ulong total=d.GetProperty("RamTotal").GetUInt64(),available=d.GetProperty("RamAvailable").GetUInt64();long disk=d.GetProperty("DiskBytes").GetInt64();graphicsCacheBaseline??=disk;GraphicsRam.Text=$"{(total-available)/1073741824.0:0.0} / {total/1073741824.0:0.0} GB";GraphicsCache.Text=$"{ScsPresentation.Size(disk)} · {d.GetProperty("Files").GetInt32()} files";long grew=disk-graphicsCacheBaseline.Value;int unreadable=d.GetProperty("Unreadable").GetInt32();
            GraphicsStatsStatus.Text=string.Join(" · ",new[]{"Shared by all games",grew!=0?$"{(grew>0?"+":"−")}{ScsPresentation.Size(Math.Abs(grew))} since Arc Cache opened":null,unreadable>0?$"{unreadable} files unreadable":null,$"checked {DateTime.Now:t}"}.OfType<string>());}catch(Exception e){GraphicsStatsStatus.Text="Statistics unavailable: "+PlainLanguage.Error(e.Message);}finally{graphicsStatsBusy=false;}}
    async void RefreshGraphicsStats(object sender,RoutedEventArgs e)=>await GraphicsStats();
    async void TryDriverPatch(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;
        if(busy||!trialEligible||TrialFile.SelectedItem is not TrialFileChoice file)return;
        string selectedDriverFile=file.File;
        int requestedSize=SelectedTrialMiB;string sizeName=SetupPresentation.Limit(requestedSize);string ramWarning=requestedSize>=8192?" This setting can use up to about "+(requestedSize/1024)+" GB of system RAM; your PC has "+(GraphicsRam.Text.Split('/').LastOrDefault()?.Replace(" GB","").Trim()??"unknown")+" GB installed.":"";
        if(!await Confirm("Test a driver copy?","Arc Cache will back up the original driver and test a separate "+sizeName+" copy. It also checks the unchanged Default copy. The test stops if a check fails. A graphics reset or crash is possible. A small cache entry may be saved. No game or installed driver file is replaced. Windows security stays on. Close games before testing."+ramWarning,"Test driver copy"))return;
        TrialLog.Text="";TrialProgress.Value=0;TrialProgress.Visibility=Visibility.Visible;TrialProgress.IsIndeterminate=true;trialProgressPercent=0;trialProgressStage="Preparing the driver copy";graphicsCopyTestState=GraphicsCopyTestState.Running;TrialStage.Text="Step 1 of 3 · Preparing the driver copy · 0%";TrialSummary.Text="";driverTrialCancellation=new CancellationTokenSource();RenderGraphicsPage();
        await Run("Testing driver copy…",async()=>{
            try{
                var reply=await Engine(new("TryDriverTrial",DriverFile:selectedDriverFile,MiB:requestedSize,Confirmed:true,RiskConfirmed:true),driverTrialCancellation.Token);
                bool passed=reply.Data.GetProperty("Passed").GetBoolean();TrialProgress.Value=100;TrialProgress.IsIndeterminate=false;trialProgressPercent=100;trialProgressStage="Done";TrialStage.Text="Done";TrialFilesButton.IsEnabled=true;TrialLog.Text+=JsonSerializer.Serialize(reply.Data,new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine;
                if(passed&&reply.Data.TryGetProperty("Cleanup",out var cleanup)&&cleanup.ValueKind==JsonValueKind.Object){int removedTests=cleanup.GetProperty("RemovedFolders").GetArrayLength();if(removedTests>0)Log($"Removed {removedTests} older driver test{(removedTests==1?"":"s")} and freed {cleanup.GetProperty("BytesFreed").GetInt64()/1073741824.0:0.0} GB. The newest passing test for each cache size and the tests your games use are kept.");}
                if(passed){var tested=reply.Data.GetProperty("Patched");successfulTrialDriver=tested.GetProperty("Driver").GetString()??reply.Data.GetProperty("Control").GetProperty("LoadedCacheDriver").GetString()??file.Name;successfulTrial=tested.GetProperty("Receipt").GetString()??reply.Data.GetProperty("Control").GetProperty("Receipt").GetString();graphicsCopyTestState=GraphicsCopyTestState.Passed;await LoadDriver(true);await InspectTrial();}
                else{graphicsCopyTestState=GraphicsCopyTestState.Failed;TrialSummary.Text="Arc Cache could not verify the driver copy. Test again or use the default settings.";RenderGraphicsPage();}
            }catch(OperationCanceledException){
                graphicsCopyTestState=GraphicsCopyTestState.Stopped;TrialLog.Text+="The driver-copy test was stopped. No game files were changed."+Environment.NewLine;TrialStage.Text="Done";TrialProgress.Value=100;trialProgressPercent=100;trialProgressStage="Done";TrialProgress.IsIndeterminate=false;RenderGraphicsPage();
            }catch{
                graphicsCopyTestState=GraphicsCopyTestState.Stopped;TrialFilesButton.IsEnabled=Directory.Exists(Path.Combine(store.Root,"driver-trials"));TrialSummary.Text="The driver-copy test stopped before finishing. You can test again.";TrialStage.Text="Done";TrialProgress.Value=100;trialProgressPercent=100;trialProgressStage="Done";TrialProgress.IsIndeterminate=false;RenderGraphicsPage();throw;
            }finally{driverTrialCancellation?.Dispose();driverTrialCancellation=null;}
        });
    }
    void StopDriverTrial(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;driverTrialCancellation?.Cancel();}
    async void DiscardTrialCopy(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback()||busy)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;if(!await Confirm("Remove this driver copy?","Select experiment.json inside a driver-trials folder. Arc Cache checks every file before removing that one test folder. Changed or extra files stop removal. Game setups and shader caches stay as they are.","Remove driver copy"))return;await Run("Removing the driver copy…",async()=>{await Engine(new("DiscardDriverTrial",ReceiptPath:file.Path,Confirmed:true));successfulTrial=null;successfulTrialDriver=null;graphicsCopyTestState=GraphicsCopyTestState.NotRun;await LoadDriver(true);await InspectTrial();});}
    void ChooseGameForTrial(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;NavigateTo("Library");}
    void OpenDriverDownloads(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;Process.Start(new ProcessStartInfo("https://www.intel.com/content/www/us/en/download-center/home.html"){UseShellExecute=true});}
    async Task CheckTrialSupport(bool showStatus=false){if(busy||closing)return;SupportProgress.Visibility=Visibility.Visible;SupportProgress.IsActive=true;await Run("Checking your Intel graphics…",async()=>{try{await LoadDriver(true);if(TrialFile.SelectedItem is TrialFileChoice choice&&!String.Equals(choice.Name,loadedCapabilityDriver,StringComparison.OrdinalIgnoreCase))await InspectTrial();}finally{SupportProgress.IsActive=false;SupportProgress.Visibility=Visibility.Collapsed;RenderGraphicsPage();}},showStatus);}
    void OpenAdvancedChip(object sender,RoutedEventArgs e){chipChoiceConfirmed=false;RenderGraphicsPage();Step1Card.StartBringIntoView();}
    void OpenAdvancedRouting(object sender,RoutedEventArgs e){windowsManageOpen=!windowsManageOpen;RenderGraphicsPage();if(windowsManageOpen)DisableRouteButton.StartBringIntoView();}
    void OpenAdvancedTrial(object sender,RoutedEventArgs e){var next=limitCapabilities.Values.Where(c=>c.MiB>512&&c.Buildable&&!c.Tested).OrderBy(c=>c.MiB).FirstOrDefault();if(next!=null){fillingTrial=true;TrialSizeChoice.SelectedItem=TrialSizeChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==next.MiB.ToString());fillingTrial=false;graphicsCopyTestState=GraphicsCopyTestState.NotRun;RenderGraphicsPage();TrialCard.StartBringIntoView();}else{GraphicsAdvanced.IsExpanded=true;DriverCopySection.StartBringIntoView();}}
    void FollowGameGuide(object sender,RoutedEventArgs e){if(busy||gameGuide==null)return;switch(gameGuide.Action){case "Graphics":case "Settings":NavigateTo("Driver");break;case "Restore":RestoreGame(sender,e);break;case "Prepare":case "Apply":ApplyGame(sender,e);break;case "Launch":LaunchGame(sender,e);break;default:RecoveryExpander.IsExpanded=true;break;}}
    void OpenTrialResults(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;string root=Path.Combine(store.Root,"driver-trials");if(Directory.Exists(root))Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={root}});}
    void Navigate(NavigationView sender,NavigationViewSelectionChangedEventArgs args){string? tag=(args.SelectedItem as NavigationViewItem)?.Tag as string;if(developerMyGamesPreview&&tag!="Library"){PreviewOnlyFeedback();Nav.SelectedItem=LibraryNav;return;}ActivatePage(tag);}
    void NavigateTo(string tag){
        NavigationViewItem? item=Nav.MenuItems.OfType<NavigationViewItem>().Concat(Nav.FooterMenuItems.OfType<NavigationViewItem>()).FirstOrDefault(i=>Equals(i.Tag,tag));
        if(item is null)return;
        Nav.SelectedItem=item;
        ActivatePage(tag);
    }
    void ActivatePage(string? tag){
        if(tag is null||LibraryPanel is null)return;
        if(activePageTag==tag)return;
        activePageTag=tag;footerStatusTimer.Stop();FooterStatus.Text=string.Empty;if(tag!="Library")discoveryTimer.Stop();
        HomePanel.Visibility=tag=="Home"?Visibility.Visible:Visibility.Collapsed;LibraryPanel.Visibility=tag=="Library"?Visibility.Visible:Visibility.Collapsed;DriverPanel.Visibility=tag=="Driver"?Visibility.Visible:Visibility.Collapsed;ActivityPanel.Visibility=tag=="Activity"?Visibility.Visible:Visibility.Collapsed;SettingsPanel.Visibility=tag=="Settings"?Visibility.Visible:Visibility.Collapsed;
        if(tag=="Home"){if(!preview){graphicsStatsTimer.Start();_=GraphicsStats();}}else graphicsStatsTimer.Stop();
        if(tag=="Driver"&&!preview)_=CheckTrialSupport();
        if(tag=="Settings"){RenderWindowsSetting();if(!preview){_=MeasureDriverTests();if(!busy)_=RefreshWindowsSetting();}}
        if(tag=="Library"){RenderMyGames();if(selected is not null&&(selectedReceipt is not null&&selected.Supported||!selected.Supported))discoveryTimer.Start();}
    }
    string DriverTestsFolder=>Path.Combine(store.Root,"driver-trials");
    async Task MeasureDriverTests(){
        string folder=DriverTestsFolder;
        long bytes=await Task.Run(()=>{try{if(!Directory.Exists(folder))return 0L;long total=0;foreach(var file in new DirectoryInfo(folder).EnumerateFiles("*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=true}))total+=file.Length;return total;}catch(Exception){return -1L;}});
        driverTestsBytes=bytes;int count=0;try{if(Directory.Exists(folder))count=Directory.GetDirectories(folder).Length;}catch(Exception){}
        DriverTestsSize.Text=bytes<0?"The size of the driver tests could not be measured.":count==0?"No driver tests are saved.":$"{count} driver test{(count==1?"":"s")} use {bytes/1073741824.0:0.0} GB in {folder}";
    }
    void OpenGraphics(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;NavigateTo("Driver");}
    void OpenSettings(object sender,RoutedEventArgs e)=>NavigateTo("Settings");
    void OpenLibrary(object sender,RoutedEventArgs e)=>NavigateTo("Library");
    void HomeSetupAction(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;if(HomeSetupButton.Tag?.ToString()=="Library")OpenLibrary(sender,e);else OpenGraphics(sender,e);}
    async void HomeGameClicked(object sender,RoutedEventArgs e){if(sender is not Button b||b.Tag is not string key)return;var game=games.FirstOrDefault(g=>g.Key==key);if(game==null)return;try{store.MarkRecentGame(game.Key);}catch(Exception error){Log("Could not save recent game: "+error.Message);}NavigateTo("Library");selected=game;synchronizingSelection=true;GamesList.SelectedItem=game;synchronizingSelection=false;await Run("Checking "+game.Name+"…",ShowGame);}
    void RefreshHome(){
        if(HomeRecentGames==null)return;
        var pageState=graphicsPageState??GraphicsPageState.Resolve(new(graphicsCheckState,graphicsDriverState,graphicsWindowsState,graphicsCopyTestState));
        HomeGreeting.Text=pageState.HomeHeader;
        HomeGraphicsModel.Text=pageState.CheckState switch{GraphicsCheckState.NoIntel=>"No Intel graphics found",GraphicsCheckState.Failed=>"Not checked",GraphicsCheckState.Running or GraphicsCheckState.NotRun=>"Checking…",_=>homeAdapter};
        HomeGraphicsDriver.Text=pageState.CheckState==GraphicsCheckState.NoIntel?"Driver not available":string.IsNullOrWhiteSpace(homeVersion)?"Driver details unavailable":"Driver "+homeVersion;
        if(developerStatePreview){RefreshHomePreview(pageState);return;}
bool checking=pageState.CheckState is GraphicsCheckState.NotRun or GraphicsCheckState.Running;HomeSetupButton.Visibility=checking||pageState.IsBusy?Visibility.Collapsed:Visibility.Visible;
        if(checking){HomeSetupLabel.Text="CHECKING SETUP";HomeSetupTitle.Text=pageState.HomeTitle;HomeSetupDetails.Text="Arc Cache is checking your Intel graphics and Windows setting.";HomeSetupButton.Visibility=Visibility.Collapsed;HomeRoutingStatus.Text="Checking…";HomeRoutingDetails.Text="Waiting for the graphics check.";HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,39,41,45));HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,65,69,76));}
        else if(pageState.CheckState is GraphicsCheckState.Failed or GraphicsCheckState.NoIntel){HomeSetupLabel.Text="GRAPHICS CHECK";HomeSetupTitle.Text=pageState.HomeTitle;HomeSetupDetails.Text="Open Graphics to review the message and try again.";HomeSetupButton.Content="Open Graphics";HomeSetupButton.Tag="Graphics";HomeRoutingStatus.Text="Needs attention";HomeRoutingDetails.Text="Open Graphics to see what to do next.";}
        else if(driverOriginal&&routingReady){HomeSetupLabel.Text="SETUP COMPLETE";HomeSetupTitle.Text="Windows is ready for game-specific settings";HomeSetupDetails.Text=$"{homeAdapter} · Driver {homeVersion}";HomeSetupButton.Content="Choose a game";HomeSetupButton.Tag="Library";HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,24,56,44));HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,40,90,67));HomeRoutingStatus.Text="On";HomeRoutingStatus.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,127,225,174));HomeRoutingDetails.Text="Needed for per-game driver copies.";}
        else if(pageState.CurrentStep==GraphicsStep.DriverCopy&&pageState.IsBusy){HomeSetupLabel.Text="DRIVER COPY TEST";HomeSetupTitle.Text="Testing driver copy";HomeSetupDetails.Text="Your installed driver and game files are unchanged.";HomeSetupButton.Visibility=Visibility.Collapsed;HomeRoutingStatus.Text=pageState.Step2Result;HomeRoutingDetails.Text="Needed for per-game driver copies.";}
        else if(pageState.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy){HomeSetupLabel.Text="ONE STEP LEFT";HomeSetupTitle.Text="Test the driver copy";HomeSetupDetails.Text="Open Graphics to run the separate driver-copy check.";HomeSetupButton.Content="Open Graphics";HomeSetupButton.Tag="Graphics";HomeRoutingStatus.Text=pageState.Step2Result;HomeRoutingDetails.Text="Needed for per-game driver copies.";}
        else if(!driverOriginal&&successfulTrial!=null&&routingReady){HomeSetupLabel.Text="GRAPHICS CHECK PASSED";HomeSetupTitle.Text="Choose a game to set up";HomeSetupDetails.Text="Your installed driver stays unchanged. Each game gets its own driver copy.";HomeSetupButton.Content="Choose a game";HomeSetupButton.Tag="Library";HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,31,43,55));HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,58,79,101));HomeRoutingStatus.Text="On";HomeRoutingStatus.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,127,225,174));HomeRoutingDetails.Text="Needed for per-game driver copies.";}
        else if(pageState.Step2Result=="Restart required"){HomeSetupLabel.Text="RESTART WINDOWS";HomeSetupTitle.Text="Restart Windows to finish setup";HomeSetupDetails.Text="Save your work, restart, then return here to choose a game.";HomeSetupButton.Content="View Graphics";HomeSetupButton.Tag="Graphics";HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,48,43,30));HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,104,82,38));HomeRoutingStatus.Text="Restart needed";HomeRoutingDetails.Text="Restart Windows to finish.";}
        else {HomeSetupLabel.Text=successfulTrial!=null?"GRAPHICS CHECK PASSED":"ONE STEP LEFT";HomeSetupTitle.Text=successfulTrial!=null?"Finish Windows setup to use this copy":"Enable game-specific settings";HomeSetupDetails.Text=successfulTrial!=null?"The graphics test passed. Open Graphics to turn on the Windows setting.":driverOriginal?"Open Graphics and turn on the Windows setting. A restart may be needed.":"Open Graphics to check whether your Intel driver is supported.";HomeSetupButton.Content=driverOriginal||successfulTrial!=null?"Finish setup":"Check support";HomeSetupButton.Tag="Graphics";HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,48,43,30));HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,104,82,38));HomeRoutingStatus.Text=driverOriginal||successfulTrial!=null?"Off":"Not checked";HomeRoutingStatus.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,250,190,95));HomeRoutingDetails.Text=driverOriginal||successfulTrial!=null?"Turn it on in Graphics.":"Check support on the Graphics page.";}
        SetHomeEyebrowTone(pageState.CheckState is GraphicsCheckState.Failed or GraphicsCheckState.NoIntel,pageState.ShowFinish||HomeSetupLabel.Text is "SETUP COMPLETE" or "GRAPHICS CHECK PASSED");
        if(developerStatePreview){HomeSetupButton.Visibility=Visibility.Collapsed;HomeRecentGames.Children.Clear();HomeEmptyGames.Text="No games are loaded in this fake-data preview.";HomeEmptyGames.Visibility=Visibility.Visible;return;}
        var recents=store.ReadRecentGames().Select(k=>games.FirstOrDefault(g=>string.Equals(g.Key,k,StringComparison.OrdinalIgnoreCase))).Where(g=>g!=null).Cast<SteamGame>().ToList();if(recents.Count==0)recents.AddRange(games.Where(g=>File.Exists(store.Receipt(g))).OrderByDescending(g=>File.GetLastWriteTimeUtc(store.Receipt(g))));if(selected!=null&&!recents.Any(g=>g.Key==selected.Key))recents.Insert(0,selected);foreach(var game in games)if(recents.Count<3&&!recents.Any(g=>g.Key==game.Key))recents.Add(game);HomeRecentGames.Children.Clear();HomeEmptyGames.Visibility=recents.Count==0?Visibility.Visible:Visibility.Collapsed;
        foreach(var game in recents.Take(3)){string status=GameSummary(game).Line;var row=new Grid{ColumnSpacing=12,Margin=new Thickness(12)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(38)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var initials=new TextBlock{Text=string.Concat(game.Name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s=>char.ToUpperInvariant(s[0]))),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var tileContent=new Grid();tileContent.Children.Add(initials);var icon=ArtworkImages.Get(game.IconSources,76);if(icon!=null){var image=new Image{Source=icon,Stretch=Microsoft.UI.Xaml.Media.Stretch.UniformToFill};image.ImageFailed+=(_,_)=>image.Opacity=0;tileContent.Children.Add(image);}var tile=new Border{Width=38,Height=38,CornerRadius=new CornerRadius(8),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,55,70,78)),Child=tileContent};Grid.SetColumn(tile,0);row.Children.Add(tile);var labels=new StackPanel{Spacing=2,VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(new TextBlock{Text=game.Name,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});labels.Children.Add(new TextBlock{Text=game.Source+" · "+status,FontSize=12,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,180,180,180)),TextTrimming=TextTrimming.CharacterEllipsis});Grid.SetColumn(labels,1);row.Children.Add(labels);var arrow=new FontIcon{Glyph="\uE76C",FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,2);row.Children.Add(arrow);var surface=new Border{CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,65,69,76)),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,39,41,45)),Child=row};var button=new Button{Tag=game.Key,Content=surface,Padding=new Thickness(0),Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0,0,0,0)),BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch};button.Click+=HomeGameClicked;HomeRecentGames.Children.Add(button);}
    }
    void RefreshHomePreview(GraphicsPageState state){
        
        HomeRecentGames.Children.Clear();HomeEmptyGames.Text="No games are loaded in this fake-data preview.";HomeEmptyGames.Visibility=Visibility.Visible;
        HomeSetupButton.Visibility=Visibility.Visible;HomeSetupButton.Tag="Graphics";HomeSetupButton.Content="Open Graphics";
        HomeRoutingStatus.Text=state.CheckState switch{GraphicsCheckState.Running or GraphicsCheckState.NotRun=>"Checking…",GraphicsCheckState.NoIntel or GraphicsCheckState.Failed=>"Waiting for graphics check",_=>state.Step2Result};
        HomeRoutingDetails.Text=state.CheckState switch{GraphicsCheckState.NoIntel=>"Not checked.",GraphicsCheckState.Failed=>"Check the message under Graphics.",_=>state.Step2Body.Length>0?state.Step2Body:"Needed for per-game driver copies."};
        string label,title,details;
        if(state.CheckState is GraphicsCheckState.Running or GraphicsCheckState.NotRun){label="CHECKING SETUP";title="Checking your graphics";details="Arc Cache is checking for an Intel graphics chip.";HomeSetupButton.Visibility=Visibility.Collapsed;}
        else if(state.CheckState==GraphicsCheckState.NoIntel){label="NEEDS ATTENTION";title="Graphics setup needs attention";details="Arc Cache needs an Intel Arc or Xe graphics chip.";HomeGraphicsModel.Text="No Intel graphics found";}
        else if(state.CheckState==GraphicsCheckState.Failed){label="NEEDS ATTENTION";title="Graphics check could not finish";details="Check the Intel driver installation, then try again under Graphics.";}
        else if(state.ShowFinish){label="READY FOR GAMES";title="Choose a game";details="Cache sizes available: "+state.CacheSizes;HomeSetupButton.Content="Choose a game";HomeSetupButton.Tag="Library";}
        else if(state.IsBusy&&state.CurrentStep==GraphicsStep.DriverCopy){label="DRIVER COPY TEST";title="Testing driver copy";details="Your installed driver and game files are unchanged.";HomeSetupButton.Visibility=Visibility.Collapsed;}
        else if(state.Step2Result=="Restart required"){label="RESTART WINDOWS";title="Restart Windows to finish";details="Return to Graphics after restarting.";}
        else if(state.PrimaryAction==GraphicsPrimaryAction.TestDriverCopy){label="ONE STEP LEFT";title="Test the driver copy";details="Open Graphics to check a temporary driver copy.";}
        else if(state.PrimaryAction==GraphicsPrimaryAction.EnableWindowsSetting){label="ONE STEP LEFT";title="Turn on game-specific settings";details="Open Graphics to follow the Windows setup step.";}
        else {label="GRAPHICS SETUP";title="Finish graphics setup";details="Open Graphics to see the next step.";}
        HomeSetupLabel.Text=label;HomeSetupTitle.Text=title;HomeSetupDetails.Text=details;SetHomeEyebrowTone(state.CheckState is GraphicsCheckState.Failed or GraphicsCheckState.NoIntel,state.ShowFinish);
        var fill=state.CheckState is GraphicsCheckState.NoIntel or GraphicsCheckState.Failed
            ?Windows.UI.Color.FromArgb(255,61,48,30)
            :state.ShowFinish?Windows.UI.Color.FromArgb(255,24,56,44):Windows.UI.Color.FromArgb(255,39,41,45);
        var stroke=state.CheckState is GraphicsCheckState.NoIntel or GraphicsCheckState.Failed
            ?Windows.UI.Color.FromArgb(255,128,94,38)
            :state.ShowFinish?Windows.UI.Color.FromArgb(255,40,90,67):Windows.UI.Color.FromArgb(255,65,69,76);
        HomeSetupCard.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(fill);HomeSetupCard.BorderBrush=new Microsoft.UI.Xaml.Media.SolidColorBrush(stroke);
    }
    void SetHomeEyebrowTone(bool attention,bool ready){var color=attention?Windows.UI.Color.FromArgb(255,250,190,95):ready?Windows.UI.Color.FromArgb(255,127,225,174):Windows.UI.Color.FromArgb(255,180,180,180);HomeSetupLabel.Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(color);}
    async void RefreshGame(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;await Run("Refreshing installed setup…",ShowGame);}
    async void RefreshLibrary(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;await Run("Refreshing installed games…",LoadLibrary);}
    void SearchChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args){if(GamesList is not null)Filter();}
    async void GameSelected(object sender,SelectionChangedEventArgs e){if(busy||synchronizingSelection||PreviewOnlyFeedback())return;var next=GamesList.SelectedItem as SteamGame;if(next?.Key==selected?.Key)return;selected=next;myGamesChangeStep=0;gameLoadResult=MyGamesLoadResult.Unknown;if(next!=null)try{store.MarkRecentGame(next.Key);}catch(Exception error){Log("Could not save recent game: "+error.Message);}await Run("Checking this game's setup…",ShowGame);}
    async void InspectDriver(object sender,RoutedEventArgs e){if(PreviewOnlyFeedback())return;await CheckTrialSupport(true);}
    async void CheckSelectedChip(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;
        if(busy||GraphicsChoice.SelectedItem is not GraphicsChipChoice choice)return;
        chipChoiceConfirmed=true;
        await Run("Checking the selected graphics chip…",async()=>{fillingTrial=true;TrialFile.SelectedItem=null;fillingTrial=false;graphicsCopyTestState=GraphicsCopyTestState.Stale;await Engine(new("SelectGraphics",Confirmed:true,AdapterId:choice.Id));await LoadDriver(true);await InspectTrial();if(selected!=null)await ShowGame();});
    }
    async void ApplyGame(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||ScsBlocks("change this game's setup"))return;
        SteamGame? game=selected;if(game==null)return;
        // Never return silently: the button is visible, so the user must see why nothing happens.
        if(busy){GlobalNotice.Severity=InfoBarSeverity.Informational;GlobalNotice.Title="Arc Cache is still working";GlobalNotice.Message="Wait until the current check finishes, then try again.";GlobalNotice.IsOpen=true;return;}
        if(game.Supported!=true){GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="This game's setup needs attention first";GlobalNotice.Message=string.IsNullOrWhiteSpace(discoveryMessage)?"Start the game once in DirectX 12 so Arc Cache can find its files, then try again.":discoveryMessage;GlobalNotice.IsOpen=true;Log("Size change stopped: "+GlobalNotice.Message);return;}
        bool trialMode=TrialMode;string? sourceTrial=successfulTrial;
        if(trialMode){
            await Run("Checking this game's setup…",async()=>{await DiscoverSelected();RenderMyGames();});
            if(selected?.Key!=game.Key)return;game=selected;
            if(!game.TrialProfileReady){GlobalNotice.Severity=InfoBarSeverity.Informational;GlobalNotice.Title="One game check is still needed";GlobalNotice.Message="Start the game once with the normal Intel driver in DirectX 12. Arc Cache will confirm the game files before it creates the private copy.";GlobalNotice.IsOpen=true;return;}
            trialMode=TrialMode;sourceTrial=successfulTrial;
        }
        if(CapChoice.SelectedItem is not ComboBoxItem chosenSize){GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Choose a cache size";GlobalNotice.Message="Select a cache size, then apply it.";GlobalNotice.IsOpen=true;return;}
        setupOperationRunning=true;selectedCacheMiB=int.Parse(chosenSize.Tag.ToString()!);gameLoadResult=MyGamesLoadResult.Unknown;myGamesChangeStep=0;RenderMyGames();
        await Run("Checking safety and confirming this private change…",async()=>{
        string receipt=store.Receipt(game);int mib=int.Parse(((ComboBoxItem)CapChoice.SelectedItem).Tag.ToString()!);
        bool configured=setupState?.Configured==true;
        string message=configured?$"Set {game.Name}'s cache size to {SetupPresentation.Limit(mib)}? Close the game first. Only this game's separate graphics files will change. Windows and other games keep their normal driver.":$"Set up {game.Name}? Arc Cache will create a separate set of Intel graphics files without changing the installed driver. It checks free disk space before starting. Close the game first. If a graphics recording mod prevents setup, disable it yourself; Arc Cache will not remove it. Start the game once so the new files can be checked before a size change.";
        // Recheck before showing consent; the worker independently repeats this check.
        WorkerReply assessment;try{assessment=await safety.RunAsync(new("Safety",SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory));}catch(Exception ex){assessment=new(false,ex.Message,default);}
        bool riskRequired=SafetyPresentation.RequiresPerGameAcknowledgement(assessment.Data,game.Source,mib,TrialMode,assessment.Ok);
        if(riskRequired)message="This game may have multiplayer or anti-cheat. A modified driver can cause account penalties or stop it from starting.\n\n"+message;
        if(game.Source is "HoYoPlay" or "Kuro" && mib>512)message="Likely kernel anti-cheat: this game may block a modified Intel driver or penalise your account. Use Default for online play. Arc Cache requires a separate confirmation for this game's larger cache size.\n\n"+message;
        if(mib>=8192)message+="\n\n8 GB and 16 GB have not been tested in games. Starting a game may use roughly that much extra RAM and can crash or fail to start. Actual cache growth is unknown. Stop the game if RAM rises quickly. You can choose a smaller size or return to Default.";
        else if(mib>=2048)message+="\n\nLarger limits can increase disk use and RAM needed while loading. Effective cache capacity and performance improvements remain unmeasured. Use default settings to undo this game setup.";
        if(TrialMode)message+="\n\nThis driver copy passed its own isolated test, but has not been tested in this game. It could cause launch problems or crashes. Use single-player/offline play; multiplayer or anti-cheat may reject it or penalise your account. Use Default to undo this game's setting.";
        if(!await Confirm(riskRequired?"Apply anyway?":configured?"Change this game’s cache size":"Set up this game",message,configured?"Apply":"Set up",requireOriginalTest:configured&&mib>=8192,requireRisk:riskRequired))return;
            if(trialMode!=TrialMode){await ShowGame();GlobalNotice.Severity=InfoBarSeverity.Informational;GlobalNotice.Title="Graphics check changed";GlobalNotice.Message="The selected driver changed while you were confirming. Review the game steps and try again.";GlobalNotice.IsOpen=true;return;}
            if(configured)await Engine(new(trialMode?"ChangeGameTrial":"Change",ReceiptPath:receipt,MiB:mib,Confirmed:true,RiskConfirmed:riskRequired));
            else await Engine(new(trialMode?"PrepareGameTrial":"Prepare",game.SupportedExecutable,receipt,MiB:512,Confirmed:true,RiskConfirmed:riskRequired,SourceReceipt:trialMode?sourceTrial:null));
            await ShowGame();
            GlobalNotice.Severity=InfoBarSeverity.Success;GlobalNotice.Title=configured?"Limit installed":"Original copy prepared";
            GlobalNotice.Message=configured?$"{SetupPresentation.Limit(mib)} set for {game.Name}. Setup files checked. Start the game to check whether it loaded them. Performance improvements have not been measured.":"Next: start the game through its launcher in DirectX 12 for the automatic loading check. Your selected limit is saved.";
            GlobalNotice.IsOpen=true;
        });
    }
    async void RestoreGame(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||ScsBlocks("return this game to default settings"))return;
        if(selected?.SupportedExecutable==null)return;string receipt=store.Receipt(selected);
        if(!await Confirm("Use default settings for this game?","Close the game first. Arc Cache will remove only the setup files it created, after checking the saved record. Files that changed or were added will stop removal. Your shader cache and the Windows setting stay as they are.","Use default settings"))return;
        await Run("Removing this game's verified private set…",async()=>{await Engine(new("Remove",ReceiptPath:receipt,Confirmed:true));await ShowGame();});
    }
    async void ObserveGame(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback())return;
        string? executable=selected?.SupportedExecutable;
        if(executable==null){GlobalNotice.Title="Start this game first";GlobalNotice.Message="Start this game in DirectX 12 so Arc Cache can check its setup.";GlobalNotice.IsOpen=true;return;}
        await Run("Checking this game's loaded Intel driver…",async()=>{
            var result=await Engine(new("ObserveGame",executable));RuntimeDetails.Text=result.Message;
            if(result.Message.Contains(".local\\",StringComparison.OrdinalIgnoreCase))gameLoadResult=MyGamesLoadResult.DriverCopyLoaded;
            else if(result.Message.Contains(".dll",StringComparison.OrdinalIgnoreCase))gameLoadResult=MyGamesLoadResult.NormalDriverLoaded;
            else gameLoadResult=MyGamesLoadResult.Unknown;
            RenderMyGames();
        });
    }
    // On-screen stats: one overlay at a time, for monitorGameKey. Hiding it while its game runs keeps auto-start from
    // reopening it until that game closes (overlaySuppressedKey).
    string? monitorGameKey,overlaySuppressedKey;OverlaySettings overlaySettings=new();string overlaySettingsPath="";bool overlaySettingsLoading;
    readonly DispatcherTimer overlayWatchTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool MonitorRunning=>cacheMonitor is {HasExited:false};
    void StopCacheMonitor() {
        if(cacheMonitor!=null){if(!cacheMonitor.HasExited)cacheMonitor.Kill();cacheMonitor.Dispose();cacheMonitor=null;}
        monitorGameKey=null;RenderMonitorButton();
    }
    void RenderMonitorButton()=>MonitorButton.Content=MonitorRunning&&monitorGameKey==selected?.Key?"Hide on-screen stats":"Show on-screen stats";
    async Task StartCacheMonitor(SteamGame game,int mib){
        string exe=game.SupportedExecutable??throw new InvalidOperationException("Launch this game in DirectX 12 first so Arc Cache can find its game files.");
        StopCacheMonitor();var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"ArcCache.SafetyEngine.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        var link=cacheFiles?.For(exe);
        foreach(string arg in new[]{"--cache-monitor",exe,mib.ToString(System.Globalization.CultureInfo.InvariantCulture),link?.File??"-",
            (scsStatuses.GetValueOrDefault(game.Key)?.EstimateBytes??0).ToString(System.Globalization.CultureInfo.InvariantCulture),new string(game.Name.Where(c=>!char.IsControl(c)).Take(200).ToArray()),overlaySettings.Corner})info.ArgumentList.Add(arg);
        cacheMonitor=Process.Start(info)??throw new IOException("The cache monitor could not start.");monitorGameKey=game.Key;
        await Task.Delay(1000);
        if(cacheMonitor.HasExited){string result=await cacheMonitor.StandardOutput.ReadToEndAsync();StopCacheMonitor();throw new IOException("Monitor could not start: "+result);}
        RenderMonitorButton();
        Log(link is {} shown?$"On-screen stats started for {game.Name}: its linked Intel cache file ({shown.File[..8]}…). Borderless or windowed display required; CSV saved under Activity logs.":$"On-screen stats started for {game.Name}: its cache file isn't linked yet, so the overlay shows the file that grows most. Prepare shaders once to link it. Borderless or windowed display required; CSV saved under Activity logs.");
    }
    async void ToggleCacheMonitor(object sender,RoutedEventArgs e) {
        if(MonitorRunning&&monitorGameKey==selected?.Key){if(GameProcessRunning(selected?.SupportedExecutable))overlaySuppressedKey=selected?.Key;StopCacheMonitor();Log("On-screen stats hidden.");return;}
        if(selected?.Supported!=true){GlobalNotice.Title="Start this game first";GlobalNotice.Message="Launch this game in DirectX 12 first so Arc Cache can find its game files.";GlobalNotice.IsOpen=true;return;}
        var game=selected;int mib=selectedReceipt?.MiB??512;overlaySuppressedKey=null;
        await Run("Opening on-screen stats…",()=>StartCacheMonitor(game,mib));
    }
    // Auto-start: every 5 s, the first running set-up game gets the overlay. Only reads process names and setup records.
    async Task WatchForGames(){
        if(!overlaySettings.AutoStart||closing||busy||MonitorRunning)return;
        if(cacheMonitor!=null)StopCacheMonitor(); // its game closed
        foreach(var game in games){
            if(game.SupportedExecutable is not string exe||!File.Exists(store.Receipt(game)))continue;
            if(GameSummary(game).Badge is not ("Ready" or "Set up"))continue;
            if(!GameProcessRunning(exe)){if(overlaySuppressedKey==game.Key)overlaySuppressedKey=null;continue;}
            if(overlaySuppressedKey==game.Key)continue;
            try{await StartCacheMonitor(game,ReceiptMiB(game));}
            catch(Exception e){overlaySuppressedKey=game.Key;if(GameProcessRunning(exe))Log("On-screen stats could not start for "+game.Name+": "+e.Message);} // a game that just closed needs no message
            return;
        }
    }
    int ReceiptMiB(SteamGame game){
        try{using var doc=JsonDocument.Parse(File.ReadAllText(store.Receipt(game)));return doc.RootElement.TryGetProperty("MiB",out var m)&&m.TryGetInt32(out int v)?v:512;}
        catch(Exception e) when (e is JsonException or IOException or UnauthorizedAccessException){return 512;}
    }
    void LoadOverlaySettings(){
        overlaySettingsPath=Path.Combine(store.Root,"overlay-settings.json");overlaySettings=OverlaySettings.Load(overlaySettingsPath);
        overlaySettingsLoading=true;
        try{
            OverlayAutoStart.IsOn=overlaySettings.AutoStart;OverlayCorner.Items.Clear();
            foreach(var (tag,label) in OverlaySettings.Corners)OverlayCorner.Items.Add(new ComboBoxItem{Content=label,Tag=tag});
            OverlayCorner.SelectedItem=OverlayCorner.Items.OfType<ComboBoxItem>().First(i=>(string)i.Tag==overlaySettings.Corner);
        }finally{overlaySettingsLoading=false;}
        if(overlaySettings.AutoStart)overlayWatchTimer.Start();
    }
    async void OverlaySettingChanged(object sender,RoutedEventArgs e){
        if(overlaySettingsLoading)return;
        string corner=OverlayCorner.SelectedItem is ComboBoxItem {Tag:string tag}?tag:"top-right";bool moved=corner!=overlaySettings.Corner;
        overlaySettings=new(OverlayAutoStart.IsOn,corner);
        try{overlaySettings.Save(overlaySettingsPath);}catch(Exception error) when (error is IOException or UnauthorizedAccessException){Log("On-screen stats setting could not be saved: "+error.Message);}
        if(overlaySettings.AutoStart)overlayWatchTimer.Start();else overlayWatchTimer.Stop();
        // The overlay reads its corner when it starts: reopen a running one in the new place.
        if(moved&&MonitorRunning&&games.FirstOrDefault(g=>g.Key==monitorGameKey) is {} shown){try{await StartCacheMonitor(shown,ReceiptMiB(shown));}catch(Exception error){Log("On-screen stats could not move: "+error.Message);}}
        else if(overlaySettings.AutoStart)await WatchForGames();
        FooterStatus.Text=moved?"On-screen stats: "+OverlaySettings.Corners.First(c=>c.Tag==corner).Label.ToLowerInvariant():overlaySettings.AutoStart?"On-screen stats open when a set-up game starts":"On-screen stats open only from a game's page";footerStatusTimer.Stop();footerStatusTimer.Start();
    }
    void OverlayCornerChanged(object sender,SelectionChangedEventArgs e)=>OverlaySettingChanged(sender,new RoutedEventArgs());
    async void LaunchGame(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||ScsBlocks("start the game"))return;
        if(selected==null)return;SteamGame game=selected;string id=game.Key;
        await CheckDriverChanged();
        await Run("Checking this game before launching…",async()=>{
            if(game.SupportedExecutable!=null) {
                var state=GameSetupState.FromReply(await Engine(new("GameState",game.SupportedExecutable,store.Receipt(id))));
                if(state.FolderExists) {
                    if(!routingReady)throw new InvalidOperationException("Open Graphics, enable game-specific settings, and restart Windows before starting this game. To use the normal driver, choose Use default settings first.");
                    if(!GameDriverReady)throw new InvalidOperationException("Driver compatibility needs review. Check support or return this game to the system driver first.");
                    if(!game.Supported)throw new InvalidOperationException("Game executable changed. Return its private set to stock before launching for rediscovery.");
                    if(state.CompatibilityRepairRequired)throw new InvalidOperationException("This private setup needs a compatibility update. Use default settings, then prepare a fresh original 512 MiB copy before launching.");
                    if(!state.Configured)throw new InvalidOperationException("Existing private setup is unverified. Import its recovery record or recover it before launching through this app.");
                    await Engine(new("ValidateForLaunch",ReceiptPath:store.Receipt(id)));
                    var assessment=(await Engine(new("Safety",SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory))).Data;
                    if(!assessment.GetProperty("Allowed").GetBoolean()&&!await Confirm("This game may reject custom settings","This game may have multiplayer or anti-cheat. Playing with a modified driver can cause account penalties or launch failures. Use the original driver for online play.","Launch anyway",requireRisk:true))return;
                }
            }
            GameLaunch.Start(GameLaunch.For(game));gameLoadResult=MyGamesLoadResult.Unknown;myGamesChangeStep=0;discoveryTimer.Start();Log("Launch action requested for "+game.Source+". Launching is not proof of driver loading.");RenderMyGames();
        });
    }
    async void ImportSetup(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||ScsBlocks("import a setup"))return;
        if(selected==null)return;string id=selected.Key;
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        await Run("Verifying an existing recovery receipt…",async()=>{
            await Engine(new("Import",selected!.SupportedExecutable,store.Receipt(id),Confirmed:true,SourceReceipt:file.Path,SteamAppId:selected.AppId,InstallDirectory:selected.InstallDirectory,LibrarySource:selected.Source,ExternalId:selected.ExternalId));await ShowGame();
        });
    }
    async void SourceChanged(object sender,SelectionChangedEventArgs e){if(SourceFilter==null||GamesList==null||busy)return;if(developerMyGamesPreview){PreviewOnlyFeedback();Filter();return;}Filter();var next=GamesList.SelectedItem as SteamGame??GamesList.Items.OfType<SteamGame>().FirstOrDefault();if(next?.Key==selected?.Key)return;synchronizingSelection=true;try{GamesList.SelectedItem=next;selected=next;}finally{synchronizingSelection=false;}await Run("Showing this library…",ShowGame);}
    async void AddManualGame(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;
        if(busy)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".exe");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        await Run("Adding your selected game…",async()=>{var added=new ManualGameStore(store.Root).Add(file.Path);await LoadLibrary();SourceFilter.SelectedItem="All libraries";Filter();GamesList.SelectedItem=games.First(g=>g.Key==added.Key);selected=GamesList.SelectedItem as SteamGame;await ShowGame();});
    }
    async void RemoveManualGame(object sender,RoutedEventArgs e){
        if(PreviewOnlyFeedback())return;
        if(busy||selected?.Source!="Manual")return;var game=selected;
        if(setupState?.FolderExists==true||File.Exists((game.ManualExecutable??"")+".local")||Directory.Exists((game.ManualExecutable??"")+".local")){GlobalNotice.Title="Use default settings first";GlobalNotice.Message="Remove this game's custom setup before removing it from the library.";GlobalNotice.IsOpen=true;return;}
        if(!await Confirm("Remove this library entry?","The game and its shader caches stay on disk. Only the manual library entry is removed.","Remove entry"))return;
        await Run("Removing the manual entry…",async()=>{new ManualGameStore(store.Root).Remove(game.Key);await LoadLibrary();});
    }
    async void ChooseSteamFolder(object sender,RoutedEventArgs e) {
        var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var folder=await picker.PickSingleFolderAsync();if(folder==null)return;steamRoot=folder.Path;
        await Run("Reading the selected Steam folder…",LoadLibrary);
    }
    async void EnableRouting(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||busy)return;
        string what="Windows needs this one-time setting (DevOverrideEnable) so a game can load the separate Intel graphics files Arc Cache puts in a folder next to it, such as Game.exe.local. Without it, Windows ignores that folder and the game keeps using the installed driver. The setting applies to the whole PC; only programs that have such a folder are affected. It does not replace your installed driver or turn off Windows security. Restart Windows afterward. Arc Cache saves the previous value so you can turn it off again in Settings.";
        if(vanguardInstalled)what+="\n\n"+VanguardNote;
        if(!isAdministrator){await ReopenAsAdministrator("Turn on game-specific settings?",what);return;}
        if(!await Confirm("Turn on game-specific settings?",what,"Turn on"))return;
        await Run("Turning on game-specific settings…",async()=>{await Engine(new("EnableOverride",Confirmed:true));await LoadDriver(true);await InspectTrial();});
    }
    async void RestoreRouting(object sender,RoutedEventArgs e) {
        if(PreviewOnlyFeedback()||busy)return;
        await RefreshWindowsSetting();
        string? reason=TurnOffBlockedReason();if(reason!=null){await new ContentDialog{XamlRoot=Root.XamlRoot,Title="Game-specific settings stay on",Content=reason,CloseButtonText="Close",RequestedTheme=Root.RequestedTheme}.ShowAsync();return;}
        string what="Arc Cache puts back the value Windows had before Arc Cache turned this on. No game is using a driver copy anymore, so nothing else changes. Your shader caches are not deleted. Restart Windows afterward. You can turn it on again later.";
        if(!isAdministrator){await ReopenAsAdministrator("Turn off game-specific settings?",what);return;}
        if(!await Confirm("Turn off game-specific settings?",what,"Turn off"))return;
        await Run("Turning off game-specific settings…",async()=>{await Engine(new("RestoreOverride",Confirmed:true));await LoadDriver(true);await InspectTrial();});
    }
    void ApplyRoutingFacts(JsonElement d){isAdministrator=d.TryGetProperty("Administrator",out var admin)&&admin.ValueKind==JsonValueKind.True;routingTurnOffAvailable=d.TryGetProperty("TurnOffAvailable",out var turnOff)&&turnOff.ValueKind==JsonValueKind.True;routingTurnOffRestart=d.TryGetProperty("TurnOffRestartRequired",out var offRestart)&&offRestart.ValueKind==JsonValueKind.True;gamesUsingCopies=d.TryGetProperty("GamesUsingCopies",out var copies)&&copies.ValueKind==JsonValueKind.Array?copies.EnumerateArray().Select(x=>GameNameFor(x.GetString()??"")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray():[];}
    // Games may have been returned to default since the last graphics check; re-read the facts the turn-off decision depends on.
    async Task RefreshWindowsSetting(){
        if(preview||developerStatePreview)return;
        try{var d=(await Engine(new("Status",ReadOnly:true))).Data;ApplyRoutingFacts(d);bool enabled=d.GetProperty("DeveloperOverride").GetBoolean(),reboot=d.GetProperty("RebootRequired").GetBoolean();graphicsWindowsState=reboot?GraphicsWindowsState.RestartRequired:enabled?GraphicsWindowsState.On:GraphicsWindowsState.Off;routingReady=enabled&&!reboot;RenderGraphicsPage();}
        catch(Exception error){Log("The Windows setting could not be checked: "+error.Message);}
    }
    // A game record names its executable (or, when unreadable, the record file); show the library name when the game is known.
    string GameNameFor(string path){
        // The set-up executable is often a shipping exe deeper in the folder than the one the library lists, so fall back to the install folder that contains it.
        var game=games.FirstOrDefault(g=>string.Equals(g.SupportedExecutable,path,StringComparison.OrdinalIgnoreCase)||string.Equals(g.ManualExecutable,path,StringComparison.OrdinalIgnoreCase))
            ??games.Where(g=>!string.IsNullOrWhiteSpace(g.InstallDirectory)&&path.StartsWith(Path.TrimEndingDirectorySeparator(g.InstallDirectory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderByDescending(g=>g.InstallDirectory.Length).FirstOrDefault();
        return game?.Name??Path.GetFileNameWithoutExtension(path);}
    // Why turning the Windows setting off is not possible right now, or null when it is.
    string? TurnOffBlockedReason(){
        if(graphicsWindowsState!=GraphicsWindowsState.On&&graphicsWindowsState!=GraphicsWindowsState.RestartRequired)return "Game-specific settings are already off.";
        if(!routingTurnOffAvailable)return "This setting was already on before Arc Cache turned it on, so Arc Cache leaves it as it is.";
        if(gamesUsingCopies.Length>0){string names=gamesUsingCopies.Length<=3?string.Join(", ",gamesUsingCopies):string.Join(", ",gamesUsingCopies.Take(3))+$" and {gamesUsingCopies.Length-3} more";return $"Not yet: {names} still use{(gamesUsingCopies.Length==1?"s":"")} a driver copy. Choose Use default settings for {(gamesUsingCopies.Length==1?"it":"each")} in My games first.";}
        return null;
    }
    // Windows only lets an elevated process change this setting. The user approves the Windows prompt; nothing is elevated silently.
    async Task ReopenAsAdministrator(string title,string what){
        if(ScsBlocks("reopen Arc Cache"))return;
        if(!await Confirm(title,what+"\n\nChanging this needs administrator rights. Arc Cache will close and open again as administrator; Windows asks for your approval. Then choose this option again.","Reopen as administrator"))return;
        try{Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",Arguments="--open-graphics"});}
        catch(System.ComponentModel.Win32Exception){Log("Reopening as administrator was cancelled. Nothing was changed.");return;}
        Close();
    }
    void RenderWindowsSetting(){
        bool on=graphicsWindowsState==GraphicsWindowsState.On,restart=graphicsWindowsState==GraphicsWindowsState.RestartRequired,known=graphicsWindowsState is GraphicsWindowsState.On or GraphicsWindowsState.Off or GraphicsWindowsState.RestartRequired;
        string? blocked=on?TurnOffBlockedReason():null;
        // A disabled button gets only the one-line reason; how turning it off works is said once it can be turned off.
        string hint=blocked!=null?blocked:isAdministrator?"Restart Windows after turning it off.":"Needs administrator rights: Arc Cache reopens as administrator after Windows asks for your approval. Restart Windows afterward.";
        DisableRouteButton.Visibility=on&&!developerStatePreview?Visibility.Visible:Visibility.Collapsed;DisableRouteButton.IsEnabled=!busy&&blocked==null;
        DisableRouteReason.Text=vanguardInstalled?hint+" "+VanguardNote:hint;DisableRouteReason.Visibility=DisableRouteButton.Visibility;
        WindowsChangeLink.Content=windowsManageOpen?"Close":"Change";
        SettingsWindowsStatus.Text=!known?"Checking the Windows setting…":restart?"On · restart Windows to finish":on?"On":routingTurnOffRestart?"Off · restart Windows to finish":"Off";
        SettingsEnableRoute.Visibility=graphicsWindowsState==GraphicsWindowsState.Off&&!routingTurnOffRestart?Visibility.Visible:Visibility.Collapsed;SettingsEnableRoute.IsEnabled=!busy;
        SettingsDisableRoute.Visibility=on?Visibility.Visible:Visibility.Collapsed;SettingsDisableRoute.IsEnabled=!busy&&blocked==null;
        SettingsWindowsReason.Text=on?hint:graphicsWindowsState==GraphicsWindowsState.Off&&!routingTurnOffRestart&&!isAdministrator?"Needs administrator rights: Arc Cache reopens as administrator after Windows asks for your approval. Restart Windows afterward.":"";
        if(vanguardInstalled)SettingsWindowsReason.Text=(SettingsWindowsReason.Text+" "+VanguardNote).Trim();
        SettingsWindowsReason.Visibility=SettingsWindowsReason.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    async void ChooseScsCli(object sender,RoutedEventArgs e) {
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".exe");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        await Run("Connecting SCSKiller…",()=>{scs!.SelectCli(file.Path);ScsPath.Text=scs.CliPath;Log("SCSKiller connected. No task has started.");return Task.CompletedTask;});
        await RefreshScsInfo();
    }
    async void ScsStatus(object sender,RoutedEventArgs e)=>await RunScs("status");
    async void ScsCompile(object sender,RoutedEventArgs e)=>await RunScs("compile");
    void CancelScs(object sender,RoutedEventArgs e)=>scsCancellation?.Cancel();
    // Shader preparation runs beside the app (it can take an hour): pages stay usable, and only actions that would touch
    // the game, its setup or its cache file while SCSKiller compiles wait for it.
    bool scsRunning,closeAfterScs,smartAppControlNoted;
    bool ScsBlocks(string action){
        if(!scsRunning)return false;
        GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Shader preparation is running";GlobalNotice.Message=$"Wait until SCSKiller has finished, or choose Stop, then {action}.";GlobalNotice.IsOpen=true;return true;
    }
    async Task CloseDuringScs(){
        if(!await Confirm("Stop shader preparation?","SCSKiller is still compiling shaders. Closing Arc Cache stops it. The shaders compiled so far stay in the cache.","Stop and close"))return;
        scsCancellation?.Cancel();for(int i=0;i<100&&scsRunning;i++)await Task.Delay(100);
        closeAfterScs=true;Close();
    }
    async Task RefreshScsInfo(){
        if(scs==null||scsInfoChecking)return;scsInfoChecking=true;RenderScsTool();
        try{scsInfo=await scs.InspectAsync(CancellationToken.None);}
        catch(Exception e){scsInfo=new ScsKillerInfo(scs.CliPath??"","","",false,"SCSKiller could not be checked: "+e.Message);}
        finally{scsInfoChecking=false;}
        Log(scsInfo.CliPath.Length==0?"No SCSKiller selected.":$"SCSKiller: {scsInfo.Origin}, {scsInfo.Version}. {scsInfo.Note}");
        ScsPath.Text=scs.CliPath==null?"Choose the SCSKiller command-line program (scskiller.exe).":$"{scs.CliPath}\n{scsInfo.Origin} · {scsInfo.Version} · {scsInfo.Note}";
        if(LibraryPanel?.Visibility==Visibility.Visible)RenderMyGames();else RenderScsTool();
    }
    string? ScsToolBlock(){
        if(selectedReceipt?.MiB is not >512||scsInfo==null||scsInfo.PrivateDriverSupport)return null;
        return $"The selected SCSKiller ({scsInfo.Origin}, {scsInfo.Version}) can't use this game's driver copy. With it, the normal driver would shrink this game's cache back to 512 MB, so shader preparation is blocked. Choose Arc Cache's SCSKiller with Change.";
    }
    void RenderScsTool(){
        if(ScsToolText==null)return;
        ScsToolText.Text=scs?.CliPath==null?"No SCSKiller selected.":scsInfoChecking||scsInfo==null?"Checking which SCSKiller is selected…"
            :$"Using {scsInfo.Origin} · {scsInfo.Version} · "+(scsInfo.PrivateDriverSupport?"can use driver copies":"Default games only");
        ToolTipService.SetToolTip(ScsToolText,scsInfo?.Note);
        var link=selected?.SupportedExecutable is string exe?cacheFiles?.For(exe):null;
        ScsToolPath.Text=(scs?.CliPath==null?"":"Program: "+scs.CliPath+(scsInfo==null?"":"\n"+scsInfo.Note))+(link==null?"":$"\nIntel cache file: {link.File} · linked {link.ObservedUtc.ToLocalTime():g} ({link.Source})");
        if(ScsToolBlock() is {} block)ScsSummary.Text=block;
        ScsProgressPanel.Visibility=scsProgressGameKey!=null&&scsProgressGameKey==selected?.Key?Visibility.Visible:Visibility.Collapsed;
        ScsDetails.Children.Clear();
        if(selected?.Source=="Steam")RenderScsDetails(DetailsFor(selected));
        RenderScsRecorder();
    }
    // Frame-time graph: SCSKiller's recorder (a d3d12.dll) in the game's folder, read from the folder itself. Beside a
    // driver copy (exe.local) only an "exe.local-aware" recorder runs: an older one gets itself back for Windows' d3d12.dll
    // and the game can't load DirectX (PEAK crashed on start). First match:
    //   no SCSKiller selected            -> off, disabled  "Choose SCSKiller in Settings first."
    //   not a Steam game                 -> off, disabled  "Only for Steam games."
    //   game folder unknown              -> off, disabled  "Start the game once in DirectX 12 so Arc Cache can find its folder."
    //   a step is running                -> as is, disabled "Wait for the current step to finish."
    //   game running                     -> as is, disabled "Close the game to change this."
    //   driver copy, older recorder in   -> on, enabled    "…stops the game from starting. Turn this off."
    //   driver copy, SCSKiller's older   -> off, disabled  "This SCSKiller's recorder can't run beside a driver copy…"
    //   installed / not                  -> on / off       what it does
    bool scsRecorderRendering;
    const string RecorderUse="It gives on-screen stats their frame-time graph and notes the shaders the game creates for the next shader preparation.";
    static bool HasDriverCopy(string exe)=>Directory.Exists(exe+".local")||File.Exists(exe+".local");
    void RenderScsRecorder(){
        if(ScsRecorder==null)return;
        string? exe=selected?.SupportedExecutable;bool installed=ScsKillerBridge.RecorderInstalled(exe),running=exe!=null&&GameProcessRunning(exe),copy=exe!=null&&HasDriverCopy(exe);
        bool brokenBesideCopy=copy&&installed&&!ScsKillerBridge.InstalledRecorderDotLocalAware(exe),sourceTooOld=copy&&!installed&&!ScsKillerBridge.IsDotLocalAware(scs?.RecorderSource());
        string? blocked=scs?.CliPath==null?"Choose SCSKiller in Settings first.":selected?.Source!="Steam"?"Only for Steam games.":exe==null?"Start the game once in DirectX 12 so Arc Cache can find its folder."
            :busy||scsRunning?"Wait for the current step to finish.":running?"Close the game to change this."
            :sourceTooOld?"This SCSKiller's recorder can't run beside this game's driver copy: the game wouldn't start. Arc Cache's own SCSKiller can.":null;
        scsRecorderRendering=true;try{ScsRecorder.IsOn=installed;}finally{scsRecorderRendering=false;}
        ScsRecorder.IsEnabled=blocked==null&&!developerMyGamesPreview;
        ScsRecorderText.Text=blocked??(brokenBesideCopy?"This recorder is too old for this game's driver copy and stops the game from starting. Turn this off."
            :installed?"SCSKiller's recorder is in this game's folder. "+RecorderUse:"Puts SCSKiller's recorder (a d3d12.dll) in this game's folder. "+RecorderUse);
    }
    // Blocked by an older SCSKiller recorder: the selected SCSKiller replaces it with its exe.local-aware one ("record
    // install" updates an older recorder of its own), then the game is checked again.
    async void UpdateBlockingRecorder(){
        if(PreviewOnlyFeedback()||selected is not SteamGame game||scs==null||busy)return;
        if(!await Confirm("Use Arc Cache's recorder",$"Replace SCSKiller's recorder in {game.Name}'s folder with the one from Arc Cache's SCSKiller? It records the same way and also runs beside a driver copy. If the official SCSKiller app later installs a newer version of its own, Arc Cache shows this again.","Replace"))return;
        await Run($"Replacing SCSKiller's recorder for {game.Name}…",async()=>{
            if(GameProcessRunning(game.SupportedExecutable))throw new InvalidOperationException("Close the game first.");
            var lines=new System.Collections.Concurrent.ConcurrentQueue<string>();
            int code=await scs.RecorderAsync(game.AppId,true,line=>{lines.Enqueue(line);DispatcherQueue.TryEnqueue(()=>Log(line));},CancellationToken.None);
            if(code!=0)throw new InvalidOperationException(lines.LastOrDefault(l=>l.Contains("error",StringComparison.OrdinalIgnoreCase))?.Replace("SCSKiller log: ","").Replace("SCSKiller: ","")??"SCSKiller could not replace the recorder.");
        });
        if(activeModBlocker?.Path is not string dll||!ScsKillerBridge.IsDotLocalAware(dll)){Log("The recorder in "+game.Name+"'s folder is still the older one.");return;}
        activeModBlocker=null;activeModBlockerGameKey=null;await ShowGame();
    }
    async void ScsRecorderToggled(object sender,RoutedEventArgs e){
        if(scsRecorderRendering||selected is not SteamGame game||scs==null)return;
        bool install=ScsRecorder.IsOn;
        if(busy||scsRunning||game.SupportedExecutable==null||install&&HasDriverCopy(game.SupportedExecutable)&&!ScsKillerBridge.IsDotLocalAware(scs.RecorderSource())){RenderScsRecorder();return;}
        if(install&&!await Confirm("Frame-time graph",$"Put SCSKiller's recorder in {game.Name}'s folder? It is a d3d12.dll that loads with the game and passes everything on to Windows' DirectX. SCSKiller refuses games with anti-cheat or another DirectX mod. Turning this off removes exactly the files it added.","Turn on")){RenderScsRecorder();return;}
        await Run(install?$"Turning on recording for {game.Name}…":$"Turning off recording for {game.Name}…",async()=>{
            if(GameProcessRunning(game.SupportedExecutable))throw new InvalidOperationException("Close the game first.");
            var lines=new System.Collections.Concurrent.ConcurrentQueue<string>();
            int code=await scs.RecorderAsync(game.AppId,install,line=>{lines.Enqueue(line);DispatcherQueue.TryEnqueue(()=>Log(line));},CancellationToken.None);
            if(code!=0)throw new InvalidOperationException(lines.LastOrDefault(l=>l.Contains("error",StringComparison.OrdinalIgnoreCase))?.Replace("SCSKiller log: ","").Replace("SCSKiller: ","")??"SCSKiller could not change the recorder.");
            if(ScsKillerBridge.RecorderInstalled(game.SupportedExecutable)!=install)throw new InvalidOperationException(install?"SCSKiller didn't put its recorder in the game's folder.":"SCSKiller's recorder is still in the game's folder.");
        });
        RenderScsRecorder();
    }
    static string Size(long bytes)=>bytes>=1L<<30?(bytes/(double)(1L<<30)).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" GB":(bytes/(double)(1L<<20)).ToString("0",System.Globalization.CultureInfo.InvariantCulture)+" MB";
    void ShowScsOptions(){
        if(scs==null)return;scsOptionsLoading=true;
        try{
            var options=scs.Options;
            ScsThreads.SelectedItem=ScsThreads.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==options.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture))??ScsThreads.Items[0];
            ScsBackground.IsOn=options.Background;ScsCommunity.IsOn=options.Community;
        }finally{scsOptionsLoading=false;}
    }
    void SaveScsOptions(){
        if(scs==null||scsOptionsLoading)return;
        int threads=ScsThreads.SelectedItem is ComboBoxItem item&&int.TryParse(item.Tag?.ToString(),out int t)?t:0;
        try{scs.SaveOptions(new ScsKillerOptions(threads,ScsBackground.IsOn,ScsCommunity.IsOn));}catch(Exception e){Log("SCSKiller options could not be saved: "+e.Message);}
    }
    void ScsThreadsChanged(object sender,SelectionChangedEventArgs e)=>SaveScsOptions();
    void ScsToggleChanged(object sender,RoutedEventArgs e)=>SaveScsOptions();
    // Title in bold, SCSKiller's reason under it, then a two-column table: labels muted on the left, values on the right.
    void RenderScsDetails(ScsDetailView view){
        var muted=(Style)Application.Current.Resources["Muted"];
        ScsDetails.Children.Add(new TextBlock{Text=view.Title,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        if(view.Reason is not null)ScsDetails.Children.Add(new TextBlock{Text=view.Reason,TextWrapping=TextWrapping.Wrap,Style=muted});
        var grid=new Grid{ColumnSpacing=16,RowSpacing=4,Margin=new Thickness(0,6,0,0)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        for(int i=0;i<view.Rows.Count;i++){
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var label=new TextBlock{Text=view.Rows[i].Label,Style=muted};Grid.SetRow(label,i);
            var value=new TextBlock{Text=view.Rows[i].Value,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};Grid.SetRow(value,i);Grid.SetColumn(value,1);
            grid.Children.Add(label);grid.Children.Add(value);
        }
        ScsDetails.Children.Add(grid);
        if(view.Warning is not null)ScsDetails.Children.Add(new TextBlock{Text=view.Warning,TextWrapping=TextWrapping.Wrap,Foreground=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,255,185,70)),Margin=new Thickness(0,4,0,0)});
        if(view.Footer.Length>0)ScsDetails.Children.Add(new TextBlock{Text=view.Footer,FontSize=12,Style=muted,Margin=new Thickness(0,4,0,0)});
    }
    ScsDetailView DetailsFor(SteamGame game){
        var link=game.SupportedExecutable is string exe?cacheFiles?.For(exe):null;
        int mib=game.Key==selected?.Key?selectedReceipt?.MiB??512:512;
        return ScsPresentation.DetailView(scsStatuses.GetValueOrDefault(game.Key),mib,link==null?null:CacheFileMap.CurrentBytes(link.File),link!=null);
    }
    IReadOnlyList<string> ScsDetailLines(SteamGame game)=>ScsPresentation.Lines(DetailsFor(game));
    void RememberScsStatus(SteamGame game,IEnumerable<string> lines){
        if(ScsPresentation.ParseStatus(lines,DateTime.UtcNow) is not {} status){Log("SCSKiller's status for "+game.Name+" could not be read; see the lines above.");return;}
        scsStatuses[game.Key]=status;
        Log($"SCSKiller status for {game.Name}: "+string.Join(" | ",ScsDetailLines(game)));
        try{ScsPresentation.SaveStatuses(scsStatusPath!,scsStatuses);}catch(Exception e) when (e is IOException or UnauthorizedAccessException){Log("SCSKiller status could not be saved: "+e.Message);}
    }
    PrivateDriverWarm? CreateScsPrivateWarm(){
        if(selected?.Source!="Steam"||selected.SupportedExecutable is not string exe||setupState?.Configured!=true||!setupState.ContentsValid||!recordValid||!receiptValid||selectedReceipt?.MiB is not >512||!ReceiptLoadVerified())return null;
        string? driver=selectedReceipt.Files.Keys.FirstOrDefault(n=>n.StartsWith("igd12um64xe",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase));
        if(driver==null||!selectedReceipt.Files.TryGetValue(driver,out string? hash))return null;
        string local=exe+".local";if(!Directory.Exists(local)||!File.Exists(Path.Combine(local,driver)))return null;
        try{return new PrivateDriverWarm(exe,local,selectedReceipt.MiB,driver,hash);}catch(ArgumentException){return null;}
    }
    async Task RunScs(string action) {
        if(selected==null||scs==null||busy)return;
        if(selected.Source!="Steam"){Log("SCSKiller integration currently requires a Steam game.");return;}SteamGame game=selected;
        PrivateDriverWarm? privateWarm=action=="compile"?CreateScsPrivateWarm():null;
        if(action=="compile"&&selectedReceipt?.MiB>512){await RefreshScsInfo();if(ScsToolBlock() is {} block){GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="This SCSKiller can't use the driver copy";GlobalNotice.Message=block;GlobalNotice.IsOpen=true;return;}}
        if(action=="compile"&&setupState?.Configured==true&&selectedReceipt?.MiB>512&&privateWarm==null){GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Driver copy needs attention";GlobalNotice.Message="Arc Cache could not verify this game's larger driver copy. No shader compile was started. Fix the setup or return this game to Default.";GlobalNotice.IsOpen=true;return;}
        string message=action=="compile"&&privateWarm!=null
            ?$"Prepare shaders for {game.Name} with SCSKiller? Close the game first. The temporary compile process will use this game's verified {SetupPresentation.Limit(privateWarm.CacheMiB)} driver copy, then remove its temporary files. This does not prove cache capacity and may replace cache entries. SCSKiller's own game and anti-cheat checks still apply."
            :action=="compile"?$"Prepare shaders for {game.Name} with SCSKiller? Close the game first. This uses the normal Intel driver and Default cache size. It can replace existing cache entries. Community downloads depend on your existing account and settings; some games have no recordings available.":"Check the available shaders for this game? SCSKiller may scan installed games and download community recordings according to your existing settings.";
        message+="\n\nSCSKiller keeps its own support and anti-cheat checks. This action does not install a recording mod, upload recordings or sign you in. Intel support is still a test feature.";
        if(!await Confirm(action=="compile"?privateWarm==null?"Prepare shaders with the normal driver":"Prepare shaders with this game's driver copy":"Check available shaders",message,action=="compile"?"Prepare shaders":"Check",requireRisk:privateWarm!=null))return;
        if(busy||scsRunning)return;
        string label="SCSKiller · "+(action=="compile"?"preparing shaders":"checking shaders")+" for "+game.Name+"…";
        scsRunning=true;Log(label);footerStatusTimer.Stop();FooterStatus.Text=label;UpdateButtons();RenderMyGames();
        try{
            if(action=="compile"&&game.SupportedExecutable!=null) {
                if(GameProcessRunning(game.SupportedExecutable))throw new InvalidOperationException("Close the selected game before compiling.");
            }
            scsCancellation=new CancellationTokenSource();ScsCancelButton.Visibility=Visibility.Visible;
            var cacheBefore=action=="compile"?CacheFileMap.Snapshot():null;bool driverConfirmed=false;ScsKillerProgress? lastProgress=null;var watch=System.Diagnostics.Stopwatch.StartNew();
            int cacheMiB=privateWarm?.CacheMiB??512;
            void Show(ScsProgressView view){ScsProgress.IsIndeterminate=view.Percent==null;if(view.Percent is double pct)ScsProgress.Value=pct;ScsProgressText.Text=view.Headline;ScsProgressDetail.Text=view.Detail;ScsProgressDetail.Visibility=view.Detail.Length>0?Visibility.Visible:Visibility.Collapsed;}
            string? CacheNow(){
                if(cacheBefore==null)return null;
                var grown=CacheFileMap.Snapshot().Select(p=>(p.Key,p.Value,Grew:p.Value-(cacheBefore.TryGetValue(p.Key,out long b)?b:0))).Where(x=>x.Grew>0).OrderByDescending(x=>x.Grew).FirstOrDefault();
                return grown.Key==null?null:$"cache file {Size(grown.Value)} of {SetupPresentation.Limit(cacheMiB)}";
            }
            string? Confirmed()=>driverConfirmed?$"{SetupPresentation.Limit(cacheMiB)} driver copy confirmed":null;
            void ShowProgress()=>Show(ScsPresentation.Progress(action!="compile",lastProgress,watch.Elapsed,CacheNow(),Confirmed()));
            scsProgressGameKey=game.Key;ShowProgress();RenderScsTool();
            var ticker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};ticker.Tick+=(_,_)=>ShowProgress();ticker.Start();
            try {
                var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();
                int code=await scs.RunAsync(action,game.AppId,line=>{logs.Enqueue(line);var parsed=ScsKillerBridge.ParseProgress(line);bool marker=line.Contains("ARC_CACHE_DRIVER_LOADED|",StringComparison.Ordinal);DispatcherQueue.TryEnqueue(()=>{Log(line);if(parsed!=null)lastProgress=parsed;if(marker)driverConfirmed=true;if(parsed!=null||marker)ShowProgress();});},scsCancellation.Token,privateWarm);
                if(code!=0)throw new InvalidOperationException($"SCSKiller exited with code {code}. See Activity for its reason; no private-driver files were changed.");
                if(privateWarm!=null&&!ScsKillerBridge.HasVerifiedPrivateDriverEvidence(logs,privateWarm))throw new InvalidOperationException("SCSKiller finished but did not confirm loading the selected game's private Intel driver copy. Do not treat this warm as verified; see Activity for details.");
                if(action=="compile")Log(privateWarm==null?"SCSKiller completed with the normal Intel driver and Default cache size.":$"SCSKiller confirmed the {SetupPresentation.Limit(privateWarm.CacheMiB)} private driver copy loaded in its temporary compile process. Cache capacity is not measured.");
                if(cacheBefore!=null&&game.SupportedExecutable is string exe&&action=="compile"){
                    var after=CacheFileMap.Snapshot();
                    // A compile also fills the game's separate D3D11 cache file, so its size can't overrule a link that
                    // exists: the exact link comes from the running game (LinkCacheFileFromGame). It only seeds a first one.
                    if(cacheFiles?.For(exe) is {} existing&&CacheFileMap.CurrentBytes(existing.File) is not null)Log($"Kept this game's linked Intel cache file {existing.File[..8]}…; the running game confirms it.");
                    else if(CacheFileMap.Attribute(cacheBefore,after) is string file&&cacheFiles!=null){var link=cacheFiles.Remember(exe,file,after[file],"SCSKiller compile");Log($"Linked {game.Name} to Intel cache file {file[..8]}… ({Size(link.Bytes)}).");}
                    else Log("Arc Cache couldn't tell which Intel cache file belongs to this game (no single file clearly grew).");
                }
                watch.Stop();
                if(action=="status"){RememberScsStatus(game,logs);scsProgressGameKey=null;}
                else{
                    // The compile changed what status reports (last prepared, cache on disk): read it again, without a second prompt.
                    ScsProgressText.Text="Reading the result…";ScsProgress.IsIndeterminate=true;
                    var statusLogs=new System.Collections.Concurrent.ConcurrentQueue<string>();
                    try{if(await scs.RunAsync("status",game.AppId,statusLogs.Enqueue,scsCancellation.Token)==0)RememberScsStatus(game,statusLogs);}
                    catch(Exception e) when (e is not OperationCanceledException){Log("SCSKiller's result could not be read: "+e.Message);}
                }
                Show(ScsPresentation.Progress(action!="compile",lastProgress,watch.Elapsed,CacheNow(),Confirmed(),done:true));
                FooterStatus.Text="Complete · "+label.TrimEnd('…');
            }catch(OperationCanceledException){watch.Stop();Show(ScsPresentation.Progress(action!="compile",lastProgress,watch.Elapsed,CacheNow(),null,stopped:true));throw;}
            catch{scsProgressGameKey=null;throw;}
            finally{ticker.Stop();ScsCancelButton.Visibility=Visibility.Collapsed;scsCancellation.Dispose();scsCancellation=null;RenderScsTool();}
        }catch(Exception e){
            bool stopped=e is OperationCanceledException;diagnostics?.Write("action-error",new{Action=label,Error=e.ToString()});
            Log(stopped?"SCSKiller stopped. The shaders compiled so far stay in the cache.":"Stopped: "+e.Message);FooterStatus.Text=stopped?"SCSKiller stopped":"SCSKiller stopped · see Activity";
            if(!closeAfterScs){GlobalNotice.Severity=stopped?InfoBarSeverity.Informational:InfoBarSeverity.Warning;GlobalNotice.Title=stopped?"Shader preparation stopped":"Shader preparation could not finish";GlobalNotice.Message=stopped?"The shaders compiled so far stay in the cache.":PlainLanguage.Error(e.Message);GlobalNotice.IsOpen=true;}
        }finally{
            scsRunning=false;
            if(!closing&&!closeAfterScs){footerStatusTimer.Start();UpdateButtons();if(LibraryPanel?.Visibility==Visibility.Visible)RenderMyGames();}
        }
    }
}
