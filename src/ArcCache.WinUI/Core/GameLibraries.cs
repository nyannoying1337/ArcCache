using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace ArcCache.Core;

public sealed record InstalledEntry(string Source,string Id,string Name,string Directory,string? Icon=null,string? LaunchId=null);
public sealed record LibraryLocations(string EpicManifests,string LegendaryInstalled,string HeroicInstalled,string BattleNetDatabase,IReadOnlyList<string> XboxRoots);
public static class GameLibraries {
    const int MaxMetadata=4*1024*1024,MaxEntries=10000;
    public static string ReadMetadata(string path){using var input=File.OpenRead(path);if(input.Length>MaxMetadata)throw new InvalidDataException("Installation metadata exceeds its size limit.");using var reader=new StreamReader(input);char[] buffer=new char[MaxMetadata+1];int used=0,n;while(used<buffer.Length&&(n=reader.Read(buffer,used,buffer.Length-used))>0)used+=n;if(used>MaxMetadata)throw new InvalidDataException("Installation metadata exceeds its size limit.");return new string(buffer,0,used);}
    static string? S(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    public static SteamGame FromEntry(InstalledEntry entry){
        if(string.IsNullOrWhiteSpace(entry.Name)||entry.Name.Length>512||string.IsNullOrWhiteSpace(entry.Id)||!Path.IsPathRooted(entry.Directory))throw new InvalidDataException("Incomplete game installation record.");
        string root=Path.GetFullPath(Environment.ExpandEnvironmentVariables(entry.Directory)).TrimEnd(Path.DirectorySeparatorChar);
        if(root.StartsWith(@"\\",StringComparison.Ordinal))throw new InvalidDataException("Automatic discovery supports local game folders only.");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("Game installation is missing.");
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked installation roots cannot be verified by this build.");
        var game=new SteamGame(0,entry.Name,root,LocalArtwork(root),Icon:entry.Icon,Source:entry.Source,ExternalId:entry.Id,NativeLaunchId:entry.LaunchId);_=game.Key;return game;
    }
    static string? LocalArtwork(string root){foreach(string name in new[]{"cover.png","cover.jpg","folder.jpg","icon.png"}){string path=Path.Combine(root,name);if(File.Exists(path))return path;}return null;}
    public static IEnumerable<InstalledEntry> ParseEpic(string text){
        using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});var e=doc.RootElement;if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid Epic installation record.");
        if(e.TryGetProperty("bIsIncompleteInstall",out var incomplete)&&incomplete.ValueKind==JsonValueKind.True)yield break;
        string? app=S(e,"AppName"),main=S(e,"MainGameAppName");if(app==null||main!=null&&main.Length>0&&main!=app)yield break;
        if(e.TryGetProperty("AppCategories",out var categories)&&categories.ValueKind==JsonValueKind.Array&&!categories.EnumerateArray().Any(x=>x.ValueKind==JsonValueKind.String&&x.GetString()=="games"))yield break;
        if(S(e,"InstallLocation") is string root&&S(e,"DisplayName") is string name)yield return new("Epic",app,name,root,LaunchId:S(e,"CatalogNamespace")+":"+S(e,"CatalogItemId")+":"+app);
    }
    public static IEnumerable<InstalledEntry> ParseLegendary(string text){
        using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=32});if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid Legendary installation index.");
        foreach(var app in doc.RootElement.EnumerateObject().Take(MaxEntries)){
            var e=app.Value;if(e.ValueKind!=JsonValueKind.Object)continue;if(e.TryGetProperty("is_dlc",out var dlc)&&dlc.ValueKind==JsonValueKind.True)continue;
            if(S(e,"install_path") is string path&&S(e,"title") is string title)yield return new("Legendary",app.Name,title,path);
        }
    }
    // Registry records are data only: uninstall commands are never executed.
    public static InstalledEntry? ClassifyUninstall(string key,string name,string? publisher,string? location,string? uninstall){
        if(string.IsNullOrWhiteSpace(location)||!Path.IsPathRooted(location)||location.StartsWith(@"\\",StringComparison.Ordinal)||!Directory.Exists(location)||string.IsNullOrWhiteSpace(name)||Regex.IsMatch(name,@"^(?:Battle\.net|Ubisoft Connect|Uplay|EA app|EA Desktop|Origin|GOG GALAXY|Riot Client|Amazon Games|Rockstar Games Launcher|Rockstar Games Social Club)(?:\s|$)",RegexOptions.IgnoreCase))return null;
        var bnet=Regex.Match(uninstall??"",@"Battle\.net.*?--uid=[""']?([A-Za-z0-9_]+)(?:[""'\s]|$)",RegexOptions.IgnoreCase);
        if(bnet.Success)return new("Battle.net",bnet.Groups[1].Value,name,location);
        string p=publisher??"";
        if(p.Contains("Blizzard",StringComparison.OrdinalIgnoreCase)&&File.Exists(Path.Combine(location,".build.info")))return new("Battle.net",key,name,location);
        if(p.Contains("GOG",StringComparison.OrdinalIgnoreCase)||key.StartsWith("GOG",StringComparison.OrdinalIgnoreCase))return new("GOG",key,name,location);
        if(p.Contains("Electronic Arts",StringComparison.OrdinalIgnoreCase))return new("EA",key,name,location);
        if(p.Contains("Ubisoft",StringComparison.OrdinalIgnoreCase))return new("Ubisoft",key,name,location);
        if(p.Contains("Rockstar Games",StringComparison.OrdinalIgnoreCase))return new("Rockstar",key,name,location);
        if(p.Contains("Riot Games",StringComparison.OrdinalIgnoreCase))return new("Riot",key,name,location);
        if(p.Contains("Amazon Games",StringComparison.OrdinalIgnoreCase))return new("Amazon",key,name,location);
        return null;
    }
    static IEnumerable<InstalledEntry> RegistryEntries(List<string> warnings){
        var entries=new List<InstalledEntry>();
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{using var registry=RegistryKey.OpenBaseKey(hive,view);
            using(var uninstall=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))if(uninstall!=null)foreach(string key in uninstall.GetSubKeyNames().Take(MaxEntries)){
                using var item=uninstall.OpenSubKey(key);if(item==null)continue;var entry=ClassifyUninstall(key,item.GetValue("DisplayName") as string??"",item.GetValue("Publisher") as string,item.GetValue("InstallLocation") as string,item.GetValue("UninstallString") as string);if(entry!=null)entries.Add(entry);
            }
            foreach(var spec in new[]{(Path:@"SOFTWARE\GOG.com\Games",Source:"GOG",Location:"path",Name:"gameName"),(Path:@"SOFTWARE\Ubisoft\Launcher\Installs",Source:"Ubisoft",Location:"InstallDir",Name:"DisplayName"),(Path:@"SOFTWARE\EA Games",Source:"EA",Location:"Install Dir",Name:"DisplayName")}){
                using var index=registry.OpenSubKey(spec.Path);if(index==null)continue;foreach(string id in index.GetSubKeyNames().Take(MaxEntries)){
                    using var item=index.OpenSubKey(id);string? path=item?.GetValue(spec.Location) as string;if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||!Directory.Exists(path))continue;
                    string? label=item?.GetValue(spec.Name) as string;string name=string.IsNullOrWhiteSpace(label)?Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)):label;
                    entries.Add(new(spec.Source,id,name,path));
                }
            }
            }catch(Exception error)when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException){warnings.Add("Registered library records were only partly readable ("+hive+", "+view+").");}
        }return entries;
    }
    public static IEnumerable<InstalledEntry> ParseXbox(string text,string contentRoot){
        var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxMetadata};using var input=XmlReader.Create(new StringReader(text),settings);var xml=XDocument.Load(input);
        var identity=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="Identity");string? id=identity?.Attribute("Name")?.Value;if(id==null)yield break;
        string? name=xml.Descendants().FirstOrDefault(x=>x.Name.LocalName=="ShellVisuals")?.Attribute("DefaultDisplayName")?.Value;
        if(string.IsNullOrWhiteSpace(name)||name.StartsWith("ms-resource:",StringComparison.OrdinalIgnoreCase))name=Path.GetFileName(Path.GetDirectoryName(contentRoot));
        if(!xml.Descendants().Any(x=>x.Name.LocalName=="Executable"))yield break;
        yield return new("Xbox",id,name??id,contentRoot);
    }
    public static LibraryLocations DefaultLocations(){
        string pd=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var xbox=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady).Select(d=>Path.Combine(d.RootDirectory.FullName,"XboxGames")).ToArray();
        return new(Path.Combine(pd,"Epic","EpicGamesLauncher","Data","Manifests"),Path.Combine(user,".config","legendary","installed.json"),Path.Combine(roaming,"heroic","legendaryConfig","legendary","installed.json"),Path.Combine(pd,"Battle.net","Agent","product.db"),xbox);
    }
    public static LibraryResult Discover(string? steamRoot,string appRoot,LibraryLocations? locations=null,IEnumerable<InstalledEntry>? registered=null){
        locations??=DefaultLocations();var steam=SteamLibrary.Discover(steamRoot);var warnings=new List<string>(steam.Warnings);var games=new List<SteamGame>(steam.Games);
        void Add(InstalledEntry entry){try{games.Add(FromEntry(entry));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException){warnings.Add(entry.Source+": "+e.Message);}}
        void Read(string file,Func<string,IEnumerable<InstalledEntry>> parser,string source){try{if(File.Exists(file))foreach(var item in parser(ReadMetadata(file)))Add(item);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or XmlException or ArgumentException or InvalidDataException){warnings.Add(source+" installation record could not be read: "+Path.GetFileName(file)+". "+e.Message);}}
        try{if(Directory.Exists(locations.EpicManifests))foreach(string file in Directory.EnumerateFiles(locations.EpicManifests,"*.item").Take(MaxEntries))Read(file,ParseEpic,"Epic");}catch(Exception e)when(e is IOException or UnauthorizedAccessException){warnings.Add("Epic discovery: "+e.Message);}
        Read(locations.LegendaryInstalled,ParseLegendary,"Legendary");Read(locations.HeroicInstalled,ParseLegendary,"Heroic / Legendary");
        try{foreach(var entry in registered??RegistryEntries(warnings))Add(entry);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){warnings.Add("Registered game discovery: "+e.Message);}
        ReadBattleNet(locations.BattleNetDatabase,Add,warnings);
        foreach(string xbox in locations.XboxRoots)try{if(Directory.Exists(xbox))foreach(string game in Directory.EnumerateDirectories(xbox).Take(MaxEntries)){string content=Path.Combine(game,"Content");Read(Path.Combine(content,"MicrosoftGame.config"),text=>ParseXbox(text,content),"Xbox");}}catch(Exception e)when(e is IOException or UnauthorizedAccessException){warnings.Add("Xbox folder discovery: "+e.Message);}
        try{foreach(var game in new ManualGameStore(appRoot).Read())games.Add(game);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){warnings.Add("Manual games: "+e.Message);}
        var unique=games.GroupBy(g=>g.Key,StringComparer.Ordinal).Select(g=>g.First()).GroupBy(g=>g.Source=="Manual"?g.Key:g.Source+"|"+Path.GetFullPath(g.InstallDirectory).TrimEnd('\\'),StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderByDescending(x=>ulong.TryParse(x.ExternalId,out _)).First()).OrderBy(g=>g.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        if(unique.Length>0)warnings.RemoveAll(w=>w.StartsWith("Steam was not found.",StringComparison.Ordinal));
        return new(unique,warnings,steam.SteamRoot);
    }
    static void ReadBattleNet(string file,Action<InstalledEntry> add,List<string> warnings){
        // Parsed below using only the known install-record fields, never executable content.
        try{if(File.Exists(file)){using var input=File.OpenRead(file);if(input.Length>MaxMetadata)throw new InvalidDataException("Battle.net database exceeds limit.");var bytes=new byte[(int)input.Length];input.ReadExactly(bytes);foreach(var entry in BattleNetRecords.Parse(bytes))add(entry);}}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException){warnings.Add("Battle.net installation database could not be read: "+e.Message);}
    }
}
public sealed class ManualGameStore(string root){
    string FilePath=>Path.Combine(root,"manual-games.json");
    public IReadOnlyList<SteamGame> Read(){if(!File.Exists(FilePath))return [];var records=JsonSerializer.Deserialize<List<SteamGame>>(GameLibraries.ReadMetadata(FilePath))??[];if(records.Count>1000)throw new InvalidDataException("Too many manual games.");foreach(var game in records){if(game.Source!="Manual"||game.AppId!=0||game.ManualExecutable==null||game.ExternalId!=Path.GetFullPath(game.ManualExecutable).ToUpperInvariant())throw new InvalidDataException("Invalid manual game record.");if(File.Exists(game.ManualExecutable))ValidateExecutable(game.ManualExecutable);if(!Path.GetDirectoryName(Path.GetFullPath(game.ManualExecutable))!.Equals(Path.GetFullPath(game.InstallDirectory),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Manual executable directory changed.");_=game.Key;}return records;}
    public static void ValidateExecutable(string executable){if(!Path.IsPathRooted(executable)||executable.StartsWith(@"\\",StringComparison.Ordinal)||!Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a local game .exe.");using var read=new BinaryReader(File.OpenRead(executable));if(read.BaseStream.Length<256||read.ReadUInt16()!=0x5a4d)throw new InvalidDataException("Not a Windows game executable.");read.BaseStream.Position=60;uint nt=read.ReadUInt32();if(nt>read.BaseStream.Length-26)throw new InvalidDataException("Invalid executable header.");read.BaseStream.Position=nt;if(read.ReadUInt32()!=0x4550||read.ReadUInt16()!=0x8664)throw new InvalidDataException("Only 64-bit Windows games are supported.");read.BaseStream.Position=nt+22;ushort characteristics=read.ReadUInt16();if((characteristics&2)==0||(characteristics&0x2000)!=0||read.ReadUInt16()!=0x20b)throw new InvalidDataException("Choose a 64-bit game executable, not a DLL.");}
    public SteamGame Add(string executable,string? title=null){ValidateExecutable(executable);string exe=Path.GetFullPath(executable),folder=Path.GetDirectoryName(exe)!;var game=new SteamGame(0,string.IsNullOrWhiteSpace(title)?Path.GetFileNameWithoutExtension(exe):title.Trim(),folder,null,Source:"Manual",ExternalId:exe.ToUpperInvariant(),ManualExecutable:exe);_=game.Key;var games=Read().Where(g=>g.Key!=game.Key).Append(game).ToArray();Save(games);return game;}
    public void Remove(string key){Save(Read().Where(g=>g.Key!=key).ToArray());}
    void Save(IReadOnlyList<SteamGame> games){Directory.CreateDirectory(root);string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".pending";try{File.WriteAllText(temp,JsonSerializer.Serialize(games));File.Move(temp,FilePath,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
