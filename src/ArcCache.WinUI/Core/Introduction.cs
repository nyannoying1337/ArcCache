namespace ArcCache.Core;
public sealed record IntroductionScreen(string Title,string Body,string[] Points,string Note);
public sealed class Introduction {
 // Raise Version when the screens change, so everyone sees the new introduction once.
 const string Version="3";
 readonly string path;
 public Introduction(string root){path=Path.Combine(root,"introduction-version.txt");}
 public static readonly IntroductionScreen[] Screens=[
  new("What Arc Cache does",
   "Games save the shaders they have already compiled in a shader cache, so they don't have to compile them again. Intel's driver limits each game's cache file to 512 MB. When a big game fills it, older shaders are thrown away and the game compiles them again, which can cause stutter.",
   ["Arc Cache gives a game its own copy of Intel's graphics files, in a folder next to the game's .exe file",
    "Only that copy gets a larger cache limit, from 1 GB up to 16 GB",
    "The cache file only grows as the game needs it; choosing 16 GB does not reserve 16 GB of disk space"],
   "Your installed driver files and other games stay exactly as they are."),
  new("The Windows setting: game-specific settings",
   "Windows normally ignores graphics files placed next to a game. A Windows setting called DevOverrideEnable tells Windows to load them from a folder named after the program, such as Game.exe.local. Arc Cache calls this game-specific settings.",
   ["It applies to the whole PC, but only programs that have such a folder are affected. Normally none do, so nothing else changes",
    "It does not replace your installed driver and does not turn off Windows security or driver signature checks",
    "Turning it on or off needs administrator rights (Arc Cache asks Windows for approval) and a restart",
    "Arc Cache saves the previous value. You can turn it off in Settings after every game is back on default settings"],
   "You only need to turn it on once."),
  new("Set up your first game",
   "Arc Cache walks you through each step and asks before it changes anything.",
   ["1  Check whether your Intel driver is supported (Graphics)",
    "2  Turn on game-specific settings and restart your PC",
    "3  Start the game once so Arc Cache can find it, then close it",
    "4  Set up the driver copy, then start the game again for a check",
    "5  Close the game and choose a cache size"],
   "You can return any game to its usual driver at any time with Use default settings.")];
 public bool ShouldShow {get{try{return !File.Exists(path)||new FileInfo(path).Length>16||File.ReadAllText(path).Trim()!=Version;}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}}}
 public void Dismiss(){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,Version);}
}
