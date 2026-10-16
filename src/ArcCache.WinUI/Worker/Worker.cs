using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using ArcCap;

class Request {
    public bool RiskConfirmed {get;set;}
    public bool ReadOnly {get;set;}
    public uint SteamAppId {get;set;}public string InstallDirectory {get;set;}public string LibrarySource {get;set;}public string ExternalId {get;set;}public string SelectedExecutable {get;set;}
    public string DriverFile {get;set;}public string AdapterId {get;set;}public string Action {get;set;}public string Executable {get;set;}public string ReceiptPath {get;set;}public string SourceReceipt {get;set;}public string DriverFolder {get;set;}public string CurrentLogPath {get;set;}public string Confirmation {get;set;}public int MiB {get;set;}public bool Confirmed {get;set;}
}
class Worker {
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern ulong GetTickCount64();
    [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref uint length);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static GameSafetyReport Safety(Request r) {
        var report=GameSafety.Assess(r.SteamAppId,r.InstallDirectory);
        if(!report.ScanComplete||report.Marker!=null)return report;
        if(r.SteamAppId==0){report.Allowed=false;report.Multiplayer="Unknown";report.SteamStatus="No Steam game-mode data applies to this installation.";report.Message="Multiplayer and anti-cheat safety are unknown. Explicit risk acknowledgment is required.";return report;}
        try {
            System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
            var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://store.steampowered.com/api/appdetails?appids="+r.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture)+"&l=english&filters=basic,categories");
            request.Timeout=5000;request.ReadWriteTimeout=5000;request.UserAgent="ArcCache-Safety/1.0";
            string raw;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){char[] buffer=new char[262145];int count=0,n;while(count<buffer.Length&&(n=reader.Read(buffer,count,buffer.Length-count))>0)count+=n;if(count>262144)throw new InvalidDataException("Steam response too large.");raw=new string(buffer,0,count);}
            var data=Json.Deserialize<Dictionary<string,object>>(raw);var app=(Dictionary<string,object>)data[r.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            if(!Convert.ToBoolean(app["success"]))throw new InvalidDataException("Steam metadata unavailable.");
            var details=(Dictionary<string,object>)app["data"];if(Convert.ToUInt32(details["steam_appid"])!=r.SteamAppId)throw new InvalidDataException("Steam app identity mismatch.");
            bool single=false,multi=false,vac=false;var categories=(System.Collections.IEnumerable)details["categories"];
            foreach(Dictionary<string,object> category in categories) {
                int id=Convert.ToInt32(category["id"]);string label=(string)category["description"];single|=id==2;vac|=id==8;
                multi|=new[]{1,9,20,27,36,37,38,39,44,49}.Contains(id)||System.Text.RegularExpressions.Regex.IsMatch(label,"multi.?player|co.op|pvp|mmo",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            report.SteamStatus="Live Steam categories checked at "+DateTime.UtcNow.ToString("o")+". Store declarations are not a complete anti-cheat inventory.";
            report.Multiplayer=multi?"Steam reports multiplayer/co-op":single?"Steam reports single-player":"Unknown";
            if(vac){report.Allowed=false;report.Message="Steam declares Valve Anti-Cheat; explicit risk acknowledgment is required.";}
            else if(multi){report.Allowed=false;report.Message="Steam declares multiplayer/co-op; explicit risk acknowledgment is required.";}
            else if(!single){report.Allowed=false;report.Message="Steam game mode is unknown; explicit risk acknowledgment is required.";}
            else {report.Allowed=true;report.Message="Steam reports single-player; no listed anti-cheat indicators found. Detection is incomplete and does not guarantee safety.";}
        }catch(Exception){report.Allowed=false;report.SteamStatus="Steam metadata unavailable or unreadable; explicit risk acknowledgment is required.";if(report.Marker==null)report.Message=report.SteamStatus;}
        return report;
    }
    static void RequirePatchSafety(string executable,bool riskConfirmed) {
        var report=Safety(new Request{SteamAppId=MatchedSet.Profile(executable).AppId,InstallDirectory=MatchedSet.GameRoot(executable)});
        if(!report.Allowed&&!riskConfirmed)throw new InvalidOperationException("Risk acknowledgment required: "+report.Message+" Anti-cheat, multiplayer and incomplete checks may cause launch failures or account penalties. No changes made.");
    }
    static void Consent(Request r){if(r.ReadOnly)throw new InvalidOperationException("Read-only requests cannot change game or Windows files.");if(!r.Confirmed)throw new InvalidOperationException("Explicit confirmation is required before this action.");}
    static void RequireOriginalActivePackage() {
        string reason;if(!DriverCatalog.CohortQualified(DriverCatalog.Selected(),out reason))throw new InvalidDataException("Driver compatibility review required: "+reason);
    }
    static void RequireNoKnownPrivateSets() {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","games");
        if(!Directory.Exists(root))return;
        foreach(string file in Directory.GetFiles(root,"*.json")) {
            var receipt=MatchedSet.Read(file);if(Directory.Exists(receipt.Folder))throw new InvalidOperationException("Remove configured private game sets before restoring developer routing. Existing set: "+receipt.Folder);
        }
    }
    static object GameState(Request r) {
        string full=Path.GetFullPath(r.Executable),folder=full+".local";bool exists=File.Exists(r.ReceiptPath),present=Directory.Exists(folder)||File.Exists(folder),recordValid=false,contentsValid=false,compatibilityRepairRequired=false;
        MatchedSetReceipt receipt=null;string error=null;bool trialNeedsRetest=false;
        try {
            if(exists) {
                receipt=MatchedSet.Read(r.ReceiptPath);
                if(!Path.GetFullPath(receipt.Executable).Equals(full,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Receipt belongs to another game installation.");
                recordValid=true;
                if(receipt.Status=="removed"&&!present)contentsValid=true;
                else {MatchedSet.ValidateContents(receipt);contentsValid=true;compatibilityRepairRequired=receipt.Schema!=4&&!(receipt.Schema==5&&receipt.DebugFixed)&&MatchedSet.NeedsDebugRoutingFix(receipt.Executable);trialNeedsRetest=receipt.Trial&&receipt.MiB>=2048&&DriverTrial.LegacyTrialNeedsRetest(receipt.TrialReceipt,receipt.MiB,receipt.Driver);}
            }else if(present)error="A private folder exists without this app's recovery record. Import the original matched-set receipt; do not create another setup.";
        }catch(Exception e){error=e.Message;}
        if(!exists&&present) {
            try {using(var input=File.OpenRead(r.ReceiptPath)){} }
            catch(Exception e){error+=" Receipt read: "+e.GetType().Name+": "+e.Message;}
        }
        return new {ReceiptExists=exists,FolderExists=present,RecordValid=recordValid,ContentsValid=contentsValid,Receipt=recordValid?receipt:null,Error=error,CompatibilityRepairRequired=compatibilityRepairRequired,TrialNeedsRetest=trialNeedsRetest};
    }
    static void RequireAmd64Exe(string path) {
        Engine.CheckPath(path);using(var input=new BinaryReader(File.OpenRead(path))){if(input.BaseStream.Length<256||input.ReadUInt16()!=0x5A4D)throw new InvalidDataException("Not a PE executable.");input.BaseStream.Position=0x3c;uint header=input.ReadUInt32();if(header>input.BaseStream.Length-26)throw new InvalidDataException("Invalid PE header.");input.BaseStream.Position=header;if(input.ReadUInt32()!=0x4550||input.ReadUInt16()!=0x8664)throw new InvalidDataException("Only AMD64 game executables are supported.");input.BaseStream.Position=header+24;if(input.ReadUInt16()!=0x20b)throw new InvalidDataException("Expected PE32+ game.");}
    }
    static object BlockedByMod(string executable,string path,bool directory) {
        string product=null,description=null,company=null;
        if(!directory)try{var v=System.Diagnostics.FileVersionInfo.GetVersionInfo(path);product=v.ProductName;description=v.FileDescription;company=v.CompanyName;}catch{}
        return new{Validated=false,Executable=executable,BlockedByAnotherMod=true,BlockerPath=path,BlockerIsDirectory=directory,BlockerProductName=product,BlockerFileDescription=description,BlockerCompanyName=company,Message="A game-local DirectX mod or private folder blocks Arc Cache setup. Remove or disable it before checking again."};
    }
    static object DiscoverGame(Request r) {
        string gameKey=GameIdentity.Key(r.SteamAppId,r.LibrarySource,r.ExternalId,r.InstallDirectory);
        string root=Path.GetFullPath(r.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);Engine.CheckPath(root);
        string successful=r.SourceReceipt??SuccessfulTrial();Dictionary<string,object> trial=null;if(successful!=null)trial=DriverTrial.VerifySuccessful(successful,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),false);
        var previous=MatchedSet.ReadProfile(gameKey);string previousExe=null;
        if(previous==null&&r.ReceiptPath!=null&&File.Exists(r.ReceiptPath)) {
            var recovered=MatchedSet.Read(r.ReceiptPath);
            if(recovered.Game!=null&&MatchedSet.ProfileKey(recovered.Game)==gameKey&&Path.GetFullPath(recovered.Game.Root).Equals(root,StringComparison.OrdinalIgnoreCase)){if(!r.ReadOnly)MatchedSet.SaveProfile(recovered.Game);previous=recovered.Game;}
        }
        if(trial==null&&previous==null&&r.SteamAppId==292030) {
            string legacy=Path.Combine(root,@"bin\x64_dx12\witcher3.exe");
            if(File.Exists(legacy)&&IsolationControl.FileHash(legacy)==MatchedSet.GameHash)return new{Validated=true,Executable=legacy,Message="Existing DX12 profile recognized; executable verified."};
            if(Directory.Exists(legacy+".local"))return new{Validated=false,Executable=legacy,Message="The game updated. Use the system driver again, then launch DX12 for automatic detection."};
        }
        if(previous!=null) {
            previousExe=MatchedSet.ProfileExecutable(previous);
            if(!Path.GetFullPath(previous.Root).Equals(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Game installation moved; previous profile must be recovered before rediscovery.");
            if(File.Exists(previousExe)&&IsolationControl.FileHash(previousExe)==previous.Hash){
                if(trial==null&&previous.Schema==1||trial!=null&&previous.Schema==2&&previous.ObservedDriverHash==(string)trial["OriginalHash"]){if(trial!=null&&previous.TrialReceipt!=successful){previous.TrialReceipt=successful;if(!r.ReadOnly)MatchedSet.SaveProfile(previous);}return new{Validated=true,TrialProfileReady=trial!=null,Executable=previousExe,Message="Game files found. Follow the next setup step."};}
                if(Directory.Exists(previousExe+".local"))return new{Validated=true,Executable=previousExe,Message="An older game setup exists. Use default settings before setting up the tested driver."};
            }
            if(Directory.Exists(previousExe+".local"))return new{Validated=false,Executable=previousExe,Message="Game executable changed. Return its private set to stock, then launch DX12 to rediscover it. Recovery stays available."};
        }
        var candidates=new Dictionary<string,MatchedSet.ProfileInfo>(StringComparer.OrdinalIgnoreCase);int unreadable=0;
        string activeXe2=Path.Combine(ActivePackage(),trial==null?"igd12um64xe2.dll":(string)trial["Driver"]);
        foreach(var process in System.Diagnostics.Process.GetProcesses())using(process) {
            IntPtr handle=OpenProcess(0x1000,false,(uint)process.Id);
            try {
                if(handle==IntPtr.Zero)continue;var path=new System.Text.StringBuilder(4096);uint length=4096;
                if(!QueryFullProcessImageName(handle,0,path,ref length))continue;string exe=path.ToString();
                if(!exe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
                if(r.SelectedExecutable!=null&&!Path.GetFullPath(r.SelectedExecutable).Equals(exe,StringComparison.OrdinalIgnoreCase))continue;
                var modules=WindowsPlatform.ModulePaths(process.Id).ToArray();
                if(!modules.Any(m=>Path.GetFileName(m).Equals("d3d12.dll",StringComparison.OrdinalIgnoreCase)))continue;
                if(!modules.Any(m=>m.Equals(activeXe2,StringComparison.OrdinalIgnoreCase)))continue;
                if(modules.Select(Path.GetFileName).Where(n=>DriverCatalog.Names.Contains(n,StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)continue;
                RequireAmd64Exe(exe);
                string proxy=Isolation.LocalDirectXProxy(exe);if(proxy!=null)return BlockedByMod(exe,proxy,false);
                string local=Isolation.LocalPrivatePath(exe);if(local!=null)return BlockedByMod(exe,local,Directory.Exists(local));
                Isolation.RequireNoLocalD3D12(exe);
                if(trial==null)RequireOriginalActivePackage();
                candidates[exe]=new MatchedSet.ProfileInfo{Schema=trial==null?1:2,TrialReceipt=successful,AppId=r.SteamAppId,Source=r.LibrarySource,ExternalId=r.ExternalId,Root=root,Name=process.ProcessName,RelativeExecutable=exe.Substring(root.Length+1),Hash=IsolationControl.FileHash(exe),ObservedDriverHash=IsolationControl.FileHash(activeXe2),ObservedUtc=DateTime.UtcNow.ToString("o")};
            }catch(Exception e){if(!(e is IOException)&&!(e is UnauthorizedAccessException)&&!(e is InvalidOperationException)&&!(e is System.ComponentModel.Win32Exception))throw;unreadable++;}finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
        }
        if(candidates.Count==1){var entry=candidates.Single();if(!r.ReadOnly)MatchedSet.SaveProfile(entry.Value);return new{Validated=true,TrialProfileReady=trial!=null,Executable=entry.Key,Message="DX12 game executable and Intel driver observed automatically. No game files changed."};}
        if(candidates.Count==0&&trial!=null&&previous!=null&&previous.Schema==1)return new{Validated=true,TrialProfileReady=false,Executable=previousExe,Message="Game files are known. Start the game once with the normal Intel driver in DirectX 12, then close it. Arc Cache must confirm this before setting up a private copy."};
        return new{Validated=false,TrialProfileReady=false,Executable=previousExe,Message=candidates.Count>1?"Multiple rendering executables found; automatic setup needs an unambiguous game process. No profile guessed.":"Launch this game in DX12 with the stock driver and leave it running. Detection will retry automatically. Disable a local D3D12 recorder/proxy first. Unreadable matching processes: "+unreadable};
    }
    static string ObserveGame(string executable) {
        string full=Path.GetFullPath(executable),name=Path.GetFileNameWithoutExtension(full);var processes=System.Diagnostics.Process.GetProcessesByName(name);
        if(processes.Length==0)return "This game is not running. Start it through its launcher, leave it at the menu, and check again.";
        var lines=new List<string>();int inaccessible=0,matched=0;
        foreach(var process in processes)using(process) {
            IntPtr handle=OpenProcess(0x1000,false,(uint)process.Id);
            try {
                if(handle==IntPtr.Zero){inaccessible++;continue;}
                var path=new System.Text.StringBuilder(4096);uint length=4096;
                if(!QueryFullProcessImageName(handle,0,path,ref length)){inaccessible++;continue;}
                if(!path.ToString().Equals(full,StringComparison.OrdinalIgnoreCase))continue;matched++;
                foreach(string module in WindowsPlatform.ModulePaths(process.Id))if(DriverCatalog.Names.Contains(Path.GetFileName(module),StringComparer.OrdinalIgnoreCase))
                    lines.Add(name+" (PID "+process.Id+"): "+module+"\r\nDisk SHA-256: "+IsolationControl.FileHash(module));
            }catch{inaccessible++;}finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
        }
        if(lines.Count==0)return inaccessible>0?"The game process could not be fully inspected. No driver-loading result is available.":matched==0?"The selected game installation is not running. Start it through its launcher and check again.":"This game is running, but no Xe2 driver path was observed. This does not confirm use of the private driver.";
        return String.Join("\r\n",lines)+"\r\nLoaded path and disk hash observed. In-memory bytes and cache growth were not measured.";
    }
    // Checks the running game itself; the UI's observation is never trusted for saving.
    static object VerifySize(Request r) {
        var receipt=MatchedSet.Read(r.ReceiptPath);string driver=MatchedSet.DriverName(receipt);string expected=Path.Combine(receipt.Folder,driver);string expectedHash;
        if(receipt.Files==null||!receipt.Files.TryGetValue(driver,out expectedHash))throw new InvalidDataException("This game's saved setup does not list its driver file.");
        bool running=false;
        foreach(var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(receipt.Executable)))using(process) {
            IntPtr handle=OpenProcess(0x1000,false,(uint)process.Id);
            try {
                if(handle==IntPtr.Zero)continue;var path=new System.Text.StringBuilder(4096);uint length=4096;
                if(!QueryFullProcessImageName(handle,0,path,ref length)||!path.ToString().Equals(receipt.Executable,StringComparison.OrdinalIgnoreCase))continue;
                running=true;
                if(WindowsPlatform.ModulePaths(process.Id).Any(m=>m.Equals(expected,StringComparison.OrdinalIgnoreCase))) {
                    string observed=IsolationControl.FileHash(expected);
                    if(String.Equals(observed,expectedHash,StringComparison.OrdinalIgnoreCase)) {
                        if(!r.ReadOnly)MatchedSet.RecordSizeLoading(r.ReceiptPath,observed);
                        return new{Verified=true,Running=true,MiB=receipt.MiB,Message="This game's private driver copy for its saved cache size was observed loading. Game files unchanged."};
                    }
                }
            }catch(System.ComponentModel.Win32Exception){}catch(UnauthorizedAccessException){}finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
        }
        return new{Verified=false,Running=running,MiB=receipt.MiB,Message=running?"The game is running, but its private driver copy was not observed.":"The game is not running."};
    }
    static object VerifyControl(Request r) {
        var receipt=MatchedSet.Read(r.ReceiptPath);MatchedSet.RequireDebugCompatibility(receipt);if(receipt.MiB!=512)throw new InvalidDataException("Original 512 MiB control required.");
        MatchedSet.ValidateGame(receipt.Executable,receipt.Game);string expected=Path.Combine(receipt.Folder,MatchedSet.DriverName(receipt));
        foreach(var process in System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(receipt.Executable)))using(process) {
            IntPtr handle=OpenProcess(0x1000,false,(uint)process.Id);
            try {
                if(handle==IntPtr.Zero)continue;var path=new System.Text.StringBuilder(4096);uint length=4096;
                if(!QueryFullProcessImageName(handle,0,path,ref length)||!path.ToString().Equals(receipt.Executable,StringComparison.OrdinalIgnoreCase))continue;
                if(WindowsPlatform.ModulePaths(process.Id).Any(m=>m.Equals(expected,StringComparison.OrdinalIgnoreCase))&&IsolationControl.FileHash(expected)==MatchedSet.OriginalDriverHash(receipt)) {
                    if(!r.ReadOnly)MatchedSet.RecordOriginalLoading(r.ReceiptPath);return new{Verified=true,Message="Original private Intel driver path and disk hash observed automatically. Game files unchanged. Gameplay and cache behavior remain unmeasured."};
                }
            }catch(System.ComponentModel.Win32Exception){}catch(UnauthorizedAccessException){}finally{if(handle!=IntPtr.Zero)CloseHandle(handle);}
        }
        return new{Verified=false,Message="Waiting for this game's original private driver to load in DX12."};
    }
    static string SuccessfulTrial(){var package=DriverCatalog.Selected();if(package==null)return null;string probe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe");string active=DriverTrial.FindConfirmedCacheDriver(package,probe)??package.UserModeDriverName;if(String.IsNullOrWhiteSpace(active))return null;foreach(int mib in new[]{16384,8192,4096,2048,1024,512})try{string path=DriverTrial.FindSuccessful(probe,mib,active);if(path!=null)return path;}catch{}return null;}
    static DriverLimitReview[] PerSizeCapabilities(string file,out string successful,DriverTrialInspection report=null){
        successful=null;if(String.IsNullOrWhiteSpace(file)||!File.Exists(file))return DriverCatalog.LimitCapabilities(new Dictionary<int,bool>(),new HashSet<int>(),"No cache-capable Intel driver DLL is listed for this graphics chip.");
        report=report??DriverTrial.Inspect(file);var buildable=new Dictionary<int,bool>();var passed=new HashSet<int>();string probe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe");
        foreach(int mib in new[]{512,1024,2048,4096,8192,16384}){string key=mib.ToString(System.Globalization.CultureInfo.InvariantCulture);bool canBuild;buildable[mib]=report.Buildable.TryGetValue(key,out canBuild)&&canBuild;try{string proof=DriverTrial.FindSuccessful(probe,mib,Path.GetFileName(file));if(proof!=null){passed.Add(mib);successful=proof;}}catch{}}
        return DriverCatalog.LimitCapabilities(buildable,passed,"This driver file's cache code wasn't recognised.");
    }
    static object ValidateForLaunch(Request r){var receipt=MatchedSet.Read(r.ReceiptPath);MatchedSet.ValidateContents(receipt);MatchedSet.ValidateGame(receipt.Executable,receipt.Game);MatchedSet.RequireDebugCompatibility(receipt);if(receipt.Schema==5)MatchedSet.RequireTrialCurrent(receipt);else {string reason;if(!DriverCatalog.CohortQualified(DriverCatalog.Selected(),out reason))throw new InvalidDataException("Check your updated graphics driver or use default settings for this game.");}return new{Ready=true,Trial=receipt.Schema==5};}
    static string ActivePackage(){var p=DriverCatalog.Selected();if(p==null)throw new InvalidOperationException("No active Intel display package was found.");return p.Folder;}
    static object DriverIdentity(){var p=DriverCatalog.Selected();return new{IdentityTag=DriverCatalog.Identity(p),Adapter=p==null?null:p.Adapter,Version=p==null?null:p.Version,DriverFolder=p==null?null:p.Folder};}
    static object Status(bool signatures=false) {
        var package=DriverCatalog.Selected();if(package==null)throw new InvalidOperationException("No active Intel display package was found.");
        string identityBefore=DriverCatalog.Identity(package);string folder=package.Folder;var files=DriverCatalog.Names.Select(n=>DriverCatalog.Review(Path.Combine(folder,n),signatures)).ToArray();
        string reason;bool qualified=DriverCatalog.CohortQualified(package,out reason);if(DriverCatalog.PrefersXe1(package.Adapter)){qualified=false;reason="Run the Xe1 graphics check before setting up a game.";}string currentIdentity=DriverCatalog.Identity(DriverCatalog.Selected());if(identityBefore!=currentIdentity){qualified=false;reason="The active package changed during review. Check again after the driver installer finishes.";}foreach(var reviewed in files)reviewed.PatchEnabled=reviewed.PatchEnabled&&qualified;var xe2=files.First(f=>f.Name=="igd12um64xe2.dll");
        var routing=ReadRouting();
        string probePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe");string activeDriver=DriverTrial.FindConfirmedCacheDriver(package,probePath)??package.UserModeDriverName,trialFile=String.IsNullOrWhiteSpace(activeDriver)?null:Path.Combine(folder,activeDriver),successful;DriverTrialInspection driverInspection=null;DriverLimitReview[] trialCapabilities,limitCapabilities;
        if(!String.IsNullOrWhiteSpace(trialFile)&&File.Exists(trialFile)){driverInspection=DriverTrial.Inspect(trialFile);trialCapabilities=PerSizeCapabilities(trialFile,out successful,driverInspection);}else{successful=null;trialCapabilities=PerSizeCapabilities(null,out successful);}
        limitCapabilities=trialCapabilities;
        if(qualified&&String.Equals(activeDriver,"igd12um64xe2.dll",StringComparison.OrdinalIgnoreCase)){var reviewed=DriverCatalog.LimitCapabilities(true,null);var chunked=limitCapabilities.First(c=>c.MiB==4096);var four=reviewed.First(c=>c.MiB==4096);four.Buildable=chunked.Buildable;four.Available=chunked.Available;four.Tested=chunked.Tested;four.Evidence=chunked.Evidence;limitCapabilities=reviewed;}
        string testedDriver=null;if(successful!=null){var proof=DriverTrial.Saved(successful);testedDriver=proof.ContainsKey("LoadedCacheDriver")?(string)proof["LoadedCacheDriver"]:(string)proof["Driver"];}return new {SuccessfulTrial=successful,SuccessfulTrialDriver=testedDriver,CapabilityDriver=activeDriver,TrialInspection=driverInspection,TrialCapabilities=trialCapabilities,LimitCapabilities=limitCapabilities,DriverFolder=folder,Version=package.Version,Adapter=package.Adapter,AdapterId=package.Id,Adapters=DriverCatalog.Packages(),Hash=xe2.Hash,Original=xe2.State=="QualifiedOriginal",Compatible=qualified,CompatibilityReason=reason,Files=files,IdentityTag=currentIdentity,DeveloperOverride=routing.DeveloperOverride,RebootRequired=routing.RebootRequired,Administrator=IsolationControl.Administrator,SmartAppControl=SmartAppControlState()};
    }
    sealed class RoutingReview { public bool DeveloperOverride,RebootRequired; }
    // Smart App Control (Windows 11): 0 off, 1 on, 2 evaluation; -1 unknown. When on, Windows may refuse the changed and
    // therefore no longer validly signed Intel DLL in a game's driver copy (not tested yet).
    static int SmartAppControlState(){try{using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy")){object value=key==null?null:key.GetValue("VerifiedAndReputablePolicyState",null);return (value is int)?(int)value:-1;}}catch(Exception){return -1;}}
    static RoutingReview ReadRouting(){
        object setting;using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"))setting=key==null?null:key.GetValue("DevOverrideEnable",null);
        bool reboot=false;if(File.Exists(IsolationControl.ReceiptPath)){var receipt=Json.Deserialize<OverrideReceipt>(File.ReadAllText(IsolationControl.ReceiptPath));DateTime created;if(receipt!=null&&receipt.Status=="enabled-reboot-required"&&DateTime.TryParse(receipt.CreatedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out created))reboot=created.ToUniversalTime()>DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64());}
        return new RoutingReview{DeveloperOverride=setting!=null&&Convert.ToInt32(setting)==1,RebootRequired=reboot};
    }
    static string Repaired(string repair,string message){return String.IsNullOrEmpty(repair)?message:repair+"\r\n"+message;}
    static void Progress(int percent,string text){Console.Error.WriteLine("ARCCACHE_PROGRESS "+Json.Serialize(new{Percent=Math.Max(0,Math.Min(100,percent)),Message=text}));}
    static object TrialWorkflow(Request r){
        Progress(0,"Checking permission and the selected source file");
        Consent(r);if(!r.RiskConfirmed)throw new InvalidOperationException("Risk acknowledgment is required for an unreviewed driver test.");
        DriverTrial.RequireReady();var inspected=DriverTrial.Inspect(r.DriverFile);
        if(!inspected.Eligible)throw new InvalidDataException("No unambiguous trusted test candidate is available.");
        string probe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe");
        DriverTrial.Progress=delegate(int value,string text){Progress(value*20/100,"Unchanged copy: "+text);};
        var control=(Dictionary<string,object>)DriverTrial.Create(r.DriverFile,512,probe);
        DriverTrial.Progress=delegate(int value,string text){Progress(20+value*25/100,"Unchanged copy: "+text);};
        control=(Dictionary<string,object>)DriverTrial.Run((string)control["Receipt"],probe);
        if(!Convert.ToBoolean(control["RuntimeVerified"]))return new{Passed=false,Control=control,Patched=(object)null,Summary="The unchanged copy did not pass. No patched copy was made. Your games and Windows driver files are unchanged."};
        int requested=r.MiB;if(!new[]{1024,2048,4096,8192,16384}.Contains(requested))throw new InvalidDataException("Choose a supported cache size to test.");
        string actualName=control["LoadedCacheDriver"] as string;if(!DriverCatalog.IsCacheDriverName(actualName))throw new InvalidDataException("The unchanged control did not identify one private Intel cache DLL. No patched copy was made.");
        string actualFile=Path.Combine(Path.GetDirectoryName(r.DriverFile),actualName);var actualInspection=DriverTrial.Inspect(actualFile);string sizeKey=requested.ToString(System.Globalization.CultureInfo.InvariantCulture);bool canBuild;if(!actualInspection.Buildable.TryGetValue(sizeKey,out canBuild)||!canBuild)return new{Passed=false,Control=control,Patched=(object)null,Summary="The control identified "+actualName+", but this file does not have a unique safe plan for "+(requested/1024.0).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" GB. No larger copy was created."};
        DriverTrial.Progress=delegate(int value,string text){Progress(45+value*25/100,"Selected size: "+text);};
        var patched=(Dictionary<string,object>)DriverTrial.Create(actualFile,requested,probe);
        DriverTrial.Progress=delegate(int value,string text){Progress(70+value*29/100,"Selected size: "+text);};
        patched=(Dictionary<string,object>)DriverTrial.Run((string)patched["Receipt"],probe);bool passed=Convert.ToBoolean(patched["RuntimeVerified"]);
        Progress(100,"Result saved. Game support and actual cache capacity are not verified.");
        return new{Passed=passed,MiB=requested,Control=control,Patched=patched,Summary=passed?"The unchanged control and selected size loaded and prepared one simple shader. Cache capacity, stability and game support are not verified; no game was changed.":"The unchanged control passed, but the selected size did not. Keep normal game settings. No game or Windows driver file was changed."};
    }
    [STAThread]static int Main(string[] args) {
        try {
            if((args.Length>=2&&args.Length<=4)&&args[0]=="--cache-monitor") {
                MatchedSet.ValidateGame(args[1]);System.Windows.Forms.Application.EnableVisualStyles();
                int recordedMiB=512;if(args.Length>=3&&!Int32.TryParse(args[2],out recordedMiB))throw new InvalidDataException("Invalid cache-size receipt value.");
                string linkedFile=args.Length==4?args[3]:null;if(linkedFile!=null&&!System.Text.RegularExpressions.Regex.IsMatch(linkedFile,"^[0-9A-Fa-f]{64}$"))throw new InvalidDataException("Invalid cache file name.");
                System.Windows.Forms.Application.Run(new CacheMonitor(args[1],delegate(string path){return recordedMiB==512?"Default cache size from the game setup record":(recordedMiB/1024.0).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+" GiB from the game setup record";},null,null,linkedFile,recordedMiB));return 0;
            }
            string input=Console.In.ReadToEnd();if(input.Length>65536)throw new InvalidDataException("Request is too large.");
            var r=Json.Deserialize<Request>(input);if(r==null)throw new InvalidDataException("Request is empty.");
            object data=null;string message;bool reportAction=new[]{"Status","CheckDriverCompatibility","DriverLimitCapabilities","InspectDriverTrial","ScanDriverFile"}.Contains(r.Action);
            // What the app only shows (game page, polling, support checks) may reuse hashes of unchanged files across runs.
            // Changes, launch checks and load verifications that save a result always hash afresh.
            bool displayAction=reportAction||new[]{"GameState","ObserveGame","DiscoverGame"}.Contains(r.Action);
            using(IDisposable hashCache=r.ReadOnly||displayAction?IsolationControl.BeginPersistentHashCache():null){
            switch(r.Action) {
                case "Safety":data=Safety(r);message="Fresh install and Steam safety checks completed.";break;
                case "DiscoverGame":data=DiscoverGame(r);message="Automatic game discovery completed; game and driver files unchanged.";break;
                case "VerifyControl":data=VerifyControl(r);message="Original-control observation completed; game files unchanged.";break;
                case "VerifySize":data=VerifySize(r);message="Cache-size observation completed; game files unchanged.";break;
                case "SelectGraphics":Consent(r);DriverCatalog.Select(r.AdapterId);data=DriverIdentity();message="Graphics chip selected for checks. Windows and game GPU preferences were not changed.";break;
                case "ValidateForLaunch":data=ValidateForLaunch(r);message="Game setup checked before launch.";break;
                case "PrepareGameTrial":Consent(r);if(!r.RiskConfirmed)throw new InvalidOperationException("Accept the game-trial risk before continuing.");RequirePatchSafety(r.Executable,r.RiskConfirmed);DriverTrial.RequireReady();if(r.SourceReceipt==null)throw new InvalidDataException("Run the graphics test first.");if(r.MiB!=512)throw new InvalidDataException("Start with the Default copy.");var sourceProof=DriverTrial.Saved(r.SourceReceipt);string controlReceipt=DriverTrial.FindSuccessful(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),512,(string)sourceProof["Driver"]);if(controlReceipt==null)throw new InvalidDataException("The unchanged Default control has not passed on this driver.");
                    if(File.Exists(r.ReceiptPath)){var old=MatchedSet.Read(r.ReceiptPath);if(old.Status!="removed"||Directory.Exists(old.Folder)||File.Exists(old.Folder)||!old.Executable.Equals(Path.GetFullPath(r.Executable),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use default settings before creating a new setup.");File.Move(r.ReceiptPath,r.ReceiptPath+".removed-"+Guid.NewGuid().ToString("N"));}
                    message=MatchedSet.Prepare(r.Executable,ActivePackage(),r.ReceiptPath,512,controlReceipt);break;
                case "ChangeGameTrial":Consent(r);if(!r.RiskConfirmed)throw new InvalidOperationException("Accept the game-trial risk before continuing.");string trialRepair=MatchedSet.Repair(r.ReceiptPath);var trialChange=MatchedSet.Read(r.ReceiptPath);if(trialChange.Schema!=5)throw new InvalidDataException("This is not a game-trial setup.");RequirePatchSafety(trialChange.Executable,r.RiskConfirmed);DriverTrial.RequireReady();if(trialChange.MiB==512&&r.MiB!=512&&trialChange.Status!="original-control-loading-verified")throw new InvalidOperationException("Start the game's unchanged copy first. Arc Cache checks it automatically.");message=Repaired(trialRepair,MatchedSet.ChangeCap(r.ReceiptPath,r.MiB));break;
                case "GraphicsStats":data=GraphicsStatistics.Read();message="Read-only RAM and shared disk-cache snapshot completed.";break;
                case "InspectDriverTrial":Progress(10,"Checking the selected driver file");var inspected=DriverTrial.Inspect(r.DriverFile);string ignored;var trialCaps=PerSizeCapabilities(r.DriverFile,out ignored,inspected);data=new{Inspection=inspected,Capabilities=trialCaps};Progress(100,"Candidate check finished; no files changed");message="Separate-test eligibility checked. This does not approve a game patch.";break;
                case "DriverLimitCapabilities":if(String.IsNullOrWhiteSpace(r.DriverFile))throw new InvalidDataException("Select a driver file first.");string ignoredSuccessful;data=PerSizeCapabilities(r.DriverFile,out ignoredSuccessful);message="Per-size driver copy capabilities and matching tests checked.";break;
                case "DiscardDriverTrial":Consent(r);data=DriverTrial.Discard(r.ReceiptPath,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"));message="Owned test copy removed after checking its files. Games and shader caches unchanged.";break;
                case "PlanDriverTestCleanup":data=DriverTrial.PlanCleanup(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","games"));message="Old driver test cleanup plan checked; no files were changed.";break;
                case "CleanupDriverTests":Consent(r);data=DriverTrial.CleanupOldTests(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","games"));message="Driver test cleanup finished.";break;
                case "TryDriverTrial":data=TrialWorkflow(r);message="Separate driver test finished. Games and installed driver files unchanged.";break;
                case "Status":data=Status();message="Installed driver compatibility inspected.";break;
                case "DriverIdentity":data=DriverIdentity();message="Driver metadata checked; no DLL hashing or writes.";break;
                case "Routing":data=ReadRouting();message="Windows game-specific setting checked without scanning driver files.";break;
                case "CheckDriverCompatibility":data=Status(true);message="Read-only DLL, signature and full-package compatibility review completed. Signature candidates do not enable patches.";break;
                case "ScanDriverFile":if(String.IsNullOrWhiteSpace(r.DriverFile))throw new InvalidDataException("Select a DLL to inspect.");data=DriverCatalog.Review(r.DriverFile,true);message="Read-only structural DLL inspection completed. A candidate is not a runtime compatibility result.";break;
                case "ObserveGame":message=ObserveGame(r.Executable);break;
                case "GameState":data=GameState(r);message="Game setup inspected by the safety engine.";break;
                case "ValidateRecord":data=MatchedSet.Read(r.ReceiptPath);message="Recovery record matches the pinned profile.";break;
                case "Validate":var receipt=MatchedSet.Read(r.ReceiptPath);if(receipt.Status!="removed"||Directory.Exists(receipt.Folder))MatchedSet.ValidateContents(receipt);data=receipt;message=receipt.Status=="removed"?"Previous private set is removed.":"Private set matches the pinned profile.";break;
                case "Prepare":Consent(r);RequirePatchSafety(r.Executable,r.RiskConfirmed);RequireOriginalActivePackage();if(r.MiB!=512)throw new InvalidDataException("New setups start with unchanged originals.");
                    if(File.Exists(r.ReceiptPath)) {var previous=MatchedSet.Read(r.ReceiptPath);if(previous.Status!="removed"||Directory.Exists(previous.Folder)||File.Exists(previous.Folder)||!previous.Executable.Equals(Path.GetFullPath(r.Executable),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Existing setup must be verified and removed first.");File.Move(r.ReceiptPath,r.ReceiptPath+".removed-"+Guid.NewGuid().ToString("N"));}
                    message=MatchedSet.Prepare(r.Executable,ActivePackage(),r.ReceiptPath,512);break;
                case "Import":Consent(r);var imported=MatchedSet.Read(r.SourceReceipt);MatchedSet.ValidateContents(imported);
                    var importedGame=imported.Game??MatchedSet.Profile(imported.Executable);
                    string importedRoot=imported.Game!=null?Path.GetFullPath(imported.Game.Root):MatchedSet.GameRoot(imported.Executable);
                    if(r.InstallDirectory!=null&&(MatchedSet.ProfileKey(importedGame)!=GameIdentity.Key(r.SteamAppId,r.LibrarySource,r.ExternalId,r.InstallDirectory)||!importedRoot.Equals(Path.GetFullPath(r.InstallDirectory),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Receipt belongs to another game installation.");
                    if(r.Executable!=null&&!Path.GetFullPath(imported.Executable).Equals(Path.GetFullPath(r.Executable),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Receipt belongs to another installation.");
                    Engine.CheckPath(Path.GetDirectoryName(Path.GetFullPath(r.ReceiptPath)));byte[] importedBytes=System.Text.Encoding.UTF8.GetBytes(Json.Serialize(imported));
                    using(var stream=new FileStream(r.ReceiptPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(importedBytes,0,importedBytes.Length);stream.Flush(true);}if(imported.Game!=null)MatchedSet.SaveProfile(imported.Game);message="Existing setup imported; game files unchanged.";break;
                case "Change":Consent(r);string changeRepair=MatchedSet.Repair(r.ReceiptPath);var change=MatchedSet.Read(r.ReceiptPath);if(change.Schema==5)throw new InvalidDataException("Use the explicit game-trial action for this setup.");RequirePatchSafety(change.Executable,r.RiskConfirmed);RequireOriginalActivePackage();
                    if(change.MiB==512&&r.MiB!=512&&change.Status!="original-control-loading-verified")throw new InvalidOperationException("Launch the unchanged original private control in DX12. Its loaded driver will be checked automatically before patching.");
                    message=Repaired(changeRepair,MatchedSet.ChangeCap(r.ReceiptPath,r.MiB));break;
                case "Remove":Consent(r);message=Repaired(MatchedSet.Repair(r.ReceiptPath),MatchedSet.Remove(r.ReceiptPath));break;
                case "EnableOverride":Consent(r);message=IsolationControl.Enable();break;
                case "RestoreOverride":Consent(r);RequireNoKnownPrivateSets();message=IsolationControl.Restore();break;
                case "ResetArcCache":Consent(r);if(r.Confirmation!="RESET")throw new InvalidOperationException("Type RESET in the app's confirmation dialog before this action can run.");data=ArcCacheMaintenance.Reset(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap"),r.CurrentLogPath);message=((ArcCacheResetResult)data).Message;break;
                default:throw new InvalidOperationException("Unknown action. No changes made.");
            }}
            Console.Write(Json.Serialize(new {Ok=true,Message=message,Data=data}));return 0;
        }catch(Exception e){Console.Write(Json.Serialize(new {Ok=false,Message=e.Message,Data=(object)null}));return 1;}
    }
}
