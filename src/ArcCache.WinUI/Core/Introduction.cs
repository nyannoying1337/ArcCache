namespace ArcCache.Core;
public sealed record IntroductionScreen(string Title,string Body);
public sealed class Introduction {
 readonly string path;
 public Introduction(string root){path=Path.Combine(root,"introduction-version.txt");}
 public static readonly IntroductionScreen[] Screens=[
  new("Keep more prepared graphics","Games save prepared shaders so they can reuse work. Arc Cache lets you try larger cache limits for individual games. Benefits vary; a larger cache does not guarantee smoother play."),
  new("Check your Intel graphics","Start on Graphics and choose the chip your game uses. Check support checks the installed driver. Some drivers need a separate unchanged-copy and patched-copy test first. Choosing a chip here does not switch the game to it."),
  new("Choose a game","Pick an installed Steam game. Start it in DirectX 12 so Arc Cache can find its files, then close it before setup. Multiplayer and anti-cheat warnings need your attention; compatibility is not guaranteed."),
  new("Set up, check, choose a size","Arc Cache starts with an unchanged copy for that game. Start the game once with Arc Cache open to check that it loads, then close it and apply a supported size. Windows keeps its installed driver. Enabling game-specific settings is a separate, confirmed Windows change that needs a restart."),
  new("Play normally. Undo any time.","Once set up, play through Steam. Arc Cache does not need to stay open unless you want monitoring. Use default settings on the game's page to undo its setup. Check support again after a driver update. Extra troubleshooting logs are available in Settings.")];
 public bool ShouldShow {get{try{return !File.Exists(path)||new FileInfo(path).Length>16||File.ReadAllText(path).Trim()!="1";}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}}}
 public void Dismiss(){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"1");}
}
