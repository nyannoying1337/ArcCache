using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ArcCap {
public class MatchedSetReceipt {
    public int Schema {get;set;} public string Executable {get;set;} public string Folder {get;set;}
    public int MiB {get;set;} public string Status {get;set;} public Dictionary<string,string> Files {get;set;}
    public MatchedSet.ProfileInfo Game {get;set;}
    public bool Trial {get;set;}public bool DebugFixed {get;set;}public string TrialReceipt {get;set;}public int TrialMiB {get;set;}public string Driver {get;set;}public string OriginalHash {get;set;}public string TrialPatchedHash {get;set;}public Dictionary<string,string> OriginalFiles {get;set;}
}
public static class MatchedSet {
    public const string GameHash="2DF765398A3D1F165DB418271BE7200178320A4466817379F2FA0DC0001FB101";
    public class ProfileInfo {
        public uint AppId;public string Name,RelativeExecutable,Hash,Launcher,Source,ExternalId;
        public int Schema;public string Root,ObservedDriverHash,ObservedUtc,TrialReceipt;
    }
    static readonly ProfileInfo[] Profiles={
        new ProfileInfo{AppId=292030,Name="Witcher",RelativeExecutable=@"bin\x64_dx12\witcher3.exe",Hash=GameHash}
    };
    public static string ProfileDirectory {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","profiles");}}
    public static string ProfileKey(ProfileInfo p){return GameIdentity.Key(p.AppId,p.Source,p.ExternalId,p.Root);}
    public static string ProfilePath(uint appId){return ProfilePath("steam-"+appId);}
    public static string ProfilePath(string key){Require(GameIdentity.ValidKey(key),"Invalid profile key.");return Path.Combine(ProfileDirectory,key+".json");}
    public static string ProfileExecutable(ProfileInfo p) {
        Require(p!=null&&(p.Schema==1||p.Schema==2)&&GameIdentity.ValidKey(ProfileKey(p))&&(p.Schema==1?p.ObservedDriverHash==Engine.Original:!String.IsNullOrWhiteSpace(p.TrialReceipt)&&System.Text.RegularExpressions.Regex.IsMatch(p.ObservedDriverHash??"","\\A[0-9A-F]{64}\\z"))&&p.Hash!=null&&System.Text.RegularExpressions.Regex.IsMatch(p.Hash,"\\A[0-9A-F]{64}\\z"),"Invalid observed game profile.");
        Require(!String.IsNullOrWhiteSpace(p.Root)&&!String.IsNullOrWhiteSpace(p.RelativeExecutable)&&!Path.IsPathRooted(p.RelativeExecutable),"Invalid profile paths.");
        string root=Path.GetFullPath(p.Root).TrimEnd(Path.DirectorySeparatorChar),exe=Path.GetFullPath(Path.Combine(root,p.RelativeExecutable));
        Require(exe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(exe).Equals(".exe",StringComparison.OrdinalIgnoreCase),"Observed executable escaped its game installation.");return exe;
    }
    public static ProfileInfo ReadProfile(uint appId){return ReadProfile("steam-"+appId);}
    public static ProfileInfo ReadProfile(string key){string path=ProfilePath(key);if(!File.Exists(path))return null;Engine.CheckPath(path);Require(new FileInfo(path).Length<=65536,"Profile is too large.");var p=new JavaScriptSerializer().Deserialize<ProfileInfo>(File.ReadAllText(path));ProfileExecutable(p);Require(ProfileKey(p)==key,"Profile identity mismatch.");return p;}
    public static void SaveProfile(ProfileInfo profile) {
        ProfileExecutable(profile);Directory.CreateDirectory(ProfileDirectory);Engine.CheckPath(ProfileDirectory);
        string path=ProfilePath(ProfileKey(profile)),tmp=path+"."+Guid.NewGuid().ToString("N")+".pending";
        File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(profile));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
    }
    public static ProfileInfo Profile(string executable) {
        string full=Path.GetFullPath(executable);
        if(Directory.Exists(ProfileDirectory))foreach(string file in Directory.EnumerateFiles(ProfileDirectory,"*.json").Take(10000)) {
            string key=Path.GetFileNameWithoutExtension(file);if(!GameIdentity.ValidKey(key))continue;
            var profile=ReadProfile(key);if(full.Equals(ProfileExecutable(profile),StringComparison.OrdinalIgnoreCase))return profile;
        }
        foreach(var profile in Profiles)if(full.EndsWith("\\"+profile.RelativeExecutable,StringComparison.OrdinalIgnoreCase))return profile;
        throw new InvalidDataException("No pinned private-driver profile for this executable path.");
    }
    public static string GameRoot(string executable) {
        var profile=Profile(executable);string full=Path.GetFullPath(executable);
        return profile.Root==null?full.Substring(0,full.Length-profile.RelativeExecutable.Length-1):Path.GetFullPath(profile.Root);
    }
    const string Xe2="igd12um64xe2.dll";
    public static readonly Dictionary<string,string> Originals=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"clangFEWrapper.dll","0B92E83923B4546E84E307AC940676F299253E223BC3F39B2A9A0101FCB9C6D0"},
            {"DnnlPlugin.dll","830FAE35948D3912364BB2DACE719A85D1E450D810379C816B7D98BDB9CB2E87"},
            {"iga64.dll","1504040E97F3CFB66561DC199F19AB3AD82AF8D3AE8F9C690C074CAE72F2228E"},
            {"igc-default64.dll","55559222A70929C46C7B4000E019BFE5126FA8C051037CFDF62AC6F29BA53A89"},
            {"igc-fallback64.dll","FF95E410083871A410903CA091179F256AEC6B6E96716CC9DF86A9893BF4C0F3"},
            {"igc64.dll","39911E8E314686FD29DD0FEA4BFF1262CEBF59FAA107C0625347A087AC9352CB"},
            {"igd12dxva64.dll","01F5A99859004812C108BF0C41B120789A044E480DD4FFB106C7A3909330C862"},
            {"igd12um64xe2.dll","1A552E471AE986A52D045C4112E1980FB7171AABEBFF93C027A4BBA10A3C6229"},
            {"igd12umd64.dll","BC44EA9502D717E407B47D363A9436CCE2CE3B7C62CD181EFAEC075F3E61FAA9"},
            {"igd12umt64.dll","0B59B540FDDF541404A2090CEBD108A9F6483371E81B40258EF71F220EF77FA8"},
            {"igddxvacommon64.dll","C3FF451B4E02D69EEBE91C2494E995BD06483ED8339D18512F69C5311DAD5272"},
            {"igdext64.dll","77023AF5B37A0F6C131E6F187B0343824407BC6CE343F383998784B0F1E0D838"},
            {"igdfcl64.dll","4AAFD9F797A227CDAB388853BA835A5670F384CA98395E859A4CF7810B8D68AE"},
            {"igdgmm2_64.dll","DB2EA2A6F271CC85D097DC13176E153E5EBF67775155DF162B768A75ED863A2E"},
            {"igdgmm64.dll","DB3B3D6FBC6ABA6BA156080224916C1FEFCB1080F0EA3927727F52626623C681"},
            {"igdgmmstaging_64.dll","994D22569552487DCADBC8B1AA6AC9BEF0C24DF23E67637B4E2A2F63952C1E70"},
            {"igdinfo64.dll","C15C5EB83770455AB89E298346314572B9BE8BDE3AFD6161B425AA9A1EA9BE30"},
            {"igdmd64.dll","64F2680B79D9F2E30CC3B368A5C205FD8CC8A45155F68C9CC3400E180D8280DA"},
            {"igdml64.dll","02000B55E4AF662CA3D0C3999CCB021BCA99C66940C2EE97D3BEE6099BC4F5AA"},
            {"igmlsblob64xe2.dll","FA904D151ED0C25B062C1848ACF5C454805C1A989DB5C9294741818A24C4AC94"},
            {"igxess2.dll","2C49B09841D3D370D845A65DC7D49A857FAE8D15B90341816DDD11E9961EA789"},
            {"MSVCP140.dll","8E8F8293B3FF9DA7EB1B6B68132E330A7B04A01319B04A433DA2FF2D936B40EC"},
            {"opencl-clang264.dll","173F91A4A2D985DB86FF940486F32578BCAC83F0F89F36C7D4B8597B3433F7A0"},
            {"UniversalAdapter64.dll","CBBBDF2C03BB82B6F01AE11EAFD6C424F6770510943DF0E32B7F97D4B6CC034C"},
            {"VCRUNTIME140_1.dll","800A51AE7DC7154C2DC1F9C8FC70103D2E3A3B86BB1E7F7E2178FF516688E116"},
            {"VCRUNTIME140.dll","10B7E953B52F7D47471D86CB0E8CD279DC6BC7151556036EAD1DD6A9AD9F6F59"},
            {"ze_loader.dll","B902825D0193E1C86D8CC628913C7869A87B2FB1DBF81F86E0C20422269EA0A4"},
    };
    static void Require(bool yes,string message){if(!yes)throw new InvalidDataException(message);}
    static void Closed(string executable,ProfileInfo observed=null){var profile=observed??Profile(executable);foreach(string name in new[]{Path.GetFileNameWithoutExtension(executable),profile.Launcher}.Where(n=>n!=null))foreach(var process in Process.GetProcessesByName(name))using(process)throw new InvalidOperationException("Close "+profile.Name+" before changing its private set.");}
    // withDriverCopy false: for what only watches the game (the on-screen stats), where a mod beside the exe doesn't matter.
    public static void ValidateGame(string executable,ProfileInfo observed=null,bool withDriverCopy=true) {
        string full=Path.GetFullPath(executable);Engine.CheckPath(full);
        var profile=observed??Profile(full);
        Require(IsolationControl.FileHash(full)==profile.Hash,"Unsupported "+profile.Name+" executable. Launch the updated game with the stock driver to rediscover its profile.");
        if(withDriverCopy)Isolation.RequireNoLocalD3D12(full);
    }
    // Microsoft Windows 10.0.26100.9549 AMD64 helpers: source signatures were
    // verified during support qualification. Copy locally; never ship these DLLs.
    public static readonly Dictionary<string,string> DebugHelpers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"dbghelp.dll","2D2EC94891162A61D86789133623B47D64D74C15D6BE99473B25AD7A6468CF39"},
        {"dbgcore.dll","4AA8F5241B94752CAC649E61BDF57C8910E1DBF3D4E9759C658BB41A852F6CDF"}
    };
    public static bool NeedsDebugRoutingFix(string executable) {
        string helper=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable)),"dbghelp.dll");
        if(!File.Exists(helper))return false;Engine.CheckPath(helper);
        using(var reader=new BinaryReader(File.OpenRead(helper))) {
            Require(reader.BaseStream.Length>=64,"Truncated game debug helper.");Require(reader.ReadUInt16()==0x5A4D,"Unknown game debug helper.");
            reader.BaseStream.Position=60;uint pe=reader.ReadUInt32();Require(pe<=reader.BaseStream.Length-6,"Invalid game debug helper header.");
            reader.BaseStream.Position=pe;Require(reader.ReadUInt32()==0x4550,"Invalid game debug helper PE.");
            if(reader.ReadUInt16()!=0x14C)return false;
        }
        Require(IsolationControl.FileHash(helper)=="3DCE0A04D672F925E63CE3AFE2F9F2889CCD962C8B0F4DF2F4926E6B0F682AB3","Unqualified 32-bit dbghelp conflict. Private routing is blocked until a compatible fix is verified.");
        return true;
    }
    public static void RequireDebugCompatibility(MatchedSetReceipt receipt) {
        Require(!NeedsDebugRoutingFix(receipt.Executable)||(receipt.Schema==4||receipt.Schema==5&&receipt.DebugFixed),"Existing private setup needs the debug-helper compatibility update. Use the system driver again, then prepare a fresh original 512 MiB copy with this app.");
    }
    public static void ValidateDebugSources() {
        foreach(var file in DebugHelpers){string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),file.Key);Engine.CheckPath(source);Require(IsolationControl.FileHash(source)==file.Value,"Windows debug helper version is unsupported: "+file.Key+". No compatibility files copied.");}
    }
    static string Expected(string name,int mib) {
        Require(mib==512||mib==1024||mib==2048||mib==4096||mib==8192||mib==16384,"Supported experimental private limits: 512 MiB, 1/2/4/8/16 GiB.");
        return name.Equals(Xe2,StringComparison.OrdinalIgnoreCase)?mib==8192?LargeCachePatch.EightGiB:mib==16384?LargeCachePatch.SixteenGiB:mib==1024?Engine.OneGiB:mib==2048?Engine.TwoGiB:mib==4096?Engine.FourGiB:Engine.Original:Originals[name];
    }
    public static bool IsTrialReceipt(string path){if(!File.Exists(path))return false;Engine.CheckPath(path);Require(new FileInfo(path).Length<=1048576,"Setup record exceeds bounds.");var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));return data.ContainsKey("Schema")&&Convert.ToInt32(data["Schema"])==5;}
    static string Expected(string name,int mib,MatchedSetReceipt receipt){
        if(receipt==null||receipt.Schema!=5)return Expected(name,mib);
        int proofSize=receipt.TrialMiB==0?1024:receipt.TrialMiB;
        Require(mib==512||mib==proofSize,"Test this cache size on the selected driver before applying it to a game.");
        return name.Equals(receipt.Driver,StringComparison.OrdinalIgnoreCase)?mib==512?receipt.OriginalHash:receipt.TrialPatchedHash:receipt.OriginalFiles[name];
    }
    static Dictionary<string,string> Package(MatchedSetReceipt r){return r.Schema==5?r.OriginalFiles:Originals;}
    public static string OriginalDriverHash(MatchedSetReceipt r){return r.Schema==5?r.OriginalHash:Engine.Original;}
    public static string DriverName(MatchedSetReceipt r){return r.Schema==5?r.Driver:Xe2;}
    public static void RequireTrialCurrent(MatchedSetReceipt r){Require(r.Schema==5&&r.Trial,"This is not a game-trial record.");var proof=DriverTrial.VerifySuccessful(r.TrialReceipt,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),true);int proofSize=Convert.ToInt32(proof["MiB"]);Require((r.TrialMiB==0?proofSize:r.TrialMiB)==proofSize&&(string)proof["OriginalHash"]==r.OriginalHash&&(string)proof["PatchedHash"]==r.TrialPatchedHash&&(string)proof["Driver"]==r.Driver,"The game setup belongs to a different driver-size test.");var sources=(Dictionary<string,object>)proof["Sources"];Require(sources.Count==r.OriginalFiles.Count&&r.OriginalFiles.All(e=>sources.ContainsKey(e.Key)&&(string)sources[e.Key]==e.Value),"Game trial dependencies differ from the successful test.");}
    static byte[] PatchGame(byte[] current,int mib,MatchedSetReceipt receipt){if(receipt.Schema!=5)return Engine.Patch(current,mib);Require(Engine.Hash(current)==receipt.Files[receipt.Driver],"The game's test file changed.");string backup=Path.Combine(Path.GetDirectoryName(receipt.TrialReceipt),"backup",receipt.Driver);Engine.CheckPath(backup);Require(new FileInfo(backup).Length<=128L*1024*1024,"Trial backup exceeds bounds.");byte[] original=File.ReadAllBytes(backup);Require(Engine.Hash(original)==receipt.OriginalHash,"Trial backup changed.");byte[] result=DriverTrial.Patch(original,mib,receipt.Driver);Require(Engine.Hash(result)==Expected(receipt.Driver,mib,receipt),"Trial output does not match its successful check.");return result;}
    public static void ValidateReceipt(MatchedSetReceipt r) {
        Require(r!=null&&(r.Schema==3||r.Schema==4||r.Schema==5)&&r.Files!=null,"Unsupported matched-set receipt.");
        if(r.Schema==5){Require(r.Trial&&r.Game!=null&&r.Game.Schema==2&&r.OriginalFiles!=null&&r.OriginalFiles.Count<=128&&r.OriginalFiles.Count>=2&&DriverTrial.SupportedName(r.Driver),"Invalid game-trial record.");
            // Game.TrialReceipt is the proof recorded when the game's loaded driver
            // was observed. TrialReceipt is refreshed when another size is tested
            // or applied. Both can refer to different size tests on the same original
            // driver; the original hash and active proof are validated separately.
            Require(r.OriginalFiles.ContainsKey(r.Driver)&&r.OriginalFiles[r.Driver]==r.OriginalHash&&r.OriginalFiles.ContainsKey("igd12umd64.dll")&&r.Game.ObservedDriverHash==r.OriginalHash,"Game trial source differs.");
            foreach(var file in r.OriginalFiles)Require(Path.GetFileName(file.Key)==file.Key&&file.Key.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)&&System.Text.RegularExpressions.Regex.IsMatch(file.Value??"","\\A[0-9A-F]{64}\\z"),"Invalid trial dependency.");Require(System.Text.RegularExpressions.Regex.IsMatch(r.TrialPatchedHash??"","\\A[0-9A-F]{64}\\z"),"Invalid patched hash.");}
        var package=Package(r);Require(r.Files.Count==package.Count+((r.Schema==4||r.Schema==5&&r.DebugFixed)?DebugHelpers.Count:0),"Setup membership differs.");
        string exe=Path.GetFullPath(r.Executable),folder=Path.GetFullPath(r.Folder);
        if(r.Game!=null)Require(ProfileExecutable(r.Game).Equals(exe,StringComparison.OrdinalIgnoreCase),"Receipt game profile differs from its target.");else Profile(exe);
        Require(folder.Equals(exe+".local",StringComparison.OrdinalIgnoreCase),"Unexpected receipt target.");
        foreach(var entry in Package(r))Require(r.Files.ContainsKey(entry.Key)&&r.Files[entry.Key]==Expected(entry.Key,r.MiB,r),"Receipt does not match the pinned package.");
        if(r.Schema==4||r.Schema==5&&r.DebugFixed)foreach(var entry in DebugHelpers)Require(r.Files.ContainsKey(entry.Key)&&r.Files[entry.Key]==entry.Value,"Receipt does not match pinned Windows debug helpers.");
    }
    static void Save(string path,MatchedSetReceipt r) {
        path=Path.GetFullPath(path);Engine.CheckPath(Path.GetDirectoryName(path));
        string tmp=path+".pending";Require(!File.Exists(tmp),"Receipt staging file already exists.");
        byte[] bytes=System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(r));
        using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
        if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
    }
    internal static void SaveChangeStarted(string path,MatchedSetReceipt receipt,Action afterSave=null){receipt.Status="change-started";Save(path,receipt);if(afterSave!=null)afterSave();}
    public static MatchedSetReceipt Read(string path) {Engine.CheckPath(path);var r=new JavaScriptSerializer().Deserialize<MatchedSetReceipt>(File.ReadAllText(path));ValidateReceipt(r);return r;}
    internal static void MarkChangedRuntimeUnverified(MatchedSetReceipt receipt){receipt.Status="changed-runtime-unverified";}
    // Saves that the running game loaded this receipt's own driver file for its current size.
    // Change() resets Status to changed-runtime-unverified, so a verified status always refers to the current MiB.
    public static void RecordSizeLoading(string path,string observedDriverHash){var receipt=Read(path);ValidateContents(receipt);string driver=DriverName(receipt);Require(receipt.Files!=null&&receipt.Files.ContainsKey(driver)&&String.Equals(receipt.Files[driver],observedDriverHash,StringComparison.OrdinalIgnoreCase),"The loaded driver does not match this game's saved cache size.");receipt.Status=receipt.MiB==512?"original-control-loading-verified":"size-loading-verified";Save(path,receipt);}
    public static void RecordOriginalLoading(string path){var receipt=Read(path);Require(receipt.MiB==512,"Only unchanged original controls can be verified.");ValidateContents(receipt);receipt.Status="original-control-loading-verified";Save(path,receipt);}
    public static void ValidateContents(MatchedSetReceipt r) {
        ValidateReceipt(r);Engine.CheckPath(r.Folder);
        Require(Directory.EnumerateFileSystemEntries(r.Folder).Count()==r.Files.Count,"Private set contains unexpected files; nothing will be removed.");
        foreach(var entry in r.Files){string file=Path.Combine(r.Folder,entry.Key);Engine.CheckPath(file);Require(File.Exists(file)&&IsolationControl.FileHash(file)==entry.Value,"Private set changed: "+entry.Key);}
    }
    internal static void ValidatePrepareObservation(ProfileInfo profile,IDictionary<string,object> proof) {
        Require(profile!=null&&proof!=null&&proof.ContainsKey("OriginalHash")&&profile.Schema==2&&String.Equals(profile.ObservedDriverHash,Convert.ToString(proof["OriginalHash"]),StringComparison.OrdinalIgnoreCase),"Start this game once with its normal driver so Arc Cache can find it.");
    }
    public static string Prepare(string executable,string driverFolder,string receiptPath,int mib,string trialReceipt=null) {
        Require(mib!=4096||trialReceipt!=null,"A 4 GiB copy must use its own chunked driver-copy test first.");
        ValidateGame(executable);Closed(executable);driverFolder=Path.GetFullPath(driverFolder);Engine.CheckPath(driverFolder);
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\DriverStore\FileRepository")+Path.DirectorySeparatorChar;
        Require(driverFolder.StartsWith(root,StringComparison.OrdinalIgnoreCase),"Choose the installed driver package inside DriverStore.");
        using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"))
            Require(key!=null&&Convert.ToInt32(key.GetValue("DevOverrideEnable",0))==1,"Developer override must already be explicitly enabled and Windows rebooted. No setting was changed.");
        string full=Path.GetFullPath(executable),folder=full+".local";receiptPath=Path.GetFullPath(receiptPath);
        Require(!receiptPath.StartsWith(Path.GetDirectoryName(full)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Save the receipt outside the game directory.");
        Engine.CheckPath(Path.GetDirectoryName(receiptPath));Require(!File.Exists(receiptPath)&&!Directory.Exists(folder)&&!File.Exists(folder),"Receipt or redirection path already exists.");
        Dictionary<string,string> package=Originals;Dictionary<string,object> proof=null;
        if(trialReceipt==null)Require(Profile(full).Schema!=2,"Use the explicit game-trial setup action for this profile.");
        if(trialReceipt!=null){Require(mib==512,"Start game trials with an unchanged copy.");proof=DriverTrial.VerifySuccessful(trialReceipt,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),true);Require(((string)proof["SourceFolder"]).Equals(driverFolder,StringComparison.OrdinalIgnoreCase),"The selected driver differs from this test.");package=((Dictionary<string,object>)proof["Sources"]).ToDictionary(e=>e.Key,e=>(string)e.Value,StringComparer.OrdinalIgnoreCase);var trialProfile=Profile(full);ValidatePrepareObservation(trialProfile,proof);}
        foreach(var entry in package) {
            Require(!File.Exists(Path.Combine(Path.GetDirectoryName(full),entry.Key)),"Game root already contains Intel package DLL: "+entry.Key);
            string source=Path.Combine(driverFolder,entry.Key);Engine.CheckPath(source);Require(IsolationControl.FileHash(source)==entry.Value,"Unsupported or changed package file: "+entry.Key);
        }
        long total=0;foreach(var entry in package)total=checked(total+new FileInfo(Path.Combine(driverFolder,entry.Key)).Length);Require(new DriveInfo(Path.GetPathRoot(folder)).AvailableFreeSpace>=total+64L*1024*1024,"Not enough disk space for this game copy.");
        bool fixDebug=NeedsDebugRoutingFix(full);if(fixDebug)ValidateDebugSources();
        var gameProfile=Profile(full);
        var r=new MatchedSetReceipt{Schema=trialReceipt!=null?5:fixDebug?4:3,Trial=trialReceipt!=null,DebugFixed=fixDebug,TrialReceipt=trialReceipt,TrialMiB=proof==null?0:Convert.ToInt32(proof["MiB"]),Driver=proof==null?Xe2:(string)proof["Driver"],OriginalHash=proof==null?Engine.Original:(string)proof["OriginalHash"],TrialPatchedHash=proof==null?Engine.OneGiB:(string)proof["PatchedHash"],OriginalFiles=proof==null?null:package,Executable=full,Folder=folder,MiB=mib,Status="prepare-started",Files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase),Game=gameProfile.Schema==1||gameProfile.Schema==2?gameProfile:null};
        foreach(var entry in package)r.Files.Add(entry.Key,Expected(entry.Key,mib,r));if(fixDebug)foreach(var entry in DebugHelpers)r.Files.Add(entry.Key,entry.Value);Save(receiptPath,r);
        string staging=folder+".pending-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(staging);
        try {
            foreach(var entry in package) {
                byte[] bytes=File.ReadAllBytes(Path.Combine(driverFolder,entry.Key));Require(Engine.Hash(bytes)==entry.Value,"Source changed during copy.");
                if(entry.Key.Equals(DriverName(r),StringComparison.OrdinalIgnoreCase))bytes=PatchGame(bytes,mib,r);
                string dest=Path.Combine(staging,entry.Key);
                using(var stream=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                Require(IsolationControl.FileHash(dest)==r.Files[entry.Key],"Copy failed validation.");
            }
            if(fixDebug)foreach(var entry in DebugHelpers) {
                string source=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),entry.Key);Engine.CheckPath(source);
                byte[] bytes=File.ReadAllBytes(source);Require(Engine.Hash(bytes)==entry.Value,"Windows debug source changed during copy.");
                string dest=Path.Combine(staging,entry.Key);using(var stream=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                Require(IsolationControl.FileHash(dest)==entry.Value,"Windows debug copy failed validation.");
            }
            Closed(full);ValidateGame(full);Directory.Move(staging,folder);ValidateContents(r);r.Status="prepared-runtime-unverified";Save(receiptPath,r);
            return "Game copy prepared: "+mib+" MiB. Runtime loading and cache effect require verification.\r\n"+folder+"\r\nRecovery receipt: "+receiptPath;
        } catch {r.Status="recovery-required";Save(receiptPath,r);throw;}
        // Failed staging is retained with pinned hashes for inspection; never recursively remove game paths.
    }
    public static string ChangeCap(string receiptPath,int mib,string backupRoot=null) {
        var r=Read(receiptPath);Require(mib!=4096||r.Schema==5,"A 4 GiB copy must use its own chunked driver-copy test first.");Closed(r.Executable,r.Game);ValidateGame(r.Executable,r.Game);ValidateContents(r);RequireDebugCompatibility(r);if(r.Schema==5){RequireTrialCurrent(r);string proofPath=DriverTrial.FindSuccessful(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),mib,r.Driver);Require(proofPath!=null,"Test "+(mib==512?"Default":(mib/1024)+" GB")+" first on this exact driver.");var proof=DriverTrial.VerifySuccessful(proofPath,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcCache.DriverProbe.exe"),true);Require((string)proof["OriginalHash"]==r.OriginalHash,"This size test belongs to a different driver file.");var proofSources=(Dictionary<string,object>)proof["Sources"];Require(proofSources.Count==r.OriginalFiles.Count&&r.OriginalFiles.All(e=>proofSources.ContainsKey(e.Key)&&(string)proofSources[e.Key]==e.Value),"This size test belongs to a different driver package.");r.TrialReceipt=proofPath;r.TrialMiB=mib;r.TrialPatchedHash=(string)proof["PatchedHash"];}
        Expected(DriverName(r),mib,r);
        string target=Path.Combine(r.Folder,DriverName(r)),staging=target+".pending";Require(!File.Exists(staging),"Staged DLL already exists; recover before continuing.");
        var previousAcl=Engine.Security(target);string backup=null;byte[] before=File.ReadAllBytes(target);
        byte[] bytes=PatchGame(r.Schema==5?before:Engine.Read(target),mib,r);
        if(DriverTrial.RequiresChangeJournal(r.MiB,mib)){
            string backups=backupRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","large-cache-backups");Directory.CreateDirectory(backups);Engine.CheckPath(backups);
            backup=Path.Combine(backups,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
            WriteLargeChangeJournal(backup,target,before,bytes,previousAcl,receiptPath,r);
        }
        SaveChangeStarted(receiptPath,r);
        using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
        try {
            Closed(r.Executable,r.Game);Require(IsolationControl.FileHash(target)==r.Files[DriverName(r)],"Private DLL changed before replacement.");
            File.Replace(staging,target,null);r.MiB=mib;r.Files[DriverName(r)]=Expected(DriverName(r),mib,r);Require(Engine.Equivalent(Engine.Security(target),previousAcl),"Private driver ACL changed during replacement; inspect the backup journal.");MarkChangedRuntimeUnverified(r);Save(receiptPath,r);ValidateContents(r);
            return "Private Xe2 copy changed to "+mib+" MiB. Installed driver unchanged. Re-launch the game in DX12 and observe the loaded path."+(backup==null?"":"\r\nBackup of previous DLL, next DLL, ACL and receipt: "+backup);
        } catch {r.Status="recovery-required";Save(receiptPath,r);throw;}
    }
    static void WriteLargeChangeJournal(string folder,string target,byte[] before,byte[] after,AclSnapshot acl,string receiptPath,MatchedSetReceipt receipt) {
        Require(!Directory.Exists(folder)&&!File.Exists(folder),"Backup journal path already exists.");Directory.CreateDirectory(folder);Engine.CheckPath(folder);
        string beforePath=Path.Combine(folder,"before.dll"),afterPath=Path.Combine(folder,"after.dll"),receiptCopy=Path.Combine(folder,"game-receipt-before.json");
        using(var s=new FileStream(beforePath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(before,0,before.Length);s.Flush(true);}
        using(var s=new FileStream(afterPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(after,0,after.Length);s.Flush(true);}
        File.Copy(receiptPath,receiptCopy);
        Require(Engine.Hash(File.ReadAllBytes(beforePath))==Engine.Hash(before)&&Engine.Hash(File.ReadAllBytes(afterPath))==Engine.Hash(after),"Large cache change backup verification failed.");
        Require(Engine.Hash(before)==receipt.Files[DriverName(receipt)],"Private driver differs from the receipt before backup.");
        var journal=new {Schema=1,Kind="Private game driver size-change recovery",Target=Path.GetFullPath(target),OriginalHash=Engine.Hash(before),NextHash=Engine.Hash(after),OriginalAcl=acl,Driver=DriverName(receipt),PreviousMiB=receipt.MiB,NextMiB=receipt.TrialMiB,CreatedUtc=DateTime.UtcNow.ToString("o")};
        File.WriteAllText(Path.Combine(folder,"journal.json"),new JavaScriptSerializer().Serialize(journal));
    }
    // ---- Repair: finish or undo what an interrupted Prepare, size change or removal left behind ----
    // Deletes only files it can prove are this setup's own: names from the receipt with the receipt's hash, or the one
    // staging name a size change uses while the real driver file is still the receipt's. Anything else stops it.
    internal sealed class RepairResult {public List<string> Done=new List<string>();public bool StatusReset,Removed;}
    static bool IsRegularFile(string path){return File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
    // A failed Prepare's "<exe>.local.pending-<guid>" folder next to the exe: removed when every file in it is one of the
    // receipt's files with the receipt's hash. The patched driver in it carries the receipt's hash too.
    internal static void RepairPrepareStaging(string executable,IDictionary<string,string> files,RepairResult result){
        string full=Path.GetFullPath(executable),dir=Path.GetDirectoryName(full),prefix=Path.GetFileName(full)+".local.pending-";
        foreach(string staging in Directory.GetDirectories(dir,prefix+"*")){
            string suffix=Path.GetFileName(staging).Substring(prefix.Length);
            if(!System.Text.RegularExpressions.Regex.IsMatch(suffix,"\\A[0-9a-f]{32}\\z"))continue;
            Engine.CheckPath(staging);
            Require(Directory.GetDirectories(staging).Length==0,"An unfinished setup folder contains subfolders; nothing was removed: "+staging);
            var entries=Directory.GetFiles(staging);string expected;
            foreach(string file in entries)Require(IsRegularFile(file)&&files.TryGetValue(Path.GetFileName(file),out expected)&&IsolationControl.FileHash(file)==expected,"An unfinished setup folder contains a file Arc Cache did not write; nothing was removed: "+file);
            foreach(string file in entries)File.Delete(file);Directory.Delete(staging,false);result.Done.Add("Removed an unfinished setup folder ("+entries.Length+" files).");
        }
    }
    // Inside the game's .local folder. removeStarted: a removal stopped half way, so every remaining file must be one of
    // the receipt's and the removal is finished. Otherwise only the size change's "<driver>.pending" goes, and only while
    // the real driver file is still exactly the receipt's (the change never replaced it).
    internal static void RepairFolder(string folder,IDictionary<string,string> files,string driver,bool removeStarted,RepairResult result){
        if(!Directory.Exists(folder))return;Engine.CheckPath(folder);
        Require(Directory.GetDirectories(folder).Length==0,"The driver copy folder contains subfolders; nothing was removed.");
        string target=Path.Combine(folder,driver),staging=target+".pending",expected;
        if(File.Exists(staging)){
            Require(IsRegularFile(staging),"The staged driver file is a link; nothing was removed.");
            Require(IsRegularFile(target)&&files.TryGetValue(driver,out expected)&&IsolationControl.FileHash(target)==expected,"A size change stopped after replacing the driver file, so Arc Cache can't tell which file is right. Nothing was removed. The backup journal under large-cache-backups has the previous file.");
            File.Delete(staging);result.Done.Add("Removed the unfinished size change ("+Path.GetFileName(staging)+"). The previous size stays.");result.StatusReset=true;
        }
        if(!removeStarted)return;
        var entries=Directory.GetFiles(folder);
        foreach(string file in entries)Require(IsRegularFile(file)&&files.TryGetValue(Path.GetFileName(file),out expected)&&IsolationControl.FileHash(file)==expected,"The driver copy folder contains a file Arc Cache did not write; nothing more was removed: "+file);
        foreach(string file in entries)File.Delete(file);Directory.Delete(folder,false);result.Done.Add("Finished removing the driver copy ("+entries.Length+" files were left).");result.Removed=true;
    }
    public static string Repair(string receiptPath){
        receiptPath=Path.GetFullPath(receiptPath);var r=Read(receiptPath);Closed(r.Executable,r.Game);var result=new RepairResult();
        // A receipt save that stopped before its rename leaves "<receipt>.pending"; the receipt itself just read fine.
        string pendingReceipt=receiptPath+".pending";if(IsRegularFile(pendingReceipt)){File.Delete(pendingReceipt);result.Done.Add("Removed an unfinished record save.");}
        RepairPrepareStaging(r.Executable,r.Files,result);
        RepairFolder(r.Folder,r.Files,DriverName(r),r.Status=="remove-started",result);
        if(result.Removed){r.Status="removed";Save(receiptPath,r);}
        else if(result.StatusReset){MarkChangedRuntimeUnverified(r);Save(receiptPath,r);}
        return result.Done.Count==0?"":String.Join(" ",result.Done);
    }
    public static string Remove(string receiptPath) {
        var r=Read(receiptPath);Closed(r.Executable,r.Game);
        if(!Directory.Exists(r.Folder)){r.Status="removed";Save(receiptPath,r);return "Private set is already absent.";}
        ValidateContents(r);r.Status="remove-started";Save(receiptPath,r);
        foreach(var entry in r.Files) {Closed(r.Executable,r.Game);string file=Path.Combine(r.Folder,entry.Key);Engine.CheckPath(file);Require(IsolationControl.FileHash(file)==entry.Value,"File changed during removal.");File.Delete(file);}
        Directory.Delete(r.Folder,false);r.Status="removed";Save(receiptPath,r);return "Owned private driver set removed. Installed driver and Windows settings unchanged.";
    }
}
}
