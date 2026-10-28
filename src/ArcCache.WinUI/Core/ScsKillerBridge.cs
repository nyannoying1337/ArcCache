using System.Diagnostics;
using System.Text.Json;

namespace ArcCache.Core;

public sealed record PrivateDriverWarm(string GameExecutable,string DriverDirectory,int CacheMiB,string DriverFile,string DriverSha256);
/// <summary>What the selected SCSKiller is. PrivateDriverSupport: its CLI answers arc-cache-info and its warm exe takes
/// --driver-local. Without it (an official portable or beta build) a game with a larger driver copy must not be compiled:
/// the stock driver would trim that game's cache back to 512 MB.</summary>
public sealed record ScsKillerInfo(string CliPath,string Version,string Origin,bool PrivateDriverSupport,string Note,bool NeedsSignIn=false);
/// <summary>Compile options. Threads 0 = SCSKiller's choice. Background = --idle (lower priority).</summary>
public sealed record ScsKillerOptions(int Threads=0,bool Background=false,bool Community=true);
/// <summary>One progress line of the CLI ("20:19:35 steam:1623730 Warming 1200/94165 (3 failed, 2 skipped: not in this install)
/// 450/s - note"): stage, done, total (0 = none), and from the rest the failed and skipped counts, rate and SCSKiller's note.</summary>
public sealed record ScsKillerProgress(string Stage,long Done,long Total,string Detail,long Failed=0,long Skipped=0,double PerSecond=0,string? Note=null);

