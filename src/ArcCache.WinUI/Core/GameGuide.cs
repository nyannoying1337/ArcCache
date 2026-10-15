namespace ArcCache.Core;
public sealed record GameGuide(string Title,string Body,string Action,string Button){
 public static GameGuide From(GameSetupState? state,bool detected,bool routing,bool compatible,bool trialAvailable){
  if(state?.FolderExists==true&&!state.Configured)return new("This setup needs attention","Open recovery details and import the saved setup record before setting up this game again.","Recover","Open recovery details");
  if(state?.Configured==true&&!compatible)return new("Update this game’s setup","This game has older graphics files. Choose Use default settings, then set it up again. Your shader cache stays as it is.","Restore","Use default settings");
  if(!compatible&&!trialAvailable)return new("Check graphics support first","Open Graphics and check your Intel driver. If a separate test is available, Arc Cache will guide you through it.","Graphics","Open Graphics");
  if(!routing)return new("Allow game-specific graphics files","Open Graphics to allow this one-time Windows setting. Arc Cache will explain the change; restart Windows if asked. Arc Cache will keep your place.","Graphics","Open Graphics");
  if(!detected)return new("Start the game","If the game offers DirectX 12, choose it. Leave the game at the main menu and keep Arc Cache open so it can find the game files.","Launch","Start through your launcher");
  if(state?.Configured!=true)return new("Set up this game","Close the game first. Arc Cache will make a separate set of graphics files for this game. Your normal driver files stay unchanged.","Prepare","Set up this game");
  if(state.Receipt!.MiB==512&&state.Receipt.Status!="original-control-loading-verified")return new("Check the game setup","Start the game in DirectX 12 and leave Arc Cache open. It will check that the game can load its separate graphics files. Close the game when the check finishes.","Launch","Start game and check copy");
  if(state.Receipt.MiB==512)return new("Choose a cache size","The game loaded its copy. Close the game, choose a size below, and apply it.","Apply","Apply cache size");
  return new(state.Receipt.Trial?"Your game is set up · ready to try":"Your game is set up · ready to play","Start in DirectX 12 through your launcher. Use default settings below whenever you want to undo this game's setup. Cache growth and performance improvements have not been measured.","Launch","Play through your launcher");
 }
}
