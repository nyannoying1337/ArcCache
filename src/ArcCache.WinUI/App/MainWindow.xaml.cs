using ArcCache.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
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
    string? experimentDll,experimentReceipt;bool experimentReviewed;
    bool fillingGraphics;
    string? successfulTrial,successfulTrialDriver;
    bool TrialMode=>selectedReceipt?.Trial==true||successfulTrial!=null&&(!driverOriginal||successfulTrialDriver=="igd12um64xe3.dll");
    bool GameDriverReady=>selectedReceipt!=null&&selectedReceipt.Status!="removed"?(selectedReceipt.Trial?successfulTrial!=null:driverOriginal&&successfulTrialDriver!="igd12um64xe3.dll"):driverOriginal||successfulTrial!=null;
    GameGuide? gameGuide;
    bool busy,driverOriginal,routingReady,receiptValid,recordValid;
    string? steamRoot;
    Process? cacheMonitor;
    bool safetyAllowed;
    ScsKillerBridge? scs;
    CancellationTokenSource? scsCancellation;
    readonly DispatcherTimer driverWatchTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    string driverIdentity="";bool checkingDriver,closing;
    readonly DispatcherTimer discoveryTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    string discoveryMessage="";
    bool preview;
    readonly Dictionary<uint,int> desiredLimits=new();
    bool updatingChoice, synchronizingSelection, backgroundCheck;
    string lastBackgroundError="", displayedArtwork="";
    int foregroundRuns, backgroundPolls, previewWatchSeconds;
    public MainWindow() {
        InitializeComponent();ExtendsContentIntoTitleBar=true;SetTitleBar(TitleArea);
        diagnostics=new DiagnosticLog(store.Root,Environment.GetCommandLineArgs().Contains("--debug"));
        safety.Progress=(percent,message)=>DispatcherQueue.TryEnqueue(()=>TrialUpdate(percent,message));
        graphicsStatsTimer.Tick+=async (_,_)=>{if(!busy)await GraphicsStats();};
        safety.Diagnostic=diagnostics.Write;initializingDiagnostics=true;DiagnosticToggle.IsOn=diagnostics.Enabled;initializingDiagnostics=false;
        diagnostics.Write("session-start",new{Version=typeof(MainWindow).Assembly.GetName().Version?.ToString(),OS=Environment.OSVersion.ToString(),Runtime=Environment.Version.ToString(),Variant=typeof(MainWindow).Assembly.GetName().Name});
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320,940));Nav.SelectedItem=LibraryNav;
        AppWindow.Closing+=(_,e)=>{if(busy)e.Cancel=true;else{closing=true;graphicsStatsTimer.Stop();discoveryTimer.Stop();driverWatchTimer.Stop();StopCacheMonitor();}};
        Root.Loaded+=async (_,_)=>{await Run("Reading your Steam library…",LoadLibrary);if(!preview)driverWatchTimer.Start();};
        driverWatchTimer.Tick+=async (_,_)=>await CheckDriverChanged();
        Activated+=async (_,e)=>{if(e.WindowActivationState!=WindowActivationState.Deactivated)await CheckDriverChanged();};
        discoveryTimer.Tick+=async (_,_)=>await PollGame();
        try{scs=new ScsKillerBridge(Path.Combine(store.Root,"scskiller-bridge.json")).Load();string bundled=Path.Combine(AppContext.BaseDirectory,"SCSKiller","cli","scskiller.exe");if(scs.CliPath==null&&File.Exists(bundled))scs.SelectCli(bundled);ScsPath.Text=scs.CliPath??"Choose the SCSKiller command-line program (scskiller.exe).";}catch(Exception e){Log("SCSKiller configuration could not be read: "+e.Message);scs=new ScsKillerBridge(Path.Combine(store.Root,"scskiller-bridge.json"));}
    }
    // Read-only visual verification; off-screen so a running game never loses focus.
    public void StartPreview(string path,uint appId=0,int watchSeconds=0,bool simulateDriverUpdate=false) {
        previewWatchSeconds=Math.Clamp(watchSeconds,0,20);
        preview=true;
        AppWindow.IsShownInSwitchers=false;AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));AppWindow.Show(false);
        Root.Loaded+=async (_,_)=>{
            try {
                await Task.Delay(1500);while(busy)await Task.Delay(200);
                if(appId!=0){selected=games.FirstOrDefault(g=>g.AppId==appId);await Run("Showing selected game for read-only preview…",ShowGame);Filter();}
                await Task.Delay(700);while(busy)await Task.Delay(200);Root.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255,32,32,32));Root.UpdateLayout();
                if(simulateDriverUpdate){ShowDriverChange(driverIdentity+"-preview-fixture");File.WriteAllText(path+".driver-checks.json",JsonSerializer.Serialize(new{ApplyEnabled=ApplyButton.IsEnabled,ApplyLabel=ApplyButton.Content?.ToString(),LaunchEnabled=LaunchButton.IsEnabled,RecoveryEnabled=RestoreButton.IsEnabled,NoticeVisible=DriverUpdateNotice.IsOpen}));}
                if(Environment.GetCommandLineArgs().Contains("--preview-driver")){Nav.SelectedItem=Nav.MenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,"Driver"));var identity=await Engine(new("DriverIdentity",ReadOnly:true));string? folder=identity.Data.GetProperty("DriverFolder").GetString();if(folder!=null)await ScanExperiment(Path.Combine(folder,"igd12um64xe3.dll"));await Task.Delay(300);Root.UpdateLayout();File.WriteAllText(path+".xe3-checks.json",JsonSerializer.Serialize(new{Reviewed=experimentReviewed,PrepareEnabled=Xe3Prepare.IsEnabled,RunEnabled=Xe3Run.IsEnabled,Busy=busy}));}
                if(Environment.GetCommandLineArgs().Contains("--preview-trial")){Nav.SelectedItem=Nav.MenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,"Driver"));await CheckTrialSupport();await Task.Delay(300);Root.UpdateLayout();File.WriteAllText(path+".trial-checks.json",JsonSerializer.Serialize(new{Eligible=trialEligible,Enabled=TrialButton.IsEnabled,Summary=TrialSummary.Text,Progress=TrialProgress.Value,Ram=GraphicsRam.Text,Cache=GraphicsCache.Text,ReadOnly=preview}));}
                if(Environment.GetCommandLineArgs().Contains("--preview-settings")){Nav.SelectedItem=Nav.FooterMenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,"Settings"));Root.UpdateLayout();}
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
    void DiagnosticToggled(object sender,RoutedEventArgs e) {
        if(initializingDiagnostics||diagnostics==null)return;
        try{diagnostics.SetEnabled(DiagnosticToggle.IsOn);Log(diagnostics.Enabled?"Detailed diagnostics enabled. Files stay local; review them before sharing.":"Detailed diagnostics disabled. Existing logs are retained.");}
        catch(Exception error){Log("Diagnostic setting could not be saved: "+error.Message);initializingDiagnostics=true;DiagnosticToggle.IsOn=diagnostics.Enabled;initializingDiagnostics=false;}
    }
    void OpenLogs(object sender,RoutedEventArgs e) {
        try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={System.IO.Path.GetDirectoryName(store.LogPath)!}});}catch(Exception error){Log("Could not open logs: "+error.Message);}
    }
    void Log(string text){store.Log(text);ActivityLog.Text+=DateTime.Now.ToString("HH:mm:ss ")+text+Environment.NewLine;}
    async Task Run(string label,Func<Task> action) {
        if(busy)return;foregroundRuns++;busy=true;Nav.IsEnabled=false;GlobalNotice.IsOpen=false;BusyRing.Visibility=Visibility.Visible;BusyRing.IsActive=true;FooterStatus.Text=label;Log(label);
        UpdateButtons();
        try{await action();FooterStatus.Text="Finished · see Recent actions for details";}
        catch(Exception e){diagnostics?.Write("action-error",new{Action=label,Error=e.ToString()});Log("Stopped: "+e.Message);FooterStatus.Text="Stopped · no further changes attempted";GlobalNotice.Severity=InfoBarSeverity.Warning;GlobalNotice.Title="Could not finish this step";GlobalNotice.Message=PlainLanguage.Error(e.Message);GlobalNotice.IsOpen=true;}
        finally{busy=false;Nav.IsEnabled=true;BusyRing.IsActive=false;BusyRing.Visibility=Visibility.Collapsed;UpdateButtons();}
    }
    async Task<WorkerReply> Engine(WorkerRequest request) {
        if(preview)request=request with{ReadOnly=true};
        WorkerReply reply=await safety.RunAsync(request);Log(reply.Message);if(!reply.Ok)throw new InvalidOperationException(reply.Message);return reply;
    }
    async Task LoadLibrary() {
        uint? previousId=selected?.AppId;var previousGames=games.ToDictionary(g=>g.AppId);
        LibraryResult result=await Task.Run(()=>SteamLibrary.Discover(steamRoot));games=result.Games.Select(g=>previousGames.TryGetValue(g.AppId,out var old)?g with{Detected=old.Detected,DetectedExecutable=old.DetectedExecutable}:g).ToArray();steamRoot=result.SteamRoot;
        LibraryCount.Text=$"{games.Count} installed Steam titles · local discovery";
        LibraryNotice.IsOpen=result.Warnings.Count>0;LibraryNotice.Title="Steam discovery";LibraryNotice.Message=string.Join(Environment.NewLine,result.Warnings.Take(3));
        Filter();await LoadDriver();
        GamesList.SelectedItem=games.FirstOrDefault(g=>g.AppId==previousId)??games.FirstOrDefault(g=>g.AppId==292030)??games.FirstOrDefault();selected=GamesList.SelectedItem as SteamGame;await ShowGame();
    }
    void Filter(){synchronizingSelection=true;try{uint? id=selected?.AppId;GamesList.ItemsSource=games.Where(g=>g.Name.Contains(Search.Text??"",StringComparison.OrdinalIgnoreCase)).ToArray();if(id!=null)GamesList.SelectedItem=games.FirstOrDefault(g=>g.AppId==id&&g.Name.Contains(Search.Text??"",StringComparison.OrdinalIgnoreCase));}finally{synchronizingSelection=false;}}
    void FillGraphics(JsonElement data) {
        fillingGraphics=true;try{
            var choices=data.GetProperty("Adapters").Deserialize<GraphicsChipChoice[]>()??[];GraphicsChoice.ItemsSource=choices;
            string? id=data.GetProperty("AdapterId").GetString();GraphicsChoice.SelectedItem=choices.FirstOrDefault(c=>c.Id==id);
        }finally{fillingGraphics=false;}
    }
    async void GraphicsChanged(object sender,SelectionChangedEventArgs e) {
        if(fillingGraphics||busy||GraphicsChoice.SelectedItem is not GraphicsChipChoice choice)return;
        await Run("Checking the selected Intel graphics chip…",async()=>{await Engine(new("SelectGraphics",Confirmed:true,AdapterId:choice.Id));await LoadDriver();if(selected!=null)await ShowGame();});
    }
    async Task LoadDriver(bool detailed=false) {
        driverOriginal=false;successfulTrial=null;routingReady=false;
        try {
            var reply=await Engine(new(detailed?"CheckDriverCompatibility":"Status"));var d=reply.Data;
            driverOriginal=d.GetProperty("Compatible").GetBoolean();successfulTrial=d.GetProperty("SuccessfulTrial").GetString();successfulTrialDriver=d.GetProperty("SuccessfulTrialDriver").GetString();driverIdentity=d.GetProperty("IdentityTag").GetString()??"";
            routingReady=d.GetProperty("DeveloperOverride").GetBoolean()&&!d.GetProperty("RebootRequired").GetBoolean();
            FillGraphics(d);FillTrial(d);DriverTitle.Text=successfulTrial!=null?"Your test copy worked · choose a game next":driverOriginal?"Ready to set up games":"Check your driver to get started";
            DriverSummary.Text=$"{d.GetProperty("Adapter").GetString()} · {d.GetProperty("Version").GetString()}\n"+(driverOriginal?"The files match a supported version. Start with a game’s unchanged copy before choosing a size.":"Your normal driver is unchanged. Check support or try the separate graphics check below.");
            var details=new List<string>{"Package: "+d.GetProperty("DriverFolder").GetString()};
            foreach(var f in d.GetProperty("Files").EnumerateArray())details.Add($"{f.GetProperty("Name").GetString()} · {f.GetProperty("State").GetString()}\nSHA-256: {f.GetProperty("Hash").GetString()}\nConstructor matches: {f.GetProperty("CandidateCount").GetInt32()} · game profile eligible: {f.GetProperty("PatchEnabled").GetBoolean()} · Xe3 device test available: {f.GetProperty("ExperimentalTestEnabled").GetBoolean()}\n{f.GetProperty("Signature").GetString()}");
            DriverDetails.Text=string.Join("\n\n",details);
            OverrideStatus.Text=RoutingPresentation.Status(d.GetProperty("DeveloperOverride").GetBoolean(),d.GetProperty("RebootRequired").GetBoolean());
            bool changedSinceLastReview=false;
            string baseline=Path.Combine(store.Root,"last-qualified-driver.json");
            if(File.Exists(baseline)){try{if(new FileInfo(baseline).Length>65536)throw new InvalidDataException("Previous review is too large.");using var old=JsonDocument.Parse(File.ReadAllText(baseline));changedSinceLastReview=old.RootElement.GetProperty("IdentityTag").GetString()!=driverIdentity;}catch(Exception ex){Log("Previous driver review unavailable: "+ex.Message);}}
            if(!preview){try{
                if(driverOriginal)File.WriteAllText(baseline,JsonSerializer.Serialize(new{IdentityTag=driverIdentity,Version=d.GetProperty("Version").GetString(),CheckedUtc=DateTime.UtcNow}));
                if(detailed){string reports=Path.Combine(store.Root,"driver-reports");Directory.CreateDirectory(reports);string report=Path.Combine(reports,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");File.WriteAllText(report,JsonSerializer.Serialize(d,new JsonSerializerOptions{WriteIndented=true}));Log("Read-only compatibility report: "+report);}
            }catch(Exception ex){Log("Driver review could not be saved: "+ex.Message);}}
            DriverUpdateNotice.IsOpen=!driverOriginal&&successfulTrial==null;DriverUpdateNotice.Title=driverOriginal?"Driver checked":changedSinceLastReview?"Your graphics driver has changed":"Check your graphics driver before continuing";
            DriverUpdateNotice.Message=driverOriginal?"The installed files match a supported version. Your game setup can be checked against this version.":"Your saved setup may use an older driver. Check support before using it again, or return the game to default settings. Its shader cache will not be deleted.";
            if(successfulTrial!=null)DriverSummary.Text="The separate graphics check passed. Choose a game below to set it up. Windows keeps its normal driver.";if(selected!=null)RenderSetup();UpdateButtons();
        }catch(Exception e){DriverTitle.Text="Could not check your graphics software";DriverSummary.Text=e.Message;DriverUpdateNotice.IsOpen=true;DriverUpdateNotice.Title="Graphics check could not finish";DriverUpdateNotice.Message="Finish installing the graphics driver, then choose Check support again. Saved game setups can still be returned to default settings.";Log("Driver inspection stopped: "+e.Message);if(successfulTrial!=null)DriverSummary.Text="The separate graphics check passed. Choose a game below to set it up. Windows keeps its normal driver.";if(selected!=null)RenderSetup();UpdateButtons();}
    }
    async Task CheckDriverChanged() {
        if(busy||checkingDriver||closing||preview||driverIdentity.Length==0)return;checkingDriver=true;
        try {
            var reply=await safety.RunAsync(new("DriverIdentity",ReadOnly:true));if(!reply.Ok)throw new IOException(reply.Message);
            string current=reply.Data.GetProperty("IdentityTag").GetString()??"";if(closing||current==driverIdentity)return;
            ShowDriverChange(current);
        }catch(Exception e){if(!closing)Log("Driver update check unavailable: "+e.Message);}finally{checkingDriver=false;}
    }
    void ShowDriverChange(string current) {
        driverIdentity=current;driverOriginal=false;successfulTrial=null;successfulTrialDriver=null;trialEligible=false;UseTrialButton.Visibility=Visibility.Collapsed;
        DriverUpdateNotice.IsOpen=true;DriverUpdateNotice.Title="Your graphics driver has changed";
        DriverUpdateNotice.Message="Your saved setup files are unchanged. Check support for the new driver before using them, or return the game to default settings.";
        DriverTitle.Text="Check support for the updated driver";DriverSummary.Text="The installed graphics software has changed. Your game setup files have not been changed.";
        Log("Driver change detected from registry/file metadata. Waiting for explicit compatibility review.");if(successfulTrial!=null)DriverSummary.Text="The separate graphics check passed. Choose a game below to set it up. Windows keeps its normal driver.";if(selected!=null)RenderSetup();UpdateButtons();
    }
    async Task PollGame() {
        if(busy||backgroundCheck||selected==null)return;
        var game=selected;uint id=game.AppId;backgroundCheck=true;backgroundPolls++;
        try {
            if(!game.Supported) {
                if(await DiscoverSelected(true)&&selected?.AppId==id&&!busy)await ShowGame();
            }else if(NeedsControlObservation()) {
                // Polling never enters global busy mode, clears artwork, or rechecks Steam safety.
                var result=await safety.RunAsync(new("VerifyControl",ReceiptPath:store.Receipt(id),ReadOnly:preview));
                if(!result.Ok)throw new InvalidOperationException(result.Message);
                if(selected?.AppId!=id||busy)return;
                if(result.Data.GetProperty("Verified").GetBoolean()) {
                    if(!await ReadSetup(game))return;
                    RuntimeStatus.Text="The first setup loaded. Close the game, then choose your cache size.";
                    RenderSetup();Log("Original private-driver loading checked automatically.");
                    discoveryTimer.Stop();
                }
            }
            lastBackgroundError="";
        }catch(Exception e){if(lastBackgroundError!=e.Message){Log("Automatic check paused: "+e.Message);lastBackgroundError=e.Message;}}
        finally{backgroundCheck=false;}
    }
    async Task<bool> ReadSetup(SteamGame game) {
        GameSetupState? state=null;
        if(game.SupportedExecutable!=null)try {
            var reply=await safety.RunAsync(new("GameState",game.SupportedExecutable,store.Receipt(game.AppId)));
            if(!reply.Ok)throw new InvalidOperationException(reply.Message);state=GameSetupState.FromReply(reply);
        }catch(Exception e){state=new(false,false,false,false,null,e.Message);}
        if(selected?.AppId!=game.AppId)return false;
        setupState=state;selectedReceipt=state?.Receipt;recordValid=state?.RecordValid==true;receiptValid=state?.ContentsValid==true;
        return true;
    }
    void RenderSetup() {
        if(selected==null)return;
        var presentation=SetupPresentation.From(setupState,selected.Supported,routingReady,GameDriverReady);if(successfulTrial!=null&&setupState?.Configured==true&&selectedReceipt?.Trial!=true&&!GameDriverReady)presentation=new SetupPresentation($"{SetupPresentation.Limit(selectedReceipt!.MiB)} saved · older setup","Update this game’s setup","The driver check passed. Use default settings below, then follow the guide to set up the tested copy.",false);
        GameStatus.Text=presentation.Installed;CurrentCap.Text=presentation.Installed;
        GameNotice.Title=presentation.Title;GameNotice.Message=presentation.Next;
        GameNotice.Severity=setupState?.Error!=null||setupState?.CompatibilityRepairRequired==true?InfoBarSeverity.Warning:InfoBarSeverity.Informational;
        GameNotice.IsOpen=true;
        gameGuide=GameGuide.From(setupState,selected.Supported,routingReady,GameDriverReady,successfulTrial!=null);GuideTitle.Text=gameGuide.Title;GuideBody.Text=gameGuide.Body;GuideButton.Content=gameGuide.Button;
        updatingChoice=true;
        int desired=desiredLimits.GetValueOrDefault(selected.AppId,selectedReceipt?.MiB is >512?selectedReceipt.MiB:1024);if(TrialMode&&desired>1024){desired=1024;desiredLimits[selected.AppId]=1024;}foreach(var item in CapChoice.Items.OfType<ComboBoxItem>())item.IsEnabled=!TrialMode||int.Parse(item.Tag.ToString()!)<=1024;
        CapChoice.SelectedIndex=desired==16384?5:desired==8192?4:desired==4096?3:desired==2048?2:desired==1024?1:0;
        updatingChoice=false;UpdateButtons();
    }
    async Task ShowGame() {
        discoveryTimer.Stop();var game=selected;if(game==null){GameTitle.Text="Choose a game";GameStatus.Text="Select an installed Steam title.";UpdateButtons();return;}
        await DiscoverSelected();if(selected?.AppId!=game.AppId)return;game=selected;
        GameTitle.Text=game.Name;
        string artKey=string.Join("|",game.ArtworkSources);
        if(displayedArtwork!=artKey){displayedArtwork=artKey;GamePlaceholder.Visibility=Visibility.Visible;GameArtwork.Source=ArtworkImages.Get(game.ArtworkSources,240);}
        GameDetails.Text=$"Steam app: {game.AppId}\nInstall folder: {game.InstallDirectory}\nRecovery record: {store.Receipt(game.AppId)}";
        RuntimeStatus.Text="Game setup has not been checked yet.";
        safetyAllowed=false;
        try {
            var assessment=(await safety.RunAsync(new("Safety",SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory))).Data;
            if(selected?.AppId!=game.AppId)return;
            var notice=SafetyPresentation.From(assessment);safetyAllowed=!notice.Warn;
            SafetyPanel.Visibility=notice.Warn?Visibility.Visible:Visibility.Collapsed;
            SafetyNotice.IsOpen=notice.Warn;SafetyNotice.Title=notice.Title;SafetyNotice.Message=notice.Message;SafetyNotice.Severity=InfoBarSeverity.Warning;
            SafetyDetails.Text=assessment.GetProperty("Message").GetString()+"\n"+assessment.GetProperty("Multiplayer").GetString()+"\n"+assessment.GetProperty("SteamStatus").GetString();
            Log("Safety: "+SafetyDetails.Text);
        }catch(Exception e){if(selected?.AppId!=game.AppId)return;SafetyPanel.Visibility=Visibility.Visible;SafetyNotice.IsOpen=true;SafetyNotice.Title="Game safety could not be confirmed";SafetyNotice.Message="A modified driver may cause launch failures or account penalties. Continue only if you accept that risk.";SafetyNotice.Severity=InfoBarSeverity.Warning;SafetyDetails.Text=e.Message;Log("Safety check: "+e.Message);}
        if(!await ReadSetup(game))return;
        if(setupState?.Configured==true&&selectedReceipt?.MiB>512)desiredLimits[game.AppId]=selectedReceipt.MiB;
        RenderSetup();
        if((!preview||previewWatchSeconds>0)&&(!game.Supported||NeedsControlObservation()))discoveryTimer.Start();
    }
    void ArtworkOpened(object sender,RoutedEventArgs e)=>GamePlaceholder.Visibility=Visibility.Collapsed;
    void ArtworkFailed(object sender,ExceptionRoutedEventArgs e)=>GamePlaceholder.Visibility=Visibility.Visible;
    bool NeedsControlObservation()=>setupState?.Configured==true&&selectedReceipt?.MiB==512&&selectedReceipt.Status!="original-control-loading-verified";
    async Task<bool> DiscoverSelected(bool quiet=false) {
        if(selected==null)return false;var game=selected;
        try {
            var request=new WorkerRequest("DiscoverGame",ReceiptPath:store.Receipt(game.AppId),SourceReceipt:successfulTrial,SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory,ReadOnly:preview);
            var reply=quiet?await safety.RunAsync(request):await Engine(request);if(!reply.Ok)throw new InvalidOperationException(reply.Message);
            if(selected?.AppId!=game.AppId)return false;
            bool found=reply.Data.GetProperty("Validated").GetBoolean();string? exe=reply.Data.GetProperty("Executable").GetString();
            discoveryMessage=reply.Data.GetProperty("Message").GetString()??"";bool changed=game.Detected!=found||game.DetectedExecutable!=exe;
            var updated=game with{Detected=found,DetectedExecutable=exe};games=games.Select(g=>g.AppId==game.AppId?updated:g).ToArray();selected=updated;
            if(changed)Filter();
            if(found)discoveryTimer.Stop();return found;
        }catch(Exception e){if(quiet)throw;discoveryMessage="Automatic detection unavailable: "+e.Message;Log(discoveryMessage);if(selected?.AppId==game.AppId)selected=game with{Detected=false};return false;}
    }
    void CapChanged(object sender,SelectionChangedEventArgs e) {
        if(updatingChoice||selected==null||CapChoice.SelectedItem is not ComboBoxItem item)return;
        desiredLimits[selected.AppId]=int.Parse(item.Tag.ToString()!);UpdateButtons();
    }
    void UpdateButtons() {
        TrialButton.IsEnabled=!busy&&trialEligible;TrialFile.IsEnabled=!busy;UseTrialButton.IsEnabled=!busy&&successfulTrial!=null;GuideButton.IsEnabled=!busy&&selected!=null;
        if(ApplyButton==null)return;
        Xe3Prepare.IsEnabled=!busy&&experimentReviewed;Xe3Run.IsEnabled=!busy&&experimentReceipt!=null;Xe3Folder.IsEnabled=!busy&&experimentReceipt!=null;
        bool supported=selected?.Supported==true;bool configured=setupState?.Configured==true;
        ApplyButton.IsEnabled=!busy&&supported&&GameDriverReady&&routingReady&&(configured||setupState?.CanPrepare==true)&&!NeedsControlObservation();
        RestoreButton.IsEnabled=!busy&&selected?.SupportedExecutable!=null&&recordValid&&selectedReceipt?.Status!="removed";LaunchButton.IsEnabled=!busy&&selected!=null;ImportButton.IsEnabled=!busy&&selected!=null;CapChoice.IsEnabled=!busy&&supported;
        RestoreButton.Content=recordValid&&selectedReceipt!=null&&setupState?.FolderExists==false&&selectedReceipt.Status!="removed"?"Clear the failed setup record":"Use default settings";
        if(setupState?.FolderExists==true&&!routingReady)LaunchButton.IsEnabled=false;
        if(setupState?.FolderExists==true&&(!configured||!GameDriverReady))LaunchButton.IsEnabled=false;
        ApplyButton.Content=NeedsControlObservation()?"Start the game to check setup":configured?"Apply this cache size":"Set up this game";

        bool same=configured&&CapChoice.SelectedItem is ComboBoxItem choice&&int.Parse(choice.Tag.ToString()!)==selectedReceipt?.MiB;
        if(same&&!NeedsControlObservation()){ApplyButton.IsEnabled=false;ApplyButton.Content="This size is already set";}
        if(!driverOriginal&&supported)ApplyButton.Content="Check graphics support first";
        if(selected!=null)SafetyPanel.Visibility=driverOriginal&&!safetyAllowed?Visibility.Visible:Visibility.Collapsed;
        ScsStatusButton.IsEnabled=!busy&&selected!=null&&scs?.CliPath!=null;
        ScsCompileButton.IsEnabled=ScsStatusButton.IsEnabled&&!(configured&&selectedReceipt?.MiB>512);
        ScsSummary.Text=configured&&selectedReceipt?.MiB>512
            ? $"SCSKiller cannot use this game’s {SetupPresentation.Limit(selectedReceipt.MiB)} setting yet. Shader preparation is paused here so it does not use a different size. Your game setup is unchanged."
            : "SCSKiller prepares shaders with the normal driver and its default size. Community downloads need your existing SCSKiller account and settings.";
    }
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
    sealed record TrialFileChoice(string Name,string File);
    bool fillingTrial,trialEligible,graphicsStatsBusy;
    long? graphicsCacheBaseline;
    readonly DispatcherTimer graphicsStatsTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    void FillTrial(JsonElement data){string? previous=(TrialFile.SelectedItem as TrialFileChoice)?.File;fillingTrial=true;try{string folder=data.GetProperty("DriverFolder").GetString()??"";var choices=data.GetProperty("Files").EnumerateArray().Where(f=>f.GetProperty("Hash").ValueKind==JsonValueKind.String).Select(f=>new TrialFileChoice(f.GetProperty("Name").GetString()!,Path.Combine(folder,f.GetProperty("Name").GetString()!))).Where(f=>f.Name=="igd12um64xe2.dll"||f.Name=="igd12um64xe3.dll").ToArray();TrialFile.ItemsSource=choices;TrialFile.SelectedItem=choices.FirstOrDefault(c=>c.File.Equals(previous,StringComparison.OrdinalIgnoreCase))??choices.FirstOrDefault(c=>c.Name==successfulTrialDriver)??choices.FirstOrDefault();trialEligible=false;UseTrialButton.Visibility=successfulTrial!=null?Visibility.Visible:Visibility.Collapsed;TrialSummary.Text=successfulTrial!=null?"Your test copy worked. Next, choose a game: Arc Cache will guide you through setting it up. You can undo the game setup at any time.":"Choose Check support to check the selected test file.";}finally{fillingTrial=false;}}
    void TrialFileChanged(object sender,SelectionChangedEventArgs e){if(fillingTrial)return;trialEligible=false;UseTrialButton.Visibility=(TrialFile.SelectedItem as TrialFileChoice)?.Name==successfulTrialDriver?Visibility.Visible:Visibility.Collapsed;TrialSummary.Text="Test file changed. Choose Check support again before trying it.";UpdateButtons();}
    void TrialUpdate(int percent,string message){TrialProgress.Visibility=Visibility.Visible;TrialProgress.IsIndeterminate=false;TrialProgress.Value=percent;TrialStage.Text=$"{percent}% · {message}";TrialLog.Text+=DateTime.Now.ToString("HH:mm:ss ")+message+Environment.NewLine;Log(message);}
    async Task InspectTrial(){if(TrialFile.SelectedItem is not TrialFileChoice file){trialEligible=false;TrialSummary.Text="No Xe2 or Xe3 file is available in this package.";return;}var result=await Engine(new("InspectDriverTrial",DriverFile:file.File));trialEligible=result.Data.GetProperty("Eligible").GetBoolean();if(successfulTrial==null)TrialSummary.Text=result.Data.GetProperty("Message").GetString();}
    async Task GraphicsStats(){if(graphicsStatsBusy||closing)return;graphicsStatsBusy=true;try{var reply=await safety.RunAsync(new("GraphicsStats",ReadOnly:true));if(!reply.Ok)throw new IOException(reply.Message);var d=reply.Data;ulong total=d.GetProperty("RamTotal").GetUInt64(),available=d.GetProperty("RamAvailable").GetUInt64();long disk=d.GetProperty("DiskBytes").GetInt64();graphicsCacheBaseline??=disk;GraphicsRam.Text=$"PC RAM: {(total-available)/1073741824.0:0.0} / {total/1073741824.0:0.0} GiB used";GraphicsCache.Text=$"Intel cache on disk: {disk/1048576.0:0.0} MiB · {d.GetProperty("Files").GetInt32()} files";GraphicsStatsStatus.Text=$"Change since opening: {(disk-graphicsCacheBaseline.Value)/1048576.0:+0.0;-0.0;0.0} MiB · unreadable files: {d.GetProperty("Unreadable").GetInt32()} · checked {DateTime.Now:HH:mm:ss}";}catch(Exception e){GraphicsStatsStatus.Text="Statistics unavailable: "+PlainLanguage.Error(e.Message);}finally{graphicsStatsBusy=false;}}
    async void RefreshGraphicsStats(object sender,RoutedEventArgs e)=>await GraphicsStats();
    async void TryDriverPatch(object sender,RoutedEventArgs e){
        if(busy||!trialEligible||TrialFile.SelectedItem is not TrialFileChoice file)return;
        if(!await Confirm("Try a separate 1 GiB copy?","Arc Cache will back up the original file, copy trusted driver files, test the unchanged copy, then create and test a 1 GiB copy. It will stop if a check fails. Allow up to 4 GiB of free disk space. Each graphics check stops after 60 seconds or if its allocated memory exceeds 3 GiB.\n\nAn unreviewed driver may crash the test or cause a graphics reset. A small shader-cache entry may be saved. No game or Windows driver file will be replaced, Windows security will stay enabled, and a pass lets you continue to a separate game setup. Stability and cache capacity still need checking. Close games before testing. Game-specific settings must already be enabled after a restart.","Try separate copy",requireRisk:true))return;
        TrialLog.Text="";TrialLogExpander.IsExpanded=true;TrialProgress.Value=0;TrialProgress.Visibility=Visibility.Visible;TrialProgress.IsIndeterminate=true;TrialSummary.Text="Testing. Your games and installed driver files are unchanged.";
        await Run("Trying the separate driver copy…",async()=>{try{var reply=await Engine(new("TryDriverTrial",DriverFile:file.File,Confirmed:true,RiskConfirmed:true));bool passed=reply.Data.GetProperty("Passed").GetBoolean();if(passed){TrialLogExpander.IsExpanded=false;successfulTrialDriver=file.Name;successfulTrial=reply.Data.GetProperty("Patched").GetProperty("Receipt").GetString();UseTrialButton.Visibility=Visibility.Visible;UseTrialButton.IsEnabled=true;TrialSummary.Text="Your test copy worked. Choose a game next; Arc Cache will walk you through setting it up.";}else TrialSummary.Text=reply.Data.GetProperty("Summary").GetString();TrialProgress.Value=100;TrialStage.Text=reply.Data.GetProperty("Passed").GetBoolean()?"Finished · ready to set up a game trial":"Finished · test did not pass; keep normal game settings";TrialFilesButton.IsEnabled=true;TrialLog.Text+=JsonSerializer.Serialize(reply.Data,new JsonSerializerOptions{WriteIndented=true})+Environment.NewLine;}catch{TrialFilesButton.IsEnabled=Directory.Exists(Path.Combine(store.Root,"driver-trials"));TrialSummary.Text="Test stopped. No game or installed driver file was changed. See the progress log for the last completed step.";TrialStage.Text="Stopped · no further steps attempted";throw;}finally{TrialProgress.IsIndeterminate=false;}});
    }
    async void DiscardTrialCopy(object sender,RoutedEventArgs e){if(busy)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;if(!await Confirm("Remove this separate test copy?","Select experiment.json inside a driver-trials folder. Arc Cache will check every file before removing that one test folder. Changed or extra files stop removal. Your game setups and shader caches stay as they are.","Remove test copy"))return;await Run("Removing the separate test copy…",async()=>{await Engine(new("DiscardDriverTrial",ReceiptPath:file.Path,Confirmed:true));TrialSummary.Text="That test copy was removed. Games and shader caches are unchanged.";});}
    async void ChooseGameForTrial(object sender,RoutedEventArgs e){Nav.SelectedItem=LibraryNav;await Run("Choose a game to set up…",async()=>{await LoadDriver();if(selected!=null)await ShowGame();});GlobalNotice.Severity=InfoBarSeverity.Informational;GlobalNotice.Title="Choose a game from your library";GlobalNotice.Message="Follow Your next step on its page. Arc Cache will tell you when to start and close the game.";GlobalNotice.IsOpen=true;}
    void FollowGameGuide(object sender,RoutedEventArgs e){if(busy||gameGuide==null)return;switch(gameGuide.Action){case "Graphics":Nav.SelectedItem=Nav.MenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,"Driver"));break;case "Settings":Nav.SelectedItem=Nav.FooterMenuItems.OfType<NavigationViewItem>().First(i=>Equals(i.Tag,"Settings"));break;case "Restore":RestoreGame(sender,e);break;case "Prepare":case "Apply":ApplyGame(sender,e);break;case "Launch":LaunchGame(sender,e);break;default:RecoveryExpander.IsExpanded=true;break;}}
    void OpenTrialResults(object sender,RoutedEventArgs e){string root=Path.Combine(store.Root,"driver-trials");if(Directory.Exists(root))Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={root}});}
    async Task CheckTrialSupport(){TrialLog.Text="";TrialProgress.Visibility=Visibility.Visible;TrialProgress.IsIndeterminate=true;await Run("Checking graphics support…",async()=>{try{await LoadDriver(true);await InspectTrial();await GraphicsStats();}finally{TrialProgress.IsIndeterminate=false;}});}
    void Navigate(NavigationView sender,NavigationViewSelectionChangedEventArgs args){string? tag=(args.SelectedItem as NavigationViewItem)?.Tag as string;if(LibraryPanel==null)return;LibraryPanel.Visibility=tag=="Library"?Visibility.Visible:Visibility.Collapsed;DriverPanel.Visibility=tag=="Driver"?Visibility.Visible:Visibility.Collapsed;ActivityPanel.Visibility=tag=="Activity"?Visibility.Visible:Visibility.Collapsed;SettingsPanel.Visibility=tag=="Settings"?Visibility.Visible:Visibility.Collapsed;if(tag=="Driver"){if(!preview)graphicsStatsTimer.Start();_ = GraphicsStats();}else graphicsStatsTimer.Stop();}
    async void RefreshGame(object sender,RoutedEventArgs e)=>await Run("Refreshing installed setup…",ShowGame);
    async void RefreshLibrary(object sender,RoutedEventArgs e)=>await Run("Refreshing Steam…",LoadLibrary);
    void SearchChanged(AutoSuggestBox sender,AutoSuggestBoxTextChangedEventArgs args){if(!busy)Filter();}
    async void GameSelected(object sender,SelectionChangedEventArgs e){if(busy||synchronizingSelection)return;var next=GamesList.SelectedItem as SteamGame;if(next?.AppId==selected?.AppId)return;selected=next;await Run("Checking this game's setup…",ShowGame);}
    async Task ScanExperiment(string path) {
        experimentReviewed=false;experimentDll=path;experimentReceipt=null;
        var reply=await Engine(new("ScanDriverFile",DriverFile:path,ReadOnly:true));var f=reply.Data;
        experimentReviewed=f.GetProperty("ExperimentalTestEnabled").GetBoolean();
        ExperimentSummary.Text=f.GetProperty("Assessment").GetString();ExperimentDetails.Text=$"File: {path}\nSHA-256: {f.GetProperty("Hash").GetString()}\nConstructor matches: {f.GetProperty("CandidateCount").GetInt32()}\n{f.GetProperty("Error").GetString()}";
        UpdateButtons();
    }
    async void ScanExperimentalDll(object sender,RoutedEventArgs e) {
        if(busy)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".dll");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file!=null)await Run("Checking the selected driver file…",()=>ScanExperiment(file.Path));
    }
    async void ScanInstalledXe3(object sender,RoutedEventArgs e) {
        await Run("Checking the installed test file…",async()=>{var identity=await Engine(new("DriverIdentity",ReadOnly:true));string folder=identity.Data.GetProperty("DriverFolder").GetString()??throw new IOException("No active Intel package was found.");await ScanExperiment(Path.Combine(folder,"igd12um64xe3.dll"));});
    }
    async void PrepareXe3(object sender,RoutedEventArgs e) {
        if(busy||!experimentReviewed||experimentDll==null)return;int mib=int.Parse(((ComboBoxItem)Xe3Cap.SelectedItem).Tag.ToString()!);
        if(!await Confirm("Create a separate graphics test?","Create a separate test copy using "+SetupPresentation.Limit(mib)+"? Games and Windows driver files will not change. The test files may need up to 2 GiB of disk space. Start with the unchanged 512 MiB option. Run Arc Cache normally; administrator rights are not needed. Passing this check does not guarantee that games will work.","Create test copy",requireRisk:true))return;
        await Run("Creating the separate graphics test…",async()=>{var reply=await Engine(new("CreateXe3Experiment",MiB:mib,Confirmed:true,RiskConfirmed:true,DriverFile:experimentDll));experimentReceipt=reply.Data.GetProperty("Receipt").GetString();ExperimentSummary.Text="The test copy is ready. Run the graphics check next; it has not been tested yet.";ExperimentDetails.Text="Receipt: "+experimentReceipt;});
    }
    async void RunXe3(object sender,RoutedEventArgs e) {
        if(busy||experimentReceipt==null)return;
        if(!await Confirm("Run the graphics check?","Arc Cache will start its separate test program and try to prepare one simple shader. It will not open a game or replace Windows driver files. A small cache entry may be saved. This test currently needs matching Xe3 graphics and game-specific settings enabled after a restart. It stops after 60 seconds. A pass does not prove that games or larger caches will work.","Run check",requireRisk:true))return;
        await Run("Running the graphics check…",async()=>{var reply=await Engine(new("RunXe3Experiment",ReceiptPath:experimentReceipt,Confirmed:true,RiskConfirmed:true));bool verified=reply.Data.GetProperty("RuntimeVerified").GetBoolean();ExperimentSummary.Text=verified?"The graphics check passed. Games and larger cache sizes still need testing.":"The graphics check did not pass. Keep default settings and open the details below.";ExperimentDetails.Text=reply.Data.GetProperty("Report").GetString()+"\nReceipt: "+experimentReceipt;});
    }
    async void OpenXe3Receipt(object sender,RoutedEventArgs e) {
        if(busy)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        experimentReceipt=file.Path;ExperimentSummary.Text="Saved test selected. Arc Cache will check its files and your current driver before it can run.";ExperimentDetails.Text="Receipt: "+file.Path;UpdateButtons();
    }
    void OpenXe3Folder(object sender,RoutedEventArgs e) {
        if(experimentReceipt==null)return;try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={Path.GetDirectoryName(experimentReceipt)!}});}catch(Exception error){Log("Could not open experiment folder: "+error.Message);}
    }
    async void InspectDriver(object sender,RoutedEventArgs e)=>await CheckTrialSupport();
    async void ApplyGame(object sender,RoutedEventArgs e) {
        SteamGame? game=selected;if(busy||game?.Supported!=true)return;
        await Run("Checking safety and confirming this private change…",async()=>{
        string receipt=store.Receipt(game.AppId);int mib=int.Parse(((ComboBoxItem)CapChoice.SelectedItem).Tag.ToString()!);
        bool configured=setupState?.Configured==true;
        string message=configured?$"Set {game.Name}'s cache size to {SetupPresentation.Limit(mib)}? Close the game first. Only this game's own driver copy will change. Windows and other games keep their normal driver.":$"Set up {game.Name}? Arc Cache will create its own unchanged driver copy. It checks free disk space before starting. Close the game first. If a graphics recording mod prevents setup, disable it yourself; Arc Cache will not remove it. Start the game once so the new copy can be checked before a size change.";
        // Recheck before showing consent; the worker independently repeats this check.
        WorkerReply assessment;try{assessment=await safety.RunAsync(new("Safety",SteamAppId:game.AppId,InstallDirectory:game.InstallDirectory));}catch(Exception ex){assessment=new(false,ex.Message,default);}
        bool riskRequired=TrialMode||!assessment.Ok||!assessment.Data.GetProperty("Allowed").GetBoolean();
        if(riskRequired)message="This game may have multiplayer or anti-cheat. A modified driver can cause account penalties or stop it from starting.\n\n"+message;
        if(mib>=8192)message+="\n\nLARGE-CACHE RELOAD EXPERIMENT: This is an untested driver reload patch. The game can fail to launch or crash. Large cache files still require large memory buffers. Loading an 8/16 GiB cache may require roughly 8/16 GiB of RAM plus the game. No gameplay or real shader-cache capacity above 4 GiB is verified. Use this experimental app to return to 4 GiB or stock. Stop the game if memory climbs rapidly.";
        else if(mib>=2048)message+="\n\nExperimental larger limit: Ready or Not reached the menu with 4 GiB on driver 32.0.101.9034 after the helper fix. Cache capacity and eviction behavior remain unmeasured. Keep the recovery receipt.";
        if(TrialMode)message+="\n\nThis copy passed the separate graphics check, but is new to this game. It could cause launch problems or crashes. Use single-player/offline play; multiplayer or anti-cheat may reject it or penalise your account. Use default settings to undo it. Only the unchanged copy and 1 GiB are offered for this game trial.";
        if(!await Confirm(riskRequired?"Apply anyway?":configured?"Change this game’s cache size":"Set up this game",message,configured?"Apply":"Set up",requireOriginalTest:configured&&mib>=8192,requireRisk:riskRequired))return;
            if(configured)await Engine(new(TrialMode?"ChangeGameTrial":"Change",ReceiptPath:receipt,MiB:mib,Confirmed:true,RiskConfirmed:riskRequired));
            else await Engine(new(TrialMode?"PrepareGameTrial":"Prepare",game.SupportedExecutable,receipt,MiB:512,Confirmed:true,RiskConfirmed:riskRequired,SourceReceipt:TrialMode?successfulTrial:null));
            await ShowGame();
            GlobalNotice.Severity=InfoBarSeverity.Success;GlobalNotice.Title=configured?"Limit installed":"Original copy prepared";
            GlobalNotice.Message=configured?$"{SetupPresentation.Limit(mib)} set for {game.Name}. Setup files checked. Start the game to check whether it loaded them. Performance improvements have not been measured.":"Next: play through Steam in DirectX 12 mode for the automatic loading check. Your selected limit is saved.";
            GlobalNotice.IsOpen=true;
        });
    }
    async void RestoreGame(object sender,RoutedEventArgs e) {
        if(selected?.SupportedExecutable==null)return;string receipt=store.Receipt(selected.AppId);
        if(!await Confirm("Use default settings for this game?","Close the game first. Arc Cache will remove only the setup files it created, after checking the saved record. Files that changed or were added will stop removal. Your shader cache and the Windows setting stay as they are.","Use default settings"))return;
        await Run("Removing this game's verified private set…",async()=>{await Engine(new("Remove",ReceiptPath:receipt,Confirmed:true));await ShowGame();});
    }
    async void ObserveGame(object sender,RoutedEventArgs e) {
        string? executable=selected?.SupportedExecutable;
        if(executable==null){GlobalNotice.Title="Start this game first";GlobalNotice.Message="Start this game in DirectX 12 mode so Arc Cache can check its setup.";GlobalNotice.IsOpen=true;return;}
        await Run("Checking this game's loaded Intel driver…",async()=>{
            var result=await Engine(new("ObserveGame",executable));RuntimeDetails.Text=result.Message;RuntimeStatus.Text=PlainLanguage.GameCheck(result.Message,executable);
        });
    }
    void StopCacheMonitor() {
        if(cacheMonitor!=null){if(!cacheMonitor.HasExited)cacheMonitor.Kill();cacheMonitor.Dispose();cacheMonitor=null;}
        MonitorButton.Content="Show on-screen stats";
    }
    async void ToggleCacheMonitor(object sender,RoutedEventArgs e) {
        if(cacheMonitor!=null&&!cacheMonitor.HasExited){StopCacheMonitor();Log("Cache monitor stopped.");return;}
        if(selected?.Supported!=true){GlobalNotice.Title="Start this game first";GlobalNotice.Message="Launch this game in DirectX 12 mode first so its executable can be detected.";GlobalNotice.IsOpen=true;return;}
        string exe=selected.SupportedExecutable!;
        await Run("Opening on-screen stats…",async()=>{
            StopCacheMonitor();var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"ArcCache.SafetyEngine.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("--cache-monitor");info.ArgumentList.Add(exe);cacheMonitor=Process.Start(info)??throw new IOException("The cache monitor could not start.");
            await Task.Delay(1000);
            if(cacheMonitor.HasExited){string result=await cacheMonitor.StandardOutput.ReadToEndAsync();StopCacheMonitor();throw new IOException("Monitor could not start: "+result);}
            MonitorButton.Content="Hide on-screen stats";Log("Overlay started: SHARED Intel folder, attribution unverified. Net disk growth since monitor start; no shader compilation count. Borderless/windowed display required; CSV saved under Activity logs.");
        });
    }
    async void LaunchGame(object sender,RoutedEventArgs e) {
        if(selected==null)return;SteamGame game=selected;uint id=game.AppId;
        await CheckDriverChanged();
        await Run("Checking this game before opening Steam…",async()=>{
            if(game.SupportedExecutable!=null) {
                var state=GameSetupState.FromReply(await Engine(new("GameState",game.SupportedExecutable,store.Receipt(id))));
                if(state.FolderExists) {
                    if(!routingReady)throw new InvalidOperationException("Enable game-specific settings in Settings and restart Windows before launching this private setup. To play with the system driver, use Use default settings first.");
                    if(!driverOriginal)throw new InvalidOperationException("Driver compatibility needs review. Check support or return this game to the system driver first.");
                    if(!game.Supported)throw new InvalidOperationException("Game executable changed. Return its private set to stock before launching for rediscovery.");
                    if(state.CompatibilityRepairRequired)throw new InvalidOperationException("This private setup needs a compatibility update. Use default settings, then prepare a fresh original 512 MiB copy before launching.");
                    if(!state.Configured)throw new InvalidOperationException("Existing private setup is unverified. Import its recovery record or recover it before launching through this app.");
                    var assessment=(await Engine(new("Safety",SteamAppId:id,InstallDirectory:game.InstallDirectory))).Data;
                    if(!assessment.GetProperty("Allowed").GetBoolean()&&!await Confirm("This game may reject custom settings","This game may have multiplayer or anti-cheat. Playing with a modified driver can cause account penalties or launch failures. Use the original driver for online play.","Launch anyway",requireRisk:true))return;
                }
            }
            Process.Start(new ProcessStartInfo("steam://rungameid/"+id){UseShellExecute=true});Log("Steam launch requested. Launching is not proof of driver loading.");
        });
    }
    async void ImportSetup(object sender,RoutedEventArgs e) {
        if(selected==null)return;uint id=selected.AppId;
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".json");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        await Run("Verifying an existing recovery receipt…",async()=>{
            await Engine(new("Import",selected!.SupportedExecutable,store.Receipt(id),Confirmed:true,SourceReceipt:file.Path,SteamAppId:id,InstallDirectory:selected.InstallDirectory));await ShowGame();
        });
    }
    async void ChooseSteamFolder(object sender,RoutedEventArgs e) {
        var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var folder=await picker.PickSingleFolderAsync();if(folder==null)return;steamRoot=folder.Path;
        await Run("Reading the selected Steam folder…",LoadLibrary);
    }
    async void EnableRouting(object sender,RoutedEventArgs e) {
        if(!await Confirm("Enable game-specific settings","This one-time Windows setting lets a game use its own graphics-driver copy. It can also affect other apps that already have separate copies. Your installed driver and Windows security stay unchanged. Open Arc Cache with Run as administrator yourself, confirm this change, then restart Windows. The old setting is saved so it can be restored.","Enable game-specific settings"))return;
        await Run("Enabling game-specific settings…",async()=>{await Engine(new("EnableOverride",Confirmed:true));await LoadDriver();});
    }
    async void RestoreRouting(object sender,RoutedEventArgs e) {
        if(!await Confirm("Restore the previous Windows setting","To undo one game, use Use default settings on its page. To undo this Windows setting for all apps, remove your custom game setups first. Open Arc Cache with Run as administrator yourself, confirm, then restart Windows. This restores the saved old setting; it may already have been on. Shader caches are not deleted.","Undo the Windows setting"))return;
        await Run("Undoing the Windows setting…",async()=>{await Engine(new("RestoreOverride",Confirmed:true));await LoadDriver();});
    }
    async void ChooseScsCli(object sender,RoutedEventArgs e) {
        var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".exe");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return;
        await Run("Connecting SCSKiller…",()=>{scs!.SelectCli(file.Path);ScsPath.Text=scs.CliPath;Log("SCSKiller connected. No task has started.");return Task.CompletedTask;});
    }
    async void ScsStatus(object sender,RoutedEventArgs e)=>await RunScs("status");
    async void ScsCompile(object sender,RoutedEventArgs e)=>await RunScs("compile");
    void CancelScs(object sender,RoutedEventArgs e)=>scsCancellation?.Cancel();
    async Task RunScs(string action) {
        if(selected==null||scs==null||busy)return;SteamGame game=selected;
        if(action=="compile"&&setupState?.Configured==true&&selectedReceipt?.MiB>512){
            GlobalNotice.Severity=InfoBarSeverity.Informational;GlobalNotice.Title="Shader preparation uses the normal driver";
            GlobalNotice.Message="Your cache size is still set. SCSKiller currently uses the normal driver, so shader preparation is paused for this setup.";GlobalNotice.IsOpen=true;return;
        }
        string message=action=="compile"?$"Prepare shaders for {game.Name} with SCSKiller? Close the game first. This uses the normal driver and default cache size, not the custom size selected here. It can replace existing cache entries. Community downloads depend on your existing account and settings; some games have no recordings available.":"Check the available shaders for this game? SCSKiller may scan installed games and download community recordings according to your existing settings.";
        message+="\n\nSCSKiller keeps its own support and anti-cheat checks. This action does not install a recording mod, upload recordings or sign you in. Intel support is still a test feature.";
        if(!await Confirm(action=="compile"?"Prepare shaders with the normal driver":"Check available shaders",message,action=="compile"?"Prepare shaders":"Check"))return;
        await Run("SCSKiller · "+action+" in progress…",async()=>{
            if(action=="compile"&&game.SupportedExecutable!=null) {
                foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.SupportedExecutable)))using(process)throw new InvalidOperationException("Close the selected game before compiling.");
            }
            scsCancellation=new CancellationTokenSource();ScsCancelButton.Visibility=Visibility.Visible;
            try {
                int code=await scs.RunAsync(action,game.AppId,line=>DispatcherQueue.TryEnqueue(()=>Log(line)),scsCancellation.Token);
                if(code!=0)throw new InvalidOperationException($"SCSKiller exited with code {code}. See Activity for its reason; no private-driver files were changed.");
                Log("SCSKiller completed. Stock-driver warming is not verification of the private cap. Cache contents are not backed up by this app.");
            }finally{ScsCancelButton.Visibility=Visibility.Collapsed;scsCancellation.Dispose();scsCancellation=null;}
        });
    }
}
