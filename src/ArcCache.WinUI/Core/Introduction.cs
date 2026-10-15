namespace ArcCache.Core;
public sealed record IntroductionScreen(string Title,string Body);
public sealed class Introduction {
 readonly string path;
 public Introduction(string root){path=Path.Combine(root,"introduction-version.txt");}
 public static readonly IntroductionScreen[] Screens=[
  new("Set up a game","Games save graphics they have already prepared in a shader cache. Arc Cache can give a game its own copy of Intel's graphics files and set that game's cache limit. Your installed driver files stay as they are.")];
 public bool ShouldShow {get{try{return !File.Exists(path)||new FileInfo(path).Length>16||File.ReadAllText(path).Trim()!="2";}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}}}
 public void Dismiss(){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"2");}
}
