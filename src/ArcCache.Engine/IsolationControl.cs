using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Principal;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ArcCap {
public class OverrideReceipt {
    public int Schema {get;set;}public bool WasPresent {get;set;}public int Before {get;set;}
    public string Status {get;set;}public string CreatedUtc {get;set;}
}
public class PrivateCopyReceipt {
    public int Schema {get;set;}public string Executable {get;set;}public string ExecutableHash {get;set;}
    public string Folder {get;set;}public string DllHash {get;set;}public int MiB {get;set;}public string Status {get;set;}
}
public static class IsolationControl {
    const string Key=@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    internal sealed class HashEntry {public long Length,WriteTicks,CreationTicks;public string Hash;[System.Web.Script.Serialization.ScriptIgnore]public bool Fresh;}
    [ThreadStatic]static Dictionary<string,HashEntry> readOnlyHashCache;
    sealed class HashCacheScope : IDisposable {readonly Dictionary<string,HashEntry> previous;public HashCacheScope(){previous=readOnlyHashCache;readOnlyHashCache=new Dictionary<string,HashEntry>(StringComparer.OrdinalIgnoreCase);}public void Dispose(){readOnlyHashCache=previous;}}
    public static IDisposable BeginReadOnlyHashCache(){return new HashCacheScope();}
    // Read-only checks (what the app shows, not what it changes) reuse hashes across worker processes: an entry counts
    // only while the file's size, last write and creation time are unchanged. Every action that changes files, and the
    // check before a game launch, hashes afresh.
    public static string HashCachePath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","hash-cache.json");}}
    const int HashCacheLimit=4096;
    sealed class PersistentHashCacheScope : IDisposable {
        readonly Dictionary<string,HashEntry> previous;readonly string path;readonly int loaded;
        public PersistentHashCacheScope(string file){
            previous=readOnlyHashCache;path=file;var entries=new Dictionary<string,HashEntry>(StringComparer.OrdinalIgnoreCase);
            try{if(File.Exists(path)&&new FileInfo(path).Length<=4*1024*1024){var saved=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<Dictionary<string,HashEntry>>(File.ReadAllText(path));if(saved!=null)foreach(var e in saved)if(e.Value!=null&&e.Value.Hash!=null&&e.Value.Hash.Length==64)entries[e.Key]=e.Value;}}catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException))throw;entries.Clear();}
            loaded=entries.Count;readOnlyHashCache=entries;
        }
        public void Dispose(){
            var entries=readOnlyHashCache;readOnlyHashCache=previous;if(entries==null||!entries.Values.Any(e=>e.Fresh))return;
            try{
                var keep=entries.Count<=HashCacheLimit?entries:entries.Where(e=>e.Value.Fresh).Take(HashCacheLimit).ToDictionary(e=>e.Key,e=>e.Value,StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllText(tmp,new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(keep));
                try{if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
            }catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException))throw;}
        }
    }
    public static IDisposable BeginPersistentHashCache(string file=null){return new PersistentHashCacheScope(file??HashCachePath);}
    // SHA256Cng uses Windows CNG (with the CPU's SHA instructions); SHA256.Create() is the much slower managed one on .NET Framework.
    internal static System.Security.Cryptography.HashAlgorithm NewSha256(){return new System.Security.Cryptography.SHA256Cng();}
    public static string ReceiptPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","developer-override.json");}}
    public static bool IsOverrideEnabled(){using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))using(var key=root.OpenSubKey(Key)){return key!=null&&Convert.ToInt32(key.GetValue("DevOverrideEnable",0))==1;}}
    public static bool Administrator {get{using(var id=WindowsIdentity.GetCurrent())return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);}}
    public static string FileHash(string path) {string full=Path.GetFullPath(path);var info=new FileInfo(full);if(readOnlyHashCache!=null){HashEntry old;if(readOnlyHashCache.TryGetValue(full,out old)&&old.Length==info.Length&&old.WriteTicks==info.LastWriteTimeUtc.Ticks&&old.CreationTicks==info.CreationTimeUtc.Ticks)return old.Hash;}string hash;using(var s=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read,1<<20,FileOptions.SequentialScan))using(var h=NewSha256())hash=BitConverter.ToString(h.ComputeHash(s)).Replace("-","");if(readOnlyHashCache!=null)readOnlyHashCache[full]=new HashEntry{Length=info.Length,WriteTicks=info.LastWriteTimeUtc.Ticks,CreationTicks=info.CreationTimeUtc.Ticks,Hash=hash,Fresh=true};return hash;}
    static void Save<T>(string path,T receipt) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";
        byte[] bytes=System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(receipt));
        using(var s=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){s.Write(bytes,0,bytes.Length);s.Flush(true);}
        if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
    }
    static T Read<T>(string path){return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path));}
    public static string Enable() {
        if(!Administrator)throw new InvalidOperationException("Close this app and manually use Run as administrator. Automatic elevation is not performed.");
        using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
        using(var key=root.OpenSubKey(Key,true)) {
            if(key==null)throw new IOException("Windows loader settings key is unavailable.");
            object value=key.GetValue("DevOverrideEnable",null);
            if(value!=null&&key.GetValueKind("DevOverrideEnable")!=RegistryValueKind.DWord)throw new InvalidDataException("Unexpected registry value type. No change made.");
            int old=value==null?0:Convert.ToInt32(value);
            if(old!=0&&old!=1)throw new InvalidDataException("Unexpected existing registry value. No change made.");
            if(File.Exists(ReceiptPath)) {
                var previous=Read<OverrideReceipt>(ReceiptPath);
                if(previous==null||previous.Schema!=1)throw new InvalidDataException("Unsupported override receipt. No change made.");
                if(previous.Status=="enabled-reboot-required"||previous.Status=="enable-started") {
                    if(old==1)return "Setting is already enabled; original setting remains saved. Reboot before the test. Receipt: "+ReceiptPath;
                    throw new InvalidDataException("A previous enable receipt exists but Windows no longer matches it. Inspect the receipt before continuing.");
                }
                if(previous.Status!="restored-reboot-required")throw new InvalidDataException("Previous setting operation needs recovery. Refusing to overwrite its receipt.");
            }
            var receipt=new OverrideReceipt{Schema=1,WasPresent=value!=null,Before=old,Status="enable-started",CreatedUtc=DateTime.UtcNow.ToString("O")};
            Save(ReceiptPath,receipt); // durable previous value before the registry write
            object recheck=key.GetValue("DevOverrideEnable",null);
            if((value==null)!=(recheck==null)||(value!=null&&(key.GetValueKind("DevOverrideEnable")!=RegistryValueKind.DWord||Convert.ToInt32(recheck)!=old)))throw new IOException("Windows setting changed during backup. No registry write performed.");
            key.SetValue("DevOverrideEnable",1,RegistryValueKind.DWord);key.Flush();
            if(Convert.ToInt32(key.GetValue("DevOverrideEnable",0))!=1)throw new IOException("Setting read-back failed. Original value is recorded at "+ReceiptPath);
            receipt.Status="enabled-reboot-required";Save(ReceiptPath,receipt);
            return "Developer DLL redirection enabled. REBOOT REQUIRED; this app does not reboot Windows.\r\nOriginal value saved: "+ReceiptPath+"\r\nSignature enforcement and the installed driver were not changed.";
        }
    }
    public static string Restore() {
        if(!Administrator)throw new InvalidOperationException("Manually run as administrator to restore the Windows setting.");
        var receipt=Read<OverrideReceipt>(ReceiptPath);
        if(receipt==null||receipt.Schema!=1||receipt.Before<0||receipt.Before>1)throw new InvalidDataException("Override receipt is invalid.");
        if(receipt.Status=="restored-reboot-required")return "Original setting was already restored. Reboot to apply it.";
        if(receipt.Status!="enabled-reboot-required"&&receipt.Status!="enable-started"&&receipt.Status!="restore-started")throw new InvalidDataException("Unexpected override receipt status.");
        using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
        using(var key=root.OpenSubKey(Key,true)) {
            object value=key.GetValue("DevOverrideEnable",null);
            bool matchesOriginal=receipt.WasPresent?value!=null&&key.GetValueKind("DevOverrideEnable")==RegistryValueKind.DWord&&Convert.ToInt32(value)==receipt.Before:value==null;
            if(!matchesOriginal&&(value==null||key.GetValueKind("DevOverrideEnable")!=RegistryValueKind.DWord||Convert.ToInt32(value)!=1))throw new InvalidDataException("Windows setting changed outside this tool. Refusing to overwrite it.");
            receipt.Status="restore-started";Save(ReceiptPath,receipt);
            if(receipt.WasPresent)key.SetValue("DevOverrideEnable",receipt.Before,RegistryValueKind.DWord);else key.DeleteValue("DevOverrideEnable",false);
            key.Flush();object restored=key.GetValue("DevOverrideEnable",null);
            if(receipt.WasPresent?restored==null||Convert.ToInt32(restored)!=receipt.Before:restored!=null)throw new IOException("Restored setting did not pass read-back. Keep the receipt for recovery.");
            receipt.Status="restored-reboot-required";Save(ReceiptPath,receipt);
            return "Original developer-override setting restored. REBOOT REQUIRED.\r\nThe game-specific .local copy is separate; remove it using its saved receipt. Installed driver unchanged.";
        }
    }
    public static PrivateCopyReceipt PreparePrivateCopy(string executable,string original,string receiptPath,int mib) {
        string full=Path.GetFullPath(executable);Engine.CheckPath(full);
        if(!String.Equals(Path.GetFileName(full),"witcher3.exe",StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetFileName(Path.GetDirectoryName(full)),"x64_dx12",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This test workflow is restricted to Witcher 3's x64_dx12\\witcher3.exe.");
        string exeHash=FileHash(full);
        if(exeHash!="2DF765398A3D1F165DB418271BE7200178320A4466817379F2FA0DC0001FB101")throw new InvalidDataException("Witcher executable differs from the inspected test build. Reinspect before support is expanded.");
        Isolation.RequireNoLocalD3D12(full);
        if(System.Diagnostics.Process.GetProcessesByName("witcher3").Length>0)throw new InvalidOperationException("Close Witcher 3 before preparing its test copy.");
        byte[] stock=Engine.Read(original);if(Engine.Hash(stock)!=Engine.Original)throw new InvalidDataException("The private-copy source must be the verified original Intel DLL.");
        byte[] patched=Engine.Patch(stock,mib);string folder=full+".local",staging=folder+".pending-"+Guid.NewGuid().ToString("N");
        receiptPath=Path.GetFullPath(receiptPath);
        if(receiptPath.StartsWith(Path.GetDirectoryName(full)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Save the recovery receipt outside the game's executable directory.");
        if(File.Exists(folder)||Directory.Exists(folder)||File.Exists(receiptPath))throw new IOException("A redirection path or receipt already exists. Refusing to overwrite it.");
        // A root-level driver copy could also affect routing; do not overwrite or ignore it.
        if(File.Exists(Path.Combine(Path.GetDirectoryName(full),"igd12um64xe2.dll")))throw new IOException("An existing game-directory Intel DLL needs inspection first.");
        var receipt=new PrivateCopyReceipt{Schema=1,Executable=full,ExecutableHash=exeHash,Folder=folder,DllHash=Engine.Hash(patched),MiB=mib,Status="prepare-started"};
        Save(receiptPath,receipt);
        try {
            Directory.CreateDirectory(staging);string dll=Path.Combine(staging,"igd12um64xe2.dll");
            using(var stream=new FileStream(dll,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(patched,0,patched.Length);stream.Flush(true);}
            if(Engine.Hash(Engine.Read(dll))!=receipt.DllHash||FileHash(full)!=exeHash)throw new IOException("Private copy or executable changed during preparation.");
            Directory.Move(staging,folder);receipt.Status="prepared-not-runtime-verified";Save(receiptPath,receipt);return receipt;
        } catch {receipt.Status=Directory.Exists(folder)?"recovery-required":"prepare-failed";Save(receiptPath,receipt);throw;}
        finally {
            if(Directory.Exists(staging)) {
                // Delete only our one known staged file; no recursive deletion of a game directory.
                string dll=Path.Combine(staging,"igd12um64xe2.dll");if(File.Exists(dll)&&FileHash(dll)==receipt.DllHash)File.Delete(dll);
                if(!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);
            }
        }
    }
    public static string RemovePrivateCopy(string receiptPath) {
        var r=Read<PrivateCopyReceipt>(receiptPath);if(r==null||r.Schema!=1)throw new InvalidDataException("Private-copy receipt is invalid.");
        string full=Path.GetFullPath(r.Executable),folder=Path.GetFullPath(r.Folder);
        if(folder!=full+".local"||Path.GetFileName(full)!="witcher3.exe")throw new InvalidDataException("Unexpected receipt target.");
        if(System.Diagnostics.Process.GetProcessesByName("witcher3").Length>0)throw new InvalidOperationException("Close Witcher 3 before removing its private DLL.");
        if(!Directory.Exists(folder)) {r.Status="removed";Save(receiptPath,r);return "Private-copy directory is already absent.";}
        Engine.CheckPath(folder);string dll=Path.Combine(folder,"igd12um64xe2.dll");
        if(Directory.EnumerateFileSystemEntries(folder).Count()!=1||!File.Exists(dll)||FileHash(dll)!=r.DllHash)throw new InvalidDataException("Private-copy folder contents changed. Refusing to delete anything.");
        var info=Engine.Inspect(Engine.Read(dll));if(info.Hash!=r.DllHash||info.MiB!=r.MiB)throw new InvalidDataException("Private DLL does not match the supported receipt.");
        File.Delete(dll);Directory.Delete(folder);r.Status="removed";Save(receiptPath,r);return "Owned Witcher private copy removed. No original game files were modified.";
    }
}
}