public sealed class ScsKillerBridge(string settingsPath) {
    public string? CliPath {get;private set;}
    /// <summary>--community is Arc Cache's fork's option (its arc-cache-info answers): an official build refuses it as unknown.</summary>
    public bool TakesCommunity {get;private set;}=true;
    /// <summary>Arc Cache's own SCSKiller, installed with it (null when missing, e.g. a development run).</summary>
    public static string? BundledCli=>Path.Combine(AppContext.BaseDirectory,"SCSKiller","cli","scskiller.exe") is var p&&File.Exists(p)?p:null;
    /// <summary>The user's own SCSKiller (an official alpha or beta) without Arc Cache's driver handoff. It keeps the recorder
    /// and its settings (the traversal stutter fix); status and compile run Arc Cache's SCSKiller on <see cref="MirrorDir"/>,
    /// a copy of the user's data, so a game's driver copy is used and the user's data folder is never written by another build.</summary>
    public bool OwnBuild=>CliPath!=null&&!TakesCommunity&&BundledCli is string bundled&&!Path.GetFullPath(bundled).Equals(Path.GetFullPath(CliPath),StringComparison.OrdinalIgnoreCase);
    public string MirrorDir=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!,"scskiller-own-data");
    static string UserDataDir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCSKiller");
    public ScsKillerBridge Load() {
        if(File.Exists(settingsPath)&&JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(settingsPath)) is {} saved){CliPath=saved.CliPath;Options=new(saved.Threads is >=0 and <=64?saved.Threads:0,saved.Background,saved.Community);}
        return this;
    }
    public void SelectCli(string path) {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)||!Path.GetFileName(path).Equals("scskiller.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Select the SCSKiller CLI scskiller.exe, not the desktop app or a proxy DLL.");
        RequireConsole(path);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath,JsonSerializer.Serialize(new BridgeSettings(path,Options.Threads,Options.Background,Options.Community)));CliPath=path;
    }
    public ScsKillerOptions Options {get;private set;}=new();
    public void SaveOptions(ScsKillerOptions options) {
        if(options.Threads is <0 or >64)throw new ArgumentOutOfRangeException(nameof(options));
        Options=options;Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath,JsonSerializer.Serialize(new BridgeSettings(CliPath,options.Threads,options.Background,options.Community)));
    }
    /// <summary>A started SCSKiller CLI. SCSKiller 1.4 refuses the real %LOCALAPPDATA%\SCSKiller from a build without Update.exe
    /// beside it (Arc Cache's bundled one) unless SCSKILLER_ALLOW_LIVE_DATA=1: Arc Cache works on the user's real games.</summary>
    static ProcessStartInfo Child(string cli) {
        var info=new ProcessStartInfo(Path.GetFullPath(cli)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(cli))!};
        info.Environment["SCSKILLER_ALLOW_LIVE_DATA"]="1";
        return info;
    }
    public static ProcessStartInfo Command(string cli,string action,uint appId,PrivateDriverWarm? privateDriver=null,ScsKillerOptions? options=null,bool takesCommunity=true) {
        if(action is not ("status" or "compile")||appId==0)throw new ArgumentException("Only a selected Steam game's status or compile command is supported.");
        if(privateDriver!=null)ValidatePrivateDriver(privateDriver,action);
        var info=Child(cli);
        options??=new();
        info.ArgumentList.Add(action);info.ArgumentList.Add("steam:"+appId);if(options.Community&&takesCommunity)info.ArgumentList.Add("--community");
        if(action=="compile"){
            if(options.Threads is >0 and <=64){info.ArgumentList.Add("--threads");info.ArgumentList.Add(options.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture));}
            if(options.Background)info.ArgumentList.Add("--idle");
        }
        foreach(string key in HandoffEnvironmentKeys)info.Environment.Remove(key); // caller/machine variables never opt a game into private files
        // Explicit experimental Intel consent is obtained by the UI for each invocation.
        // Child-only; never changes persistent environment, settings, credentials or safety policy.
        info.Environment["SCSKILLER_EXPERIMENTAL_INTEL"]="1";
        if(privateDriver!=null){
            info.Environment["SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR"]=Path.GetFullPath(privateDriver.DriverDirectory);
            info.Environment["SCSKILLER_ARCCACHE_GAME_EXE"]=Path.GetFullPath(privateDriver.GameExecutable);
            info.Environment["SCSKILLER_ARCCACHE_CACHE_MIB"]=privateDriver.CacheMiB.ToString(System.Globalization.CultureInfo.InvariantCulture);
            info.Environment["SCSKILLER_ARCCACHE_DRIVER_NAME"]=privateDriver.DriverFile;
            info.Environment["SCSKILLER_ARCCACHE_DRIVER_SHA256"]=privateDriver.DriverSha256.ToUpperInvariant();
        }
        return info;
    }
    public static bool HasVerifiedPrivateDriverEvidence(IEnumerable<string> lines,PrivateDriverWarm expected){
        ValidatePrivateDriver(expected,"compile");
        string expectedHash=expected.DriverSha256.ToUpperInvariant(),expectedExe=Path.GetFileName(expected.GameExecutable)+".local";
        foreach(string line in lines){
            int marker=line.IndexOf("ARC_CACHE_DRIVER_LOADED|",StringComparison.Ordinal);if(marker<0)continue;
            string[] fields=line[(marker+"ARC_CACHE_DRIVER_LOADED|".Length)..].Split('|');if(fields.Length!=4)continue;
            try{
                string path=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
                if(fields[1].Equals(expected.DriverFile,StringComparison.OrdinalIgnoreCase)&&fields[2].Equals(expectedHash,StringComparison.OrdinalIgnoreCase)&&
                   fields[3]==expected.CacheMiB.ToString(System.Globalization.CultureInfo.InvariantCulture)&&
                   path.Contains(Path.DirectorySeparatorChar+expectedExe+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return true;
            }catch(FormatException){}
        }
        return false;
    }
    static readonly System.Text.RegularExpressions.Regex ProgressLine=new(@"^\d{2}:\d{2}:\d{2} \S+:\S+ (?<stage>[A-Za-z]+)(?: (?<done>\d+)/(?<total>\d+))?(?<rest>.*)$",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    static readonly System.Text.RegularExpressions.Regex ProgressCounts=new(@"^\((?<failed>\d+) failed(?:, (?<skipped>\d+) skipped[^)]*)?\) (?<rate>\d+)/s(?: - (?<note>.+))?$",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static ScsKillerProgress? ParseProgress(string line) {
        if(line.StartsWith("SCSKiller: ",StringComparison.Ordinal))line=line["SCSKiller: ".Length..];
        var m=ProgressLine.Match(line);if(!m.Success)return null;
        long done=m.Groups["done"].Success?long.Parse(m.Groups["done"].Value,System.Globalization.CultureInfo.InvariantCulture):0,total=m.Groups["total"].Success?long.Parse(m.Groups["total"].Value,System.Globalization.CultureInfo.InvariantCulture):0;
        string rest=m.Groups["rest"].Value.Trim();var counts=ProgressCounts.Match(rest);
        if(!counts.Success)return new(m.Groups["stage"].Value,done,total,rest,Note:rest.StartsWith("- ",StringComparison.Ordinal)?rest[2..]:null);
        long Count(string group)=>counts.Groups[group].Success?long.Parse(counts.Groups[group].Value,System.Globalization.CultureInfo.InvariantCulture):0;
        return new(m.Groups["stage"].Value,done,total,rest,Count("failed"),Count("skipped"),Count("rate"),counts.Groups["note"].Success?counts.Groups["note"].Value:null);
    }
    /// <summary>Which SCSKiller this is: the CLI's arc-cache-info line (Arc Cache's build) or, for any other build, its file version.</summary>
    public async Task<ScsKillerInfo> InspectAsync(CancellationToken cancel) {
        if(CliPath==null||!File.Exists(CliPath))return new ScsKillerInfo(CliPath??"","","",false,"Choose scskiller.exe in Settings.");
        string fileVersion=FileVersionInfo.GetVersionInfo(CliPath).ProductVersion??"";
        string origin=Origin(CliPath);
        var info=Child(CliPath);
        info.ArgumentList.Add("arc-cache-info");foreach(string key in HandoffEnvironmentKeys)info.Environment.Remove(key);
        string output="";int code=-1;
        try{
            using var process=Process.Start(info)??throw new IOException("SCSKiller could not start.");process.StandardInput.Close();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var read=process.StandardOutput.ReadToEndAsync(timeout.Token);var err=process.StandardError.ReadToEndAsync(timeout.Token);
            try{await process.WaitForExitAsync(timeout.Token);output=await read+"\n"+await err;code=process.ExitCode;}
            catch(OperationCanceledException){try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}cancel.ThrowIfCancellationRequested();}
        }catch(Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException){return new ScsKillerInfo(CliPath,fileVersion,origin,false,"SCSKiller could not be asked what it supports: "+e.Message);}
        var described=Describe(CliPath,fileVersion,origin,code,output);TakesCommunity=output.Contains("\"arcCacheHandoff\"",StringComparison.Ordinal);
        if(!OwnBuild||described.NeedsSignIn)return described;
        // the user's own SCSKiller: Arc Cache's prepares the shaders, so its driver handoff is what counts
        var own=new ScsKillerBridge(settingsPath){CliPath=BundledCli};
        var bundled=await own.InspectAsync(cancel);
        return described with{PrivateDriverSupport=bundled.PrivateDriverSupport,Note="Your own SCSKiller records, with its settings such as the traversal stutter fix. Arc Cache's SCSKiller prepares the shaders, so driver copies work."};
    }
    public static ScsKillerInfo Describe(string cli,string fileVersion,string origin,int exitCode,string output) {
        foreach(string line in output.Split('\n').Select(l=>l.Trim()).Where(l=>l.StartsWith('{')).Reverse()){
            try{
                using var json=JsonDocument.Parse(line);var root=json.RootElement;
                if(!root.TryGetProperty("arcCacheHandoff",out var handoff)||handoff.GetInt32()<1)continue;
                string version=ShortVersion(root.TryGetProperty("version",out var v)?v.GetString()??fileVersion:fileVersion);
                bool warm=root.TryGetProperty("warmDriverLocal",out var w)&&w.ValueKind==JsonValueKind.True;
                return new ScsKillerInfo(cli,version,origin,warm,warm?"Can compile with a game's larger driver copy.":"Its compile program (scskiller_warm.exe) is older than its CLI and can't use a game's driver copy. Rebuild or reinstall this SCSKiller.");
            }catch(Exception e) when (e is JsonException or InvalidOperationException or FormatException){}
        }
        // an alpha build's CLI refuses every command until the SCSKiller app has a Patreon backer sign-in (its AlphaGate)
        if(output.Contains("needs a Patreon backer sign-in",StringComparison.Ordinal))return new ScsKillerInfo(cli,ShortVersion(fileVersion),origin,false,"This SCSKiller alpha needs your Patreon sign-in. Open the SCSKiller app once and sign in, then choose Check again.",NeedsSignIn:true);
        if(fileVersion.Contains("-alpha",StringComparison.OrdinalIgnoreCase))return new ScsKillerInfo(cli,ShortVersion(fileVersion),origin,false,"Your own SCSKiller alpha. Its recorder and settings (for example the traversal stutter fix) apply to games on Default. Compiling with a driver copy needs Arc Cache's SCSKiller.");
        return new ScsKillerInfo(cli,ShortVersion(fileVersion),origin,false,exitCode==0?"This SCSKiller doesn't report Arc Cache support.":"This SCSKiller doesn't know Arc Cache's driver handoff (for example an official portable or beta build). It can prepare shaders for games on Default only.");
    }
    static string ShortVersion(string version) {
        if(string.IsNullOrWhiteSpace(version))return "unknown version";
        int plus=version.IndexOf('+');string core=plus<0?version:version[..plus],commit=plus<0?"":version[(plus+1)..];
        if(commit.Length>7)commit=commit[..7];
        return core.StartsWith("0.0.0",StringComparison.Ordinal)?(commit.Length>0?"development build "+commit:"development build"):commit.Length>0?core+" ("+commit+")":core;
    }
    public static string Origin(string cli) {
        string full=Path.GetFullPath(cli);
        if(full.StartsWith(Path.GetFullPath(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase)||full.Contains(@"\ArcCache\out\SCSKiller\",StringComparison.OrdinalIgnoreCase))return "Arc Cache's SCSKiller";
        if(full.Contains("portable",StringComparison.OrdinalIgnoreCase))return "Portable SCSKiller";
        return "Installed SCSKiller";
    }
    /// <summary>SCSKiller's recorder: "record install|uninstall steam:N". SCSKiller checks the game (anti-cheat, another
    /// DirectX mod, a running game) and adds or removes exactly its own files.</summary>
    public static ProcessStartInfo RecorderCommand(string cli,uint appId,bool install) {
        if(appId==0)throw new ArgumentException("Only a Steam game's recorder can be changed.");
        var info=Child(cli);
        info.ArgumentList.Add("record");info.ArgumentList.Add(install?"install":"uninstall");info.ArgumentList.Add("steam:"+appId);
        foreach(string key in HandoffEnvironmentKeys)info.Environment.Remove(key);
        info.Environment["SCSKILLER_EXPERIMENTAL_INTEL"]="1"; // SCSKiller supports a game on Intel only with it, as for status
        return info;
    }
    /// <summary>Whether SCSKiller's recorder is in the game's folder: its d3d12.dll proxy, by its version information.</summary>
    public static bool RecorderInstalled(string? executable)=>executable!=null&&Path.GetDirectoryName(Path.GetFullPath(executable)) is string folder&&IsRecorder(Path.Combine(folder,"d3d12.dll"));
    public static bool IsRecorder(string dll) {
        if(!File.Exists(dll))return false;
        try{var v=FileVersionInfo.GetVersionInfo(dll);return v.ProductName=="SCSKiller"&&v.FileDescription=="SCSKiller D3D12 proxy";}catch(FileNotFoundException){return false;}
    }
    /// <summary>A recorder that runs beside a game's driver copy (exe.local): "exe.local-aware" in its version information.
    /// Older ones get themselves back for Windows' d3d12.dll there and the game can't start.</summary>
    public static bool IsDotLocalAware(string? dll) {
        if(dll==null||!IsRecorder(dll))return false;
        try{return FileVersionInfo.GetVersionInfo(dll).Comments=="exe.local-aware";}catch(FileNotFoundException){return false;}
    }
    /// <summary>Whether the recorder's scskiller.ini beside the game turns SCSKiller's traversal stutter fix on
    /// (traversal_suballocate=1, written from SCSKiller's own setting); null without that ini.</summary>
    public static bool? TraversalOn(string? executable) {
        if(executable==null||Path.GetDirectoryName(Path.GetFullPath(executable)) is not string folder)return null;
        string ini=Path.Combine(folder,"scskiller.ini");
        try{return File.Exists(ini)?File.ReadLines(ini).Any(l=>l.Trim()=="traversal_suballocate=1"):null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
    public static bool InstalledRecorderDotLocalAware(string? executable)=>executable!=null&&Path.GetDirectoryName(Path.GetFullPath(executable)) is string folder&&IsDotLocalAware(Path.Combine(folder,"d3d12.dll"));
    /// <summary>The recorder the selected CLI installs, found as SCSKiller's NativeTools.Find does: native\ next to the
    /// CLI, the CLI's folder, then native\ one folder up (Arc Cache's SCSKiller\cli and SCSKiller\native).</summary>
    public string? RecorderSource() {
        if(CliPath==null||Path.GetDirectoryName(Path.GetFullPath(CliPath)) is not string cli)return null;
        foreach(string path in new[]{Path.Combine(cli,"native","d3d12.dll"),Path.Combine(cli,"d3d12.dll"),Path.GetFullPath(Path.Combine(cli,"..","native","d3d12.dll"))})
            if(File.Exists(path))return path;
        return null;
    }
    public const string ChainName="d3d12.scskiller-next.dll";
    /// <summary>A recorder that isn't exe.local-aware (an official SCSKiller's) gets itself back for Windows' d3d12.dll beside
    /// a driver copy. SCSKiller's "record alongside" chains it to a d3d12.dll it finds in the folder (next=): Arc Cache puts a
    /// copy of Windows' d3d12.dll and D3D12Core.dll there first, so the recorder passes everything to that copy. sig= doesn't
    /// cover next=, so the signed lines (the traversal stutter fix) stay valid.</summary>
    public static bool ChainedToSystem(string? executable) {
        if(executable==null||Path.GetDirectoryName(Path.GetFullPath(executable)) is not string folder)return false;
        string ini=Path.Combine(folder,"scskiller.ini"),next=Path.Combine(folder,ChainName);
        try{return File.Exists(ini)&&File.ReadLines(ini).Any(l=>l.Trim()=="next="+ChainName)&&IsWindowsFile(next,"d3d12.dll");}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    static bool IsWindowsFile(string path,string original) {
        if(!File.Exists(path))return false;
        try{var v=FileVersionInfo.GetVersionInfo(path);return v.CompanyName=="Microsoft Corporation"&&original.Equals(v.OriginalFilename?.Replace(".mui",""),StringComparison.OrdinalIgnoreCase);}catch(FileNotFoundException){return false;}
    }
    public async Task<int> RecorderAsync(uint appId,bool install,Action<string> progress,CancellationToken cancel,string? executable=null) {
        string? folder=executable==null?null:Path.GetDirectoryName(Path.GetFullPath(executable));
        bool chain=install&&folder!=null&&(Directory.Exists(executable+".local")||File.Exists(executable+".local"))&&!IsDotLocalAware(RecorderSource());
        if(!chain){
            int code=await RunProcess(()=>RecorderCommand(CliPath!,appId,install),progress,cancel);
            if(!install&&folder!=null&&code==0)RemoveSystemCopies(folder);
            return code;
        }
        string dll=Path.Combine(folder!,"d3d12.dll");
        if(IsRecorder(dll)&&!ChainedToSystem(executable)){   // another recorder (Arc Cache's, or this one unchained): out first
            int out_=await RunProcess(()=>RecorderCommand(CliPath!,appId,false),progress,cancel);
            if(out_!=0)return out_;
            RemoveSystemCopies(folder!);
            if(File.Exists(dll))throw new InvalidOperationException("A d3d12.dll SCSKiller doesn't own is in the game's folder. Remove it first.");
        }
        string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
        if(!File.Exists(dll)&&!File.Exists(Path.Combine(folder!,ChainName)))PlaceSystemCopy(Path.Combine(system,"d3d12.dll"),dll);
        string core=Path.Combine(folder!,"D3D12Core.dll");
        if(!File.Exists(core))PlaceSystemCopy(Path.Combine(system,"D3D12Core.dll"),core);
        int along=await RunProcess(()=>AlongsideCommand(CliPath!,appId),progress,cancel);
        if(along!=0)return along;
        int code2=await RunProcess(()=>RecorderCommand(CliPath!,appId,true),progress,cancel);
        if(code2==0&&!ChainedToSystem(executable))throw new InvalidOperationException("SCSKiller installed its recorder without the chain to Windows' DirectX, so the game wouldn't start beside its driver copy. Turn recording off.");
        return code2;
    }
    static ProcessStartInfo AlongsideCommand(string cli,uint appId) {
        var info=RecorderCommand(cli,appId,true);info.ArgumentList.Clear();
        info.ArgumentList.Add("record");info.ArgumentList.Add("alongside");info.ArgumentList.Add("steam:"+appId);info.ArgumentList.Add("on");
        return info;
    }
    /// <summary>The copies Arc Cache placed for a chain, by path and SHA-256: only an unchanged one is ever removed.</summary>
    string PlacedPath=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!,"system-copies.json");
    Dictionary<string,string> Placed() {
        try{return File.Exists(PlacedPath)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(PlacedPath))??new(StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);}catch(JsonException){return new(StringComparer.OrdinalIgnoreCase);}
    }
    void PlaceSystemCopy(string source,string target) {
        File.Copy(source,target,false);
        var placed=new Dictionary<string,string>(Placed(),StringComparer.OrdinalIgnoreCase){[Path.GetFullPath(target)]=Sha256(target)};
        File.WriteAllText(PlacedPath,JsonSerializer.Serialize(placed));
    }
    /// <summary>After the recorder is out: the Windows copies Arc Cache placed in <paramref name="folder"/> (SCSKiller puts the
    /// chained d3d12.dll back under its own name), each only while unchanged.</summary>
    void RemoveSystemCopies(string folder) {
        var placed=Placed();bool changed=false;
        foreach(string name in new[]{"d3d12.dll","D3D12Core.dll"}){
            string path=Path.GetFullPath(Path.Combine(folder,name));
            if(!placed.TryGetValue(path,out var hash))continue;
            try{if(File.Exists(path)&&Sha256(path)==hash)File.Delete(path);}catch(Exception e) when (e is IOException or UnauthorizedAccessException){continue;}
            if(!File.Exists(path)){placed.Remove(path);changed=true;}
        }
        if(changed)File.WriteAllText(PlacedPath,JsonSerializer.Serialize(placed));
    }
    static string Sha256(string path){using var s=File.OpenRead(path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));}
    public Task<int> RunAsync(string action,uint appId,Action<string> progress,CancellationToken cancel,PrivateDriverWarm? privateDriver=null) {
        if(!OwnBuild)return RunProcess(()=>Command(CliPath!,action,appId,privateDriver,Options,TakesCommunity),progress,cancel);
        Mirror(UserDataDir,MirrorDir);
        return RunProcess(()=>{var info=Command(BundledCli!,action,appId,privateDriver,Options);info.Environment["SCSKILLER_DATA_DIR"]=MirrorDir;return info;},progress,cancel);
    }
    /// <summary>Copies the user's SCSKiller data into <paramref name="to"/>: each file the user's SCSKiller changed since
    /// (newer than the copy), never its lock or temporary files. A compile's own results stay until the user's SCSKiller
    /// writes that file again. The copy forgets the recorder ini's hash, so Arc Cache's older SCSKiller never rewrites
    /// the game's scskiller.ini (it would drop the lines it doesn't know, such as the traversal stutter fix's).</summary>
    public static void Mirror(string from,string to) {
        if(!Directory.Exists(from))return;
        foreach(string file in Directory.EnumerateFiles(from,"*",SearchOption.AllDirectories)){
            string name=Path.GetFileName(file);
            if(name.EndsWith(".lock",StringComparison.OrdinalIgnoreCase)||name.Contains(".tmp",StringComparison.OrdinalIgnoreCase))continue;
            string target=Path.Combine(to,Path.GetRelativePath(from,file));
            try{
                if(File.Exists(target)&&File.GetLastWriteTimeUtc(target)>=File.GetLastWriteTimeUtc(file))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);
                if(name.Equals("state.json",StringComparison.OrdinalIgnoreCase))ForgetIniHash(target);
            }catch(Exception e) when (e is IOException or UnauthorizedAccessException){}   // in use: the copy keeps its last version
        }
    }
    static void ForgetIniHash(string state) {
        try{
            if(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(state)) is not System.Text.Json.Nodes.JsonObject root||root["RecorderFiles"] is not System.Text.Json.Nodes.JsonObject files||!files.Remove("scskiller.ini"))return;
            File.WriteAllText(state,root.ToJsonString());
        }catch(JsonException){}
    }
    async Task<int> RunProcess(Func<ProcessStartInfo> command,Action<string> progress,CancellationToken cancel) {
        if(CliPath==null||!File.Exists(CliPath))throw new FileNotFoundException("Choose the installed SCSKiller CLI in Settings first.");
        RequireConsole(CliPath);
        using var process=Process.Start(command())??throw new IOException("SCSKiller could not start.");
        process.StandardInput.Close(); // No implicit affirmative answers to backend prompts.
        using var registration=cancel.Register(()=>{try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        async Task Read(StreamReader reader,string prefix) {
            while(await reader.ReadLineAsync() is {} line)progress(prefix+(line.Length>8192?line[..8192]+" [truncated]":line));
        }
        await Task.WhenAll(Read(process.StandardOutput,"SCSKiller: "),Read(process.StandardError,"SCSKiller log: "),process.WaitForExitAsync());
        cancel.ThrowIfCancellationRequested();return process.ExitCode;
    }
    static void ValidatePrivateDriver(PrivateDriverWarm handoff,string action){
        if(action!="compile")throw new ArgumentException("A private driver copy can be used only for a compile run.");
        if(handoff.CacheMiB is not (1024 or 2048 or 4096 or 8192 or 16384))throw new ArgumentOutOfRangeException(nameof(handoff.CacheMiB));
        if(!SupportedIntelDriverNames.Contains(handoff.DriverFile,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("The private copy must identify a recognised Intel Xe driver DLL.");
        if(handoff.DriverSha256.Length!=64||!handoff.DriverSha256.All(Uri.IsHexDigit))throw new ArgumentException("The private driver SHA-256 is invalid.");
        string exe=Path.GetFullPath(handoff.GameExecutable),dir=Path.GetFullPath(handoff.DriverDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!dir.Equals(exe+".local",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The private driver folder must be the selected game's .local folder.");
    }
    static readonly string[] HandoffEnvironmentKeys=["SCSKILLER_ARCCACHE_PRIVATE_DRIVER_DIR","SCSKILLER_ARCCACHE_GAME_EXE","SCSKILLER_ARCCACHE_CACHE_MIB","SCSKILLER_ARCCACHE_DRIVER_NAME","SCSKILLER_ARCCACHE_DRIVER_SHA256"];
    static readonly string[] SupportedIntelDriverNames=["igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"];
    static void RequireConsole(string path) {
        using var input=new BinaryReader(File.OpenRead(path));
        if(input.BaseStream.Length<256||input.ReadUInt16()!=0x5A4D)throw new InvalidDataException("Selected file is not a Windows CLI executable.");
        input.BaseStream.Position=0x3C;uint header=input.ReadUInt32();
        if(header>input.BaseStream.Length-96)throw new InvalidDataException("Invalid executable header.");
        input.BaseStream.Position=header;if(input.ReadUInt32()!=0x4550)throw new InvalidDataException("Invalid PE executable.");
        input.BaseStream.Position=header+24;ushort magic=input.ReadUInt16();
        input.BaseStream.Position=header+24+68;
        if((magic!=0x10B&&magic!=0x20B)||input.ReadUInt16()!=3)throw new InvalidDataException("This is the desktop app. Select SCSKiller's console CLI build instead.");
    }
    sealed record BridgeSettings(string? CliPath,int Threads=0,bool Background=false,bool Community=true);
}
