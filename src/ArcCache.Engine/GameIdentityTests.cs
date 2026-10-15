using System;using System.IO;using ArcCap;
class GameIdentityTests{
 static int passed;static void Check(bool yes,string name){if(!yes)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}static void Reject(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Expected refusal: "+name);}
 static int Main(){string root=Path.Combine(Path.GetTempPath(),"ArcIdentity-"+Guid.NewGuid().ToString("N"));
 var epic=new MatchedSet.ProfileInfo{Schema=1,AppId=0,Source="Epic",ExternalId="app",Root=root,RelativeExecutable="Game.exe",Hash=new string('A',64),ObservedDriverHash=Engine.Original};
 Check(MatchedSet.ProfileExecutable(epic)==Path.Combine(root,"Game.exe"),"Non-Steam observed profile accepts a bounded executable");
 string key=MatchedSet.ProfileKey(epic);Check(key.StartsWith("epic-")&&Path.GetFileName(MatchedSet.ProfilePath(key))==key+".json","Non-Steam profile has its own file namespace");
 epic.RelativeExecutable=@"..\Escape.exe";Reject(()=>MatchedSet.ProfileExecutable(epic),"Profile cannot escape installation");epic.RelativeExecutable="Game.exe";
 epic.Source="Steam";Reject(()=>MatchedSet.ProfileExecutable(epic),"Zero Steam ID cannot inherit non-Steam eligibility");
 epic.Source="Epic";epic.AppId=292030;Reject(()=>MatchedSet.ProfileExecutable(epic),"Non-Steam profile cannot impersonate Steam identity");
 epic.Source=null;Check(MatchedSet.ProfileKey(epic)=="steam-292030"&&MatchedSet.ProfileExecutable(epic)==Path.Combine(root,"Game.exe"),"Existing Steam profile remains readable without new fields");
 Reject(()=>MatchedSet.ProfilePath("../escape"),"Receipt key traversal refused");Console.WriteLine("Passed "+passed+" backend profile checks; no profiles or game files written.");return 0;}
}
