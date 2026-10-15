using System.Diagnostics;
namespace ArcCache.Core;
public sealed record GameLaunchAction(string Caption,string Target,bool Protocol=false,bool Direct=false,bool GameExecutable=false);
public static class GameLaunch {
    static bool Token(string value)=>System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z0-9_-]{1,256}\z");
    public static GameLaunchAction For(SteamGame game){
        _=game.Key;
        if(game.Source=="Steam")return new("Play through Steam","steam://rungameid/"+game.AppId,true);
        if(game.Source=="Epic"){
            var ids=(game.NativeLaunchId??"").Split(':');
            if(ids.Length==3&&ids.All(Token))return new("Play through Epic","com.epicgames.launcher://apps/"+string.Join("%3A",ids.Select(Uri.EscapeDataString))+"?action=launch&silent=true",true);
            return new("Open Epic library","com.epicgames.launcher://store/library",true);
        }
        if(game.Source=="Ubisoft"&&ulong.TryParse(game.ExternalId,out _))return new("Play through Ubisoft","uplay://launch/"+game.ExternalId,true);
        if(game.Source=="GOG"&&ulong.TryParse(game.ExternalId,out _))return new("Open in GOG Galaxy","goggalaxy://openGameView/"+game.ExternalId,true);
        if(game.Source=="Manual"&&game.ManualExecutable!=null){if(!Path.GetDirectoryName(Path.GetFullPath(game.ManualExecutable))!.Equals(Path.GetFullPath(game.InstallDirectory),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Manual launch escaped its installation.");return new("Start game",game.ManualExecutable,Direct:true,GameExecutable:true);}
        string programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),native=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates=game.Source switch{
            "Battle.net"=>[Path.Combine(programFiles,"Battle.net","Battle.net Launcher.exe"),Path.Combine(programFiles,"Battle.net","Battle.net.exe")],
            "EA"=>[Path.Combine(native,"Electronic Arts","EA Desktop","EA Desktop","EADesktop.exe")],
            "Ubisoft"=>[Path.Combine(programFiles,"Ubisoft","Ubisoft Game Launcher","UbisoftConnect.exe")],
            "Riot"=>[Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!,"Riot Games","Riot Client","RiotClientServices.exe")],
            _=>[]};
        string? client=candidates.FirstOrDefault(File.Exists);if(client!=null)return new("Open "+game.Source,client,Direct:true);
        return new("Open game folder",game.InstallDirectory);
    }
    // Called only by the explicit user launch action. Manifest commands/arguments are ignored.
    public static void Start(GameLaunchAction action){
        if(action.GameExecutable)ManualGameStore.ValidateExecutable(action.Target);
        if(action.Protocol)Process.Start(new ProcessStartInfo(action.Target){UseShellExecute=true});
        else if(action.Direct)Process.Start(new ProcessStartInfo(action.Target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(action.Target)!});
        else {var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add(action.Target);Process.Start(start);}
    }
}
