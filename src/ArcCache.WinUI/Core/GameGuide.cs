namespace ArcCache.Core;
public sealed record GameGuide(string Title,string Body,string Action,string Button){
 public static GameGuide From(GameSetupState? state,bool detected,bool routing,bool compatible,bool trialAvailable){
  if(state?.FolderExists==true&&!state.Configured)return new("This setup needs attention","Use the saved setup record to recover it before creating another copy.","Recover","Open recovery details");
  if(state?.Configured==true&&!compatible)return new("Start fresh with the tested driver","This game still has an older setup. Restore its default settings first; your shader cache stays as it is.","Restore","Use default settings");
  if(!compatible&&!trialAvailable)return new("First, check your graphics driver","Open Graphics and run the check. If a separate test passes, you can come back here to set up a game.","Graphics","Open Graphics");
  if(!routing)return new("One-time Windows setup","Enable game-specific settings in Settings, then restart Windows. Arc Cache will save your progress.","Settings","Open Settings");
  if(!detected)return new("Step 1 · Start the game","Choose DirectX 12 in the game's launch options. Leave it at the menu with Arc Cache open; the app will find its files automatically.","Launch","Start game through Steam");
  if(state?.Configured!=true)return new("Step 2 · Set up this game","Close the game first. Arc Cache will create its own unchanged driver copy; other games keep their usual settings.","Prepare","Set up this game");
  if(state.Receipt!.MiB==512&&state.Receipt.Status!="original-control-loading-verified")return new("Step 3 · Start the game once","Start in DirectX 12. Keep Arc Cache open: it will check the game's new copy automatically. Close the game after the check finishes.","Launch","Start game and check setup");
  if(state.Receipt.MiB==512)return new("Step 4 · Apply your cache size","The game's unchanged copy loaded successfully. Close the game, then apply the selected size below.","Apply","Apply selected cache size");
  return new(state.Receipt.Trial?"Your game is set up · ready to try":"Your game is set up · ready to play","Start in DirectX 12 through Steam. Use default settings below whenever you want to undo this game's setup. Cache growth and performance improvements have not been measured.","Launch","Play through Steam");
 }
}
