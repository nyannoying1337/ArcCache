using ArcCache.Core;
using ArcCap;
using System.Text;
using System.Text.Json;
public static class GameLibraryTests {
 public static void Run(Action<string,Action> test,Action<bool,string> check){
  string root=Path.Combine(Path.GetTempPath(),"ArcLibraries-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   string game=Path.Combine(root,"Game");Directory.CreateDirectory(game);
   test("Library identities preserve Steam receipts and separate non-Steam stores and installs",()=>{
    check(GameIdentity.Key(292030,"Steam",null!,null!)=="steam-292030","Steam migration changed identity.");
    string epic=GameIdentity.Key(0,"Epic","same",game),gog=GameIdentity.Key(0,"GOG","same",game);check(epic!=gog&&GameIdentity.Key(0,"Epic","same",game.ToUpperInvariant())==epic,"Store collision or path casing changed identity.");
    var store=new AppStore(Path.Combine(root,"app"));check(store.Receipt(292030)==store.Receipt("steam-292030"),"Legacy receipt path changed.");
    Reject(()=>GameIdentity.Key(1,"Epic","same",game));Reject(()=>GameIdentity.Key(0,"Unknown","same",game));Reject(()=>GameIdentity.Key(0,"Manual","exe",Path.GetPathRoot(game)!));
    check(!GameIdentity.ValidKey("../receipt")&&!GameIdentity.ValidKey("steam-0"),"Unsafe profile key accepted.");
   });
   test("Epic manifests exclude incomplete installs and DLC; launcher IDs cannot inject commands",()=>{
    string manifest=JsonSerializer.Serialize(new{AppName="app",MainGameAppName="app",DisplayName="Example",InstallLocation=game,CatalogNamespace="namespace",CatalogItemId="catalog",AppCategories=new[]{"games"},LaunchCommand="cmd.exe /c bad"});
    var entry=GameLibraries.ParseEpic(manifest).Single();var found=GameLibraries.FromEntry(entry);var action=GameLaunch.For(found);check(action.Target=="com.epicgames.launcher://apps/namespace%3Acatalog%3Aapp?action=launch&silent=true"&&!action.Target.Contains("cmd.exe"),"Manifest command was trusted or launch ID is wrong.");
    check(!GameLibraries.ParseEpic(manifest.Replace("\"MainGameAppName\":\"app\"","\"MainGameAppName\":\"parent\"")).Any(),"DLC imported as base game.");
    check(!GameLibraries.ParseEpic(manifest[..^1]+",\"bIsIncompleteInstall\":true}").Any(),"Incomplete install imported.");
    check(GameLaunch.For(found with{NativeLaunchId="bad:catalog;cmd:app"}).Target=="com.epicgames.launcher://store/library","Unsafe launcher token accepted.");
   });
   test("Legendary indexes import installed titles and skip DLC",()=>{
    var text=JsonSerializer.Serialize(new Dictionary<string,object>{{"base",new{title="Base",install_path=game}},{"dlc",new{title="DLC",install_path=game,is_dlc=true}}});check(GameLibraries.ParseLegendary(text).Single().Id=="base","Legendary DLC filtering failed.");
   });
   test("Battle.net registry discovery ignores launcher entries and never uses uninstall text as a launch command",()=>{
    var entry=GameLibraries.ClassifyUninstall("id","Example",null,game,"\"C:\\Battle.net\\Battle.net.exe\" --uid=wow --uninstall");check(entry?.Source=="Battle.net"&&entry.Id=="wow","Battle.net install ID was not parsed.");
    check(GameLibraries.ClassifyUninstall("client","Battle.net",null,game,"Battle.net --uid=battle.net")==null,"Launcher imported as game.");
   });
   test("Registered store discovery classifies game entries without importing client programs",()=>{
    foreach(var item in new[]{(Publisher:"GOG.com",Source:"GOG"),(Publisher:"Electronic Arts",Source:"EA"),(Publisher:"Ubisoft",Source:"Ubisoft"),(Publisher:"Riot Games",Source:"Riot"),(Publisher:"Amazon Games",Source:"Amazon"),(Publisher:"Rockstar Games",Source:"Rockstar")}){
      check(GameLibraries.ClassifyUninstall("id","Game",item.Publisher,game,null)?.Source==item.Source,"Registered source missing: "+item.Source);
    }
    check(GameLibraries.ClassifyUninstall("id","Ubisoft Connect","Ubisoft",game,null)==null,"Launcher client imported as game.");
   });
   test("Battle.net database reads known protobuf fields and rejects corrupt wire data",()=>{
    File.WriteAllText(Path.Combine(game,".build.info"),"fixture");var install=Field(1,Encoding.UTF8.GetBytes(game));var product=Field(1,Encoding.UTF8.GetBytes("wow")).Concat(Field(3,install)).ToArray();var bytes=Field(1,product);
    check(BattleNetRecords.Parse(bytes).Single().Directory==game,"Battle.net product install path missing.");
    Reject(()=>BattleNetRecords.Parse(bytes[..^1]));Reject(()=>BattleNetRecords.Parse([0]));Reject(()=>BattleNetRecords.Parse([0x0a,0xff,0xff,0xff,0xff,0x7f]));
   });
   test("Xbox config forbids XML entity expansion and discovery alone does not grant patch support",()=>{
    var entry=GameLibraries.ParseXbox("<Game><Identity Name='fixture'/><ShellVisuals DefaultDisplayName='Xbox game'/><ExecutableList><Executable Name='Game.exe'/></ExecutableList></Game>",game).Single();var found=GameLibraries.FromEntry(entry);check(found.Source=="Xbox"&&!found.Supported,"Discovery granted compatibility.");
    try{_=GameLibraries.ParseXbox("<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///C:/Windows/win.ini'>]><Game>&data;</Game>",game).ToArray();throw new Exception("XML entity accepted.");}catch(System.Xml.XmlException){}
   });
   test("Manual games persist independently, keep recovery identity when missing, and do not auto-launch",()=>{
    string exe=Path.Combine(game,"Manual.exe");byte[] pe=new byte[512];pe[0]=0x4d;pe[1]=0x5a;BitConverter.GetBytes(128).CopyTo(pe,60);BitConverter.GetBytes(0x4550).CopyTo(pe,128);BitConverter.GetBytes((ushort)0x8664).CopyTo(pe,132);BitConverter.GetBytes((ushort)2).CopyTo(pe,150);BitConverter.GetBytes((ushort)0x20b).CopyTo(pe,152);File.WriteAllBytes(exe,pe);
    var store=new ManualGameStore(Path.Combine(root,"manual"));var added=store.Add(exe,"My game");check(store.Read().Single().Key==added.Key&&!added.Supported,"Manual persistence or detection boundary failed.");store.Add(exe,"Renamed");check(store.Read().Count==1,"Manual duplicate added.");var launch=GameLaunch.For(added);check(launch.GameExecutable&&!launch.Protocol&&launch.Target==exe,"Manual launch plan is not exact selected executable.");
    File.Delete(exe);check(store.Read().Single().Key==added.Key,"Missing game lost its recovery identity.");store.Remove(added.Key);check(store.Read().Count==0,"Manual removal failed.");
    string bad=Path.Combine(game,"Bad.exe");File.WriteAllText(bad,"not a PE");Reject(()=>store.Add(bad));
   });
   test("Malformed Epic metadata cannot hide other stores and duplicate records are stable",()=>{
    string manifests=Path.Combine(root,"manifests");Directory.CreateDirectory(manifests);File.WriteAllText(Path.Combine(manifests,"bad.item"),"not json");File.WriteAllText(Path.Combine(manifests,"array.item"),"[]");File.WriteAllText(Path.Combine(manifests,"good.item"),JsonSerializer.Serialize(new{AppName="app",DisplayName="Example",InstallLocation=game}));
    var locations=new LibraryLocations(manifests,Path.Combine(root,"missing"),Path.Combine(root,"missing2"),Path.Combine(root,"missingdb"),[]);
    var result=GameLibraries.Discover(Path.Combine(root,"no-steam"),Path.Combine(root,"app-empty"),locations,[new("GOG","1","GOG Game",game),new("GOG","1","GOG Game",game)]);
    check(result.Games.Count==2&&result.Games.Any(g=>g.Source=="Epic")&&result.Games.Any(g=>g.Source=="GOG")&&result.Warnings.Any(w=>w.Contains("bad.item")),"One broken record hid another game or duplicates remained.");
   });
  }finally{Directory.Delete(root,true);}
 }
 static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Expected invalid data refusal.");}
 static byte[] Field(byte tag,byte[] value){var result=new List<byte>{(byte)(tag*8+2)};uint n=(uint)value.Length;while(n>=128){result.Add((byte)(n|128));n>>=7;}result.Add((byte)n);result.AddRange(value);return result.ToArray();}
}
