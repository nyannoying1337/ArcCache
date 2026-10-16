using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace ArcCap {
public sealed class ArcCacheResetResult {
    public bool Completed {get;set;}
    public long BytesFreed {get;set;}
    public bool RestartRequired {get;set;}
    public string Message {get;set;}
    public string[] RemovedGameFolders {get;set;}
    public string[] BlockedFolders {get;set;}
}
internal sealed class ArcCacheMaintenanceHooks {
    public Func<string,MatchedSetReceipt> ReadReceipt;
    public Action<MatchedSetReceipt> ValidateContents;
    public Func<string,string> RemoveReceipt;
    public Func<string,bool> IsGameRunning;
    public Func<bool> IsAdministrator;
    public Func<bool> OverrideEnabled;
    public Func<bool> OverrideReceiptExists;
    public Func<string> RestoreOverride;
}
public static class ArcCacheMaintenance {
    static readonly string[] DataDirectories={"driver-trials","large-cache-backups","driver-reports","profiles","games","icons","logs"};
    static readonly string[] DataFiles={"recent-games.json","manual-games.json","introduction-version.txt","graphics-selection.json","diagnostics.json","last-qualified-driver.json","scskiller-bridge.json","scskiller-bridge.json.bak","cache-files.json","developer-override.json"};
    static bool Under(string root,string path){string full=Path.GetFullPath(path);return full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
    static void NoReparse(string path){if(File.Exists(path)||Directory.Exists(path)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("A link or junction blocks reset: "+path);}}
    static void ValidateTree(string path){NoReparse(path);if(!Directory.Exists(path))return;foreach(string file in Directory.GetFiles(path)){NoReparse(file);}foreach(string directory in Directory.GetDirectories(path)){NoReparse(directory);ValidateTree(directory);}}
    static long TreeBytes(string path,string keepFile){if(!File.Exists(path)&&!Directory.Exists(path))return 0;NoReparse(path);if(File.Exists(path))return path.Equals(keepFile,StringComparison.OrdinalIgnoreCase)?0:new FileInfo(path).Length;long total=0;foreach(string file in Directory.GetFiles(path)){NoReparse(file);if(!file.Equals(keepFile,StringComparison.OrdinalIgnoreCase))total=checked(total+new FileInfo(file).Length);}foreach(string directory in Directory.GetDirectories(path)){NoReparse(directory);total=checked(total+TreeBytes(directory,keepFile));}return total;}
    static long DeleteTree(string path,string keepFile){if(!File.Exists(path)&&!Directory.Exists(path))return 0;NoReparse(path);if(File.Exists(path)){if(path.Equals(keepFile,StringComparison.OrdinalIgnoreCase))return 0;long size=new FileInfo(path).Length;File.Delete(path);return size;}long total=0;foreach(string file in Directory.GetFiles(path)){NoReparse(file);if(!file.Equals(keepFile,StringComparison.OrdinalIgnoreCase)){total=checked(total+new FileInfo(file).Length);File.Delete(file);}}foreach(string directory in Directory.GetDirectories(path)){NoReparse(directory);total=checked(total+DeleteTree(directory,keepFile));}if(!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path,false);return total;}
    static bool Running(string executable){if(String.IsNullOrWhiteSpace(executable))return false;string name=Path.GetFileNameWithoutExtension(executable);foreach(Process process in Process.GetProcessesByName(name))using(process)return true;return false;}
    static ArcCacheResetResult Blocked(string folder,string message){return new ArcCacheResetResult{Completed=false,BytesFreed=0,RestartRequired=false,RemovedGameFolders=new string[0],BlockedFolders=new[]{folder},Message=message};}
    internal static ArcCacheResetResult Reset(string dataRoot,string currentLog,ArcCacheMaintenanceHooks hooks=null){
        string root=Path.GetFullPath(dataRoot);if(String.IsNullOrWhiteSpace(root)||root.Equals(Path.GetPathRoot(root),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Refusing to reset an invalid data folder.");NoReparse(root);if(!Directory.Exists(root))throw new DirectoryNotFoundException("Arc Cache data folder was not found.");
        string log=String.IsNullOrWhiteSpace(currentLog)?null:Path.GetFullPath(currentLog);string logRoot=Path.Combine(root,"logs");if(log==null||!Under(logRoot,log)||!File.Exists(log))throw new InvalidDataException("The current session log could not be verified, so reset was stopped before making changes.");Engine.CheckPath(log);NoReparse(log);
        if(hooks==null)hooks=new ArcCacheMaintenanceHooks{ReadReceipt=MatchedSet.Read,ValidateContents=MatchedSet.ValidateContents,RemoveReceipt=MatchedSet.Remove,IsGameRunning=Running,IsAdministrator=()=>IsolationControl.Administrator,OverrideEnabled=()=>IsolationControl.IsOverrideEnabled(),OverrideReceiptExists=()=>File.Exists(IsolationControl.ReceiptPath),RestoreOverride=()=>IsolationControl.Restore()};
        string games=Path.Combine(root,"games");var receipts=Directory.Exists(games)?Directory.GetFiles(games,"*.json",SearchOption.TopDirectoryOnly):new string[0];var saved=new List<KeyValuePair<string,MatchedSetReceipt>>();
        foreach(string file in receipts){NoReparse(file);var receipt=hooks.ReadReceipt(file);if(receipt==null||String.IsNullOrWhiteSpace(receipt.Executable)||String.IsNullOrWhiteSpace(receipt.Folder)||!Path.GetFullPath(receipt.Folder).Equals(Path.GetFullPath(receipt.Executable)+".local",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A game recovery record cannot be safely checked: "+file);saved.Add(new KeyValuePair<string,MatchedSetReceipt>(file,receipt));if(File.Exists(receipt.Folder))return Blocked(receipt.Folder,"A file blocks this game's private driver folder. Arc Cache left the Windows setting and its saved data unchanged.");if(Directory.Exists(receipt.Folder)){try{NoReparse(receipt.Folder);if(hooks.IsGameRunning(receipt.Executable))return Blocked(receipt.Folder,"Close this game before resetting Arc Cache. Nothing was changed.");hooks.ValidateContents(receipt);}catch(Exception error){return Blocked(receipt.Folder,"This game folder could not be checked safely, so Arc Cache stopped before restoring the Windows setting or deleting saved data. Nothing else was changed. "+error.Message);}}}
        bool overrideExists=hooks.OverrideReceiptExists(),overrideEnabled=hooks.OverrideEnabled();if(overrideEnabled&&!overrideExists)throw new InvalidOperationException("The Windows game-specific setting is on, but Arc Cache has no saved previous value. Nothing was changed.");if(overrideExists&&!hooks.IsAdministrator())throw new InvalidOperationException("Run Arc Cache as administrator before resetting, so it can safely restore the Windows setting. Nothing was changed.");
        foreach(string name in DataDirectories){string path=Path.Combine(root,name);if(Directory.Exists(path))ValidateTree(path);else NoReparse(path);}foreach(string name in DataFiles)NoReparse(Path.Combine(root,name));
        long bytes=0;foreach(var item in saved)if(Directory.Exists(item.Value.Folder))bytes=checked(bytes+TreeBytes(item.Value.Folder,null));foreach(string name in DataDirectories)bytes=checked(bytes+TreeBytes(Path.Combine(root,name),name=="logs"?log:null));foreach(string name in DataFiles)bytes=checked(bytes+TreeBytes(Path.Combine(root,name),null));
        var removed=new List<string>();var blocked=new List<string>();
        foreach(var item in saved){if(!Directory.Exists(item.Value.Folder))continue;try{hooks.RemoveReceipt(item.Key);removed.Add(item.Value.Folder);}catch(Exception error){blocked.Add(item.Value.Folder);return new ArcCacheResetResult{Completed=false,BytesFreed=0,RestartRequired=false,RemovedGameFolders=removed.ToArray(),BlockedFolders=blocked.ToArray(),Message="A game folder could not be safely returned to default. Windows was not restored and Arc Cache data was not deleted. "+error.Message};}}
        string restoreMessage="";if(overrideExists)try{restoreMessage=hooks.RestoreOverride();}catch(Exception error){return new ArcCacheResetResult{Completed=false,BytesFreed=0,RestartRequired=false,RemovedGameFolders=removed.ToArray(),BlockedFolders=new string[0],Message="Game copies were returned to default, but Windows could not restore its previous setting. Arc Cache data was kept for recovery. "+error.Message};}
        foreach(string name in DataDirectories)DeleteTree(Path.Combine(root,name),name=="logs"?log:null);foreach(string name in DataFiles)DeleteTree(Path.Combine(root,name),null);
        return new ArcCacheResetResult{Completed=true,BytesFreed=bytes,RestartRequired=restoreMessage.IndexOf("reboot",StringComparison.OrdinalIgnoreCase)>=0,RemovedGameFolders=removed.ToArray(),BlockedFolders=new string[0],Message="Arc Cache reset completed."+(String.IsNullOrWhiteSpace(restoreMessage)?"":" "+restoreMessage)};
    }
}
}
