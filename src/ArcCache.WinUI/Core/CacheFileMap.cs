using System.Text.Json;

namespace ArcCache.Core;

/// <summary>Which Intel shader cache file belongs to which game. Intel's D3D12 driver keeps one file per exe name in
/// %USERPROFILE%\AppData\LocalLow\Intel\ShaderCache, named by a 64-hex hash that can't be derived from the name. Arc Cache
/// learns the link when it watches a compile (SCSKiller warms exactly one exe name at a time): the file that clearly grew
/// the most is that game's. Read only: Arc Cache never writes or deletes these files.</summary>
public sealed record CacheFileRecord(string ExeName,string File,long Bytes,string Source,DateTime ObservedUtc);

public sealed class CacheFileMap(string path) {
    public static string IntelRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Intel","ShaderCache");
    public static bool IsCacheName(string name)=>name.Length==64&&name.All(Uri.IsHexDigit);

    public static Dictionary<string,long> Snapshot(string? root=null) {
        var sizes=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        try {
            var dir=new DirectoryInfo(root??IntelRoot);
            if(!dir.Exists||dir.Attributes.HasFlag(FileAttributes.ReparsePoint))return sizes;
            foreach(var file in dir.EnumerateFiles())
                if(!file.Attributes.HasFlag(FileAttributes.ReparsePoint)&&IsCacheName(file.Name))sizes[file.Name]=file.Length;
        }catch(IOException){}catch(UnauthorizedAccessException){}
        return sizes;
    }

    /// <summary>The file that grew the most between two snapshots, when it clearly stands out: at least 1 MiB and at least
    /// four times the growth of any other file. Null when nothing grew or two files grew alike.</summary>
    public static string? Attribute(IReadOnlyDictionary<string,long> before,IReadOnlyDictionary<string,long> after) {
        var growth=after.Select(p=>(Name:p.Key,Bytes:p.Value-(before.TryGetValue(p.Key,out long old)?old:0))).Where(g=>g.Bytes>0).OrderByDescending(g=>g.Bytes).ToList();
        if(growth.Count==0||growth[0].Bytes<(1L<<20))return null;
        if(growth.Count>1&&growth[1].Bytes*4>growth[0].Bytes)return null;
        return growth[0].Name;
    }

    static string Key(string exePath)=>Path.GetFileName(exePath).ToLowerInvariant();

    Dictionary<string,CacheFileRecord> Read() {
        try{if(File.Exists(path)&&JsonSerializer.Deserialize<Dictionary<string,CacheFileRecord>>(File.ReadAllText(path)) is {} all)return new(all,StringComparer.OrdinalIgnoreCase);}
        catch(JsonException){}catch(IOException){}
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public CacheFileRecord? For(string exePath)=>Read().TryGetValue(Key(exePath),out var record)&&IsCacheName(record.File)?record:null;

    public CacheFileRecord Remember(string exePath,string file,long bytes,string source) {
        if(!IsCacheName(file))throw new ArgumentException("Not an Intel shader cache file name.",nameof(file));
        var all=Read();var record=new CacheFileRecord(Path.GetFileName(exePath),file,bytes,source,DateTime.UtcNow);all[Key(exePath)]=record;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(all,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);
        return record;
    }

    /// <summary>The file's current size, or null when it no longer exists.</summary>
    public static long? CurrentBytes(string file,string? root=null) {
        if(!IsCacheName(file))return null;
        try{var info=new FileInfo(Path.Combine(root??IntelRoot,file));return info.Exists?info.Length:null;}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    }
}
