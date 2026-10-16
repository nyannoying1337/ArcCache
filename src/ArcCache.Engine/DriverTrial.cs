using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Web.Script.Serialization;
namespace ArcCap {
public sealed class DriverTrialInspection {
 public bool Eligible;
 public string File,Driver,Hash,Version,Signature,Message;
 public int CandidateCount;
 public Dictionary<string,bool> Buildable;
 public Dictionary<string,string> Reasons;
}
public sealed class DriverTrialCleanupPlan { public string[] Folders; public long BytesToFree; public int SkippedUnsafe; }
internal sealed class DriverTrialCleanupRecord { public string Receipt,Identity,AdapterId,ProbeHash,Driver,CheckedUtc; public int MiB; public bool Passed,Referenced; }
public static class DriverTrial {
 static void Require(bool yes,string message){if(!yes)throw new InvalidDataException(message);}
 public static bool HasObservedCacheRead(string output){var m=System.Text.RegularExpressions.Regex.Match(output??"",@"(?im)^CacheReadBytes:\s*(\d+)\s*$");ulong bytes;return m.Success&&UInt64.TryParse(m.Groups[1].Value,out bytes)&&bytes>0;}
 // Every distinct cache file the write run changed, joined with '|' for the reload run. Null when none.
 public static string ObservedCacheWrites(string output){var paths=System.Text.RegularExpressions.Regex.Matches(output??"",@"(?im)^CacheFileWritten:\s*(.+?)\s*$").Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value.Trim()).Where(p=>p.Length>0&&p.IndexOf('|')<0).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();return paths.Length==0?null:String.Join("|",paths);}
 public static string SingleObservedCacheWrite(string output){var paths=System.Text.RegularExpressions.Regex.Matches(output??"",@"(?im)^CacheFileWritten:\s*(.+?)\s*$").Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return paths.Length==1?paths[0]:null;}
 internal static bool IgnorableReceiptFailure(Exception error){return error is IOException||error is InvalidDataException||error is InvalidOperationException||error is ArgumentException||error is KeyNotFoundException;}
 internal static bool RequiresChangeJournal(int beforeMiB,int afterMiB){return beforeMiB>=4096||afterMiB>=4096;}
 public static Action<int,string> Progress;
 static void Report(int value,string text){if(Progress!=null)Progress(value,text);}
 // Pattern-based output is an unreviewed test artifact, never a supported game definition.
 public static bool SupportedName(string name){return name=="igd12um64xe2.dll"||name=="igd12um64xe3.dll"||name==Xe1Patch.FileName;}
 public static byte[] Patch(byte[] original,int mib,string driverName=null) {
  Require(original.Length>=256&&original.Length<=128*1024*1024,"Driver file size is outside test bounds.");
  if(mib==512)return (byte[])original.Clone();
  string name=driverName??"igd12um64xe2.dll";var plan=GenericCachePatch.Discover(original,name,mib);Require(plan.CanBuild,plan.Reason);
  byte[] output=GenericCachePatch.Build(original,mib,name);
  if(mib==1024){string hash=Engine.Hash(original);if(hash==Engine.Original)Require(Engine.Hash(output)==Engine.OneGiB,"Known Xe2 output differs.");if(hash==DriverCatalog.Xe3Original)Require(Engine.Hash(output)=="F6F9ABC40702C5EBC29D848E416510C7B0D5D5D61021B3C8D74726F02E81B9F9","Known Xe3 output differs.");}
  return output;
 }
 public static DriverTrialInspection Inspect(string file){
  string full=Path.GetFullPath(file);Engine.CheckPath(full);string name=Path.GetFileName(full);
  Require(SupportedName(name),"Select an Xe1, Xe2 or Xe3 Intel driver file from the active package.");
  var package=DriverCatalog.Selected();Require(package!=null&&package.Folder.Equals(Path.GetDirectoryName(full),StringComparison.OrdinalIgnoreCase),"This file must belong to the selected active Intel graphics package.");
  var review=DriverCatalog.Review(full,true);bool eligible=review.CandidateCount==1&&review.Signature!=null&&review.Signature.StartsWith("Valid");
  var buildable=new Dictionary<string,bool>(StringComparer.Ordinal);var reasons=new Dictionary<string,string>(StringComparer.Ordinal);byte[] image=File.ReadAllBytes(full);
  foreach(int mib in new[]{512,1024,2048,4096,8192,16384}){string key=mib.ToString(System.Globalization.CultureInfo.InvariantCulture);if(mib==512){buildable[key]=eligible;continue;}var plan=GenericCachePatch.Discover(image,name,mib);buildable[key]=eligible&&plan.CanBuild;reasons[key]=plan.Reason;}
  return new DriverTrialInspection{Eligible=eligible,File=full,Driver=name,Hash=review.Hash,Version=review.Version,CandidateCount=review.CandidateCount,Signature=review.Signature,Buildable=buildable,Reasons=reasons,Message=eligible?"This file has one recognised cache setting. Each size remains locked until its own isolated test passes.":"No safe test can be offered: one matching instruction sequence and a trusted original file are required."};
 }
 public static void RequireReady(){Require(!IsolationControl.Administrator,"Run Arc Cache normally for this test, not as administrator.");Require(WindowsProbeReady(),"Open Graphics, enable game-specific settings, and restart Windows before running this separate test.");}
 public static bool Amd64Dll(string file) {
  using(var r=new BinaryReader(File.OpenRead(file))){if(r.BaseStream.Length<64||r.ReadUInt16()!=0x5a4d)return false;r.BaseStream.Position=60;uint nt=r.ReadUInt32();if(nt>r.BaseStream.Length-26)return false;r.BaseStream.Position=nt;if(r.ReadUInt32()!=0x4550||r.ReadUInt16()!=0x8664)return false;r.BaseStream.Position=nt+22;return (r.ReadUInt16()&0x2000)!=0;}
 }
 // Only this isolated probe's private directory is created. No game/system install path is accepted.
 public static object Create(string file,int mib,string probe) {
  if(IsolationControl.Administrator)throw new InvalidOperationException("Run the driver trial normally, not as administrator. No elevation is required.");
  string full=Path.GetFullPath(file);Engine.CheckPath(full);string driver=Path.GetFileName(full);Require(SupportedName(driver),"Select an Xe1, Xe2 or Xe3 file.");
  Require(new FileInfo(full).Length<=128*1024*1024,"Driver file exceeds test bounds.");byte[] original=File.ReadAllBytes(full);byte[] patched=Patch(original,mib,driver);string folder=Path.GetDirectoryName(full);Report(5,"Checking the source and signed dependencies");
  var package=DriverCatalog.Packages().FirstOrDefault(p=>p.Id==DriverCatalog.Selected().Id&&p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));Require(package!=null,"Device-test dependencies must come from a currently active Intel driver package. Offline files may be scanned, but cannot be used for this device test.");string identityBefore=DriverCatalog.Identity(package);
  Engine.CheckPath(probe);Require(File.Exists(probe)&&Amd64Exe(probe),"This build does not include the compiled x64 graphics check program.");
  var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);long total=0;
  var candidates=Directory.EnumerateFiles(folder,"*.dll").Take(257).ToArray();Require(candidates.Length<=256,"Driver cohort exceeds experiment bounds.");
  foreach(string source in candidates) {
   Engine.CheckPath(source);Require(sources.Count<128,"Driver cohort exceeds experiment bounds.");if(!Amd64Dll(source))continue;
   total=checked(total+new FileInfo(source).Length);Require(total<=2L*1024*1024*1024,"Driver cohort exceeds the 2 GiB experiment bound.");
   Require(WindowsPlatform.EmbeddedSignature(source).StartsWith("Valid"),"Dependency needs valid offline embedded trust: "+Path.GetFileName(source)+". Catalog-only/unknown dependencies are not automatically accepted.");
   sources.Add(Path.GetFileName(source),IsolationControl.FileHash(source));
  }
  Require(sources.ContainsKey(driver)&&sources.ContainsKey("igd12umd64.dll"),"The active package is missing required Intel/loader files.");
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials");Directory.CreateDirectory(root);Engine.CheckPath(root);Require(new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace>=(mib==512?2:1)*total+(mib==512?2:1)*original.Length+64L*1024*1024,"Not enough free disk space for both test copies and backups.");
  string target=Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);
  string exe=Path.Combine(target,"ArcCache.DriverProbe.exe"),local=exe+".local";Directory.CreateDirectory(local);Directory.CreateDirectory(Path.Combine(target,"backup"));
  File.WriteAllBytes(Path.Combine(target,"backup",driver),original);var security=Engine.Security(full);
  var result=new Dictionary<string,object>{{"Schema",1},{"Kind","Unreviewed isolated driver trial"},{"Source",full},{"SourceFolder",folder},{"SourceIdentity",identityBefore},{"AdapterId",package.Id},{"Adapter",package.Adapter},{"MiB",mib},{"OriginalHash",Engine.Hash(original)},{"Driver",driver},{"PatchedHash",Engine.Hash(patched)},{"OriginalAcl",security},{"Sources",sources},{"PrivateFolder",local},{"RuntimeVerified",false},{"Status","preparing"}};
  string receipt=Path.Combine(target,"experiment.json");Report(15,"Saving backup and test record: "+receipt);var json=new JavaScriptSerializer();File.WriteAllText(receipt,json.Serialize(result));
  int copied=0;foreach(var source in sources){Report(20+(int)(60.0*copied++/sources.Count),"Copying "+source.Key);string src=Path.Combine(folder,source.Key),dst=Path.Combine(local,source.Key);Require(IsolationControl.FileHash(src)==source.Value,"Source changed during preparation: "+source.Key);File.Copy(src,dst);Require(IsolationControl.FileHash(dst)==source.Value,"Copy hash mismatch: "+source.Key);}
  Require(Engine.Hash(File.ReadAllBytes(Path.Combine(local,driver)))==Engine.Hash(original),"Copied original differs.");
  File.WriteAllBytes(Path.Combine(local,driver),patched);Require(Engine.Hash(File.ReadAllBytes(Path.Combine(local,driver)))==Engine.Hash(patched),"Patched private copy verification failed.");
  Require(DriverCatalog.Packages().Any(p=>p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase)&&DriverCatalog.Identity(p)==identityBefore),"Active package changed during preparation. Do not run this bundle.");
  string probeHash=IsolationControl.FileHash(probe);File.Copy(probe,exe);Require(IsolationControl.FileHash(exe)==probeHash,"Probe copy differs.");
  var outputs=new Dictionary<string,string>(sources,StringComparer.OrdinalIgnoreCase);outputs[driver]=Engine.Hash(patched);result["Files"]=outputs;result["ProbeHash"]=probeHash;result["Status"]="prepared-no-runtime-test";result["Receipt"]=receipt;result["Executable"]=exe;
  File.WriteAllText(receipt,json.Serialize(result));Report(100,"Test files and backup checked");return result;
 }
 public static object Run(string receipt,string shippedProbe) {
  if(IsolationControl.Administrator)throw new InvalidOperationException("Run this experiment without administrator rights.");
  string full=Path.GetFullPath(receipt),root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials"));
  Require(full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Only an owned isolated experiment receipt may be run.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Experiment receipt exceeds bounds.");
  var json=new JavaScriptSerializer();var values=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(full));
  Require(Convert.ToInt32(values["Schema"])==1&&(string)values["Kind"]=="Unreviewed isolated driver trial","Unknown experiment schema.");
  string exe=Path.Combine(Path.GetDirectoryName(full),"ArcCache.DriverProbe.exe"),local=exe+".local";
  Require((string)values["Executable"]==exe&&(string)values["PrivateFolder"]==local,"Experiment paths differ from owned receipt.");
  Require(WindowsProbeReady(),"Enable game-specific settings and restart Windows before running this device test.");
  Require(IsolationControl.FileHash(exe)==IsolationControl.FileHash(shippedProbe)&&(string)values["ProbeHash"]==IsolationControl.FileHash(shippedProbe),"Owned probe differs from this build.");
  string sourceFolder=(string)values["SourceFolder"];Engine.CheckPath(sourceFolder);var package=DriverCatalog.Packages().FirstOrDefault(p=>p.Id==(string)values["AdapterId"]&&p.Folder.Equals(sourceFolder,StringComparison.OrdinalIgnoreCase));Require(package!=null&&DriverCatalog.Identity(package)==(string)values["SourceIdentity"],"The active package changed. Prepare a new bundle; do not reuse this one.");
  var sourceHashes=(Dictionary<string,object>)values["Sources"];
  string driver=(string)values["Driver"];Require(SupportedName(driver),"Invalid test driver name.");var files=(Dictionary<string,object>)values["Files"];Require(files.Count<=128&&Directory.GetFiles(local).Length==files.Count&&Directory.GetDirectories(local).Length==0,"Experiment cohort changed.");
  Require(files.Count==sourceHashes.Count,"Experiment source/output membership differs.");
  foreach(var entry in files){Require(Path.GetFileName(entry.Key)==entry.Key,"Invalid experiment file name.");Require(sourceHashes.ContainsKey(entry.Key),"Untracked experiment dependency.");string source=Path.Combine(sourceFolder,entry.Key);Engine.CheckPath(source);Require(IsolationControl.FileHash(source)==(string)sourceHashes[entry.Key],"Source dependency changed: "+entry.Key);if(!entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase))Require((string)entry.Value==(string)sourceHashes[entry.Key],"Private dependency is not the original source: "+entry.Key);string path=Path.Combine(local,entry.Key);Engine.CheckPath(path);Require(IsolationControl.FileHash(path)==(string)entry.Value,"Changed experiment file: "+entry.Key);}
  string backup=Path.Combine(Path.GetDirectoryName(full),"backup",driver);Engine.CheckPath(backup);Require(new FileInfo(backup).Length<=128*1024*1024,"Original backup size exceeds bounds.");byte[] before=File.ReadAllBytes(backup);Require(Engine.Hash(before)==(string)values["OriginalHash"]&&Engine.Hash(before)==(string)sourceHashes[driver],"Original backup differs.");int mib=Convert.ToInt32(values["MiB"]);Require(Engine.Hash(Patch(before,mib,driver))==IsolationControl.FileHash(Path.Combine(local,driver)),"Test bytes differ from the recorded one-value change.");
  var reports=new List<string>();bool timedOut=false,memoryStopped=false;int code=0;string mode=mib==512?"control":"size",cacheFile="";
  for(int pass=0;pass<2;pass++){
   string phase=pass==0?"write":"reload";var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe),Arguments=QuoteArgument(driver)+" "+QuoteArgument((string)values["Adapter"])+" "+mode+" "+phase+" "+QuoteArgument(pass==0?"":cacheFile)};
   using(var process=Process.Start(start)){try{
    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var watch=Stopwatch.StartNew();Report(5+pass*45,pass==0?"Writing pipeline-cache workload":"Restarting the probe for a second pipeline-cache workload");while(!process.WaitForExit(200)){process.Refresh();if(process.PrivateMemorySize64>3L*1024*1024*1024){memoryStopped=true;break;}if(watch.ElapsedMilliseconds>=60000){timedOut=true;break;}Report(Math.Min(94,10+pass*45+(int)(watch.ElapsedMilliseconds/1200)),phase+" run · 60 second limit");}
    if(timedOut||memoryStopped){process.Kill();Require(process.WaitForExit(5000),"The owned test process could not be stopped. Do not start another test.");}code=process.ExitCode;string phaseOutput=output.Result+errors.Result;reports.Add("=== "+phase.ToUpperInvariant()+" RUN ===\n"+phaseOutput);if(code!=0||timedOut||memoryStopped)break;
    if(pass==0){cacheFile=ObservedCacheWrites(phaseOutput);if(cacheFile==null){code=12;reports.Add("The write run did not change any Intel shader-cache file; cache-file identity is unavailable.");break;}}
    else if(!HasObservedCacheRead(phaseOutput)){code=12;reports.Add("The reload run did not read bytes from the exact file written by the first run.");break;}
   }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch{}}}
  }
  string report=String.Join("\n",reports);values["MemoryGuardTriggered"]=memoryStopped;values["ReloadRunVerified"]=reports.Count>=2&&!timedOut&&!memoryStopped&&code==0;values["CacheReloadObserved"]=Convert.ToBoolean(values["ReloadRunVerified"]);values["CacheFileObserved"]=cacheFile;values["Status"]=memoryStopped?"memory-limit-stopped":timedOut?"timeout":Convert.ToBoolean(values["ReloadRunVerified"])?"pipeline-cache-reload-observed":"runtime-not-verified";values["RuntimeVerified"]=Convert.ToBoolean(values["ReloadRunVerified"]);values["ExitCode"]=code;values["Report"]=report;values["CheckedUtc"]=DateTime.UtcNow.ToString("o");
  var loaded=System.Text.RegularExpressions.Regex.Matches(report,@"(?im)^LoadedCacheDriver:\s*(igd12um64xe2\.dll|igd12um64xe3\.dll|igd12um64xeh\.dll)\s*$");var names=loaded.Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(Convert.ToBoolean(values["RuntimeVerified"])&&names.Length==1)values["LoadedCacheDriver"]=names[0];else{values.Remove("LoadedCacheDriver");values["RuntimeVerified"]=false;values["Status"]="runtime-not-verified";}
  File.WriteAllText(Path.Combine(Path.GetDirectoryName(full),"device-test.txt"),report);File.WriteAllText(full,json.Serialize(values));Report(100,Convert.ToBoolean(values["ReloadRunVerified"])?"The second run read the shader-cache file written by the first":"The driver copy loaded, but cache reload was not verified");return values;
 }
 static string QuoteArgument(string value){if(value==null)value="";var result=new System.Text.StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1).Append(c);slashes=0;continue;}if(slashes>0){result.Append('\\',slashes);slashes=0;}result.Append(c);}if(slashes>0)result.Append('\\',slashes*2);return result.Append('"').ToString();}
 public static bool CompatibleProofHelper(string savedHash,string currentHash,string driver){return savedHash==currentHash||driver!=Xe1Patch.FileName&&(driver=="igd12um64xe2.dll"||driver=="igd12um64xe3.dll")&&savedHash=="348B383C32D4FBC8EFC5A6DD0D053CF8C9521CC2E8A763EFC25D378B47EE88EA";}
 internal static string TrialRootOverride {get;set;}
 internal static string GameReceiptRootOverride {get;set;}
 public static string TrialRoot{get{return TrialRootOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials");}}
 internal static HashSet<string> SelectCleanupKeepPaths(IEnumerable<DriverTrialCleanupRecord> records,string identity,string adapterId,string probeHash){
  var all=records.ToArray();var keep=new HashSet<string>(all.Where(r=>r.Referenced).Select(r=>Path.GetFullPath(r.Receipt)),StringComparer.OrdinalIgnoreCase);
  var current=all.Where(r=>r.Passed&&r.Identity==identity&&r.AdapterId==adapterId&&r.ProbeHash==probeHash).ToArray();
  foreach(var group in current.Where(r=>r.MiB==512).GroupBy(r=>r.Driver,StringComparer.OrdinalIgnoreCase).Concat(current.Where(r=>r.MiB!=512).GroupBy(r=>r.MiB.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+r.Driver,StringComparer.OrdinalIgnoreCase))) {
   var newest=group.OrderByDescending(r=>r.CheckedUtc,StringComparer.Ordinal).FirstOrDefault();if(newest!=null)keep.Add(Path.GetFullPath(newest.Receipt));
  }
  return keep;
 }
 static HashSet<string> TrialReferences(string gamesRoot){var refs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);if(!Directory.Exists(gamesRoot))return refs;Engine.CheckPath(gamesRoot);foreach(string file in Directory.GetFiles(gamesRoot,"*.json")){Engine.CheckPath(file);if(!MatchedSet.IsTrialReceipt(file))continue;var r=MatchedSet.Read(file);if(!String.IsNullOrWhiteSpace(r.TrialReceipt))refs.Add(Path.GetFullPath(r.TrialReceipt));}return refs;}
 static long OwnedFolderBytes(string folder){Engine.CheckPath(folder);if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("A link blocks driver-test cleanup: "+folder);long bytes=0;foreach(string file in Directory.GetFiles(folder)){Engine.CheckPath(file);if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("A link blocks driver-test cleanup: "+file);bytes=checked(bytes+new FileInfo(file).Length);}foreach(string directory in Directory.GetDirectories(folder)){Engine.CheckPath(directory);if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("A link blocks driver-test cleanup: "+directory);bytes=checked(bytes+OwnedFolderBytes(directory));}return bytes;}
 static bool CompleteOwnedRecord(string receipt){try{var v=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt));return v!=null&&v.ContainsKey("Schema")&&Convert.ToInt32(v["Schema"])==1&&v.ContainsKey("Kind")&&(string)v["Kind"]=="Unreviewed isolated driver trial"&&v.ContainsKey("OriginalHash")&&v.ContainsKey("Driver")&&v.ContainsKey("MiB")&&v.ContainsKey("Sources")&&v.ContainsKey("Files")&&v.ContainsKey("Receipt")&&Path.GetFullPath((string)v["Receipt"]).Equals(Path.GetFullPath(receipt),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 public static DriverTrialCleanupPlan PlanCleanup(string probe,string gamesRoot){
  var plan=new DriverTrialCleanupPlan{Folders=new string[0]};if(!Directory.Exists(TrialRoot))return plan;Engine.CheckPath(TrialRoot);string probeHash=IsolationControl.FileHash(probe);var selected=DriverCatalog.Selected();string identity=selected==null?null:DriverCatalog.Identity(selected),adapter=selected==null?null:selected.Id;var refs=TrialReferences(gamesRoot);var records=new List<DriverTrialCleanupRecord>();var folders=Directory.GetDirectories(TrialRoot).Take(1000).ToArray();
  foreach(string folder in folders){Engine.CheckPath(folder);if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("A link blocks driver-test cleanup: "+folder);string receipt=Path.Combine(folder,"experiment.json");if(!File.Exists(receipt)||!CompleteOwnedRecord(receipt)){plan.SkippedUnsafe++;continue;}var r=ReadCandidate(receipt);bool passed=false;string id=null,adapterId=null,hash=null,driver=null,checkedUtc="";int mib=0;
   try{var raw=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt));id=raw.ContainsKey("SourceIdentity")?raw["SourceIdentity"] as string:null;adapterId=raw.ContainsKey("AdapterId")?raw["AdapterId"] as string:null;hash=raw.ContainsKey("ProbeHash")?raw["ProbeHash"] as string:null;mib=Convert.ToInt32(raw["MiB"]);driver=mib==512&&raw.ContainsKey("LoadedCacheDriver")?raw["LoadedCacheDriver"] as string:raw["Driver"] as string;checkedUtc=raw.ContainsKey("CheckedUtc")?raw["CheckedUtc"] as string:"";if(r!=null){DriverTrial.VerifySuccessful(receipt,probe,false);passed=true;}}catch{}
   records.Add(new DriverTrialCleanupRecord{Receipt=receipt,Identity=id,AdapterId=adapterId,ProbeHash=hash,Driver=driver,MiB=mib,CheckedUtc=checkedUtc,Passed=passed,Referenced=refs.Contains(Path.GetFullPath(receipt))});
  }
  var keep=SelectCleanupKeepPaths(records,identity,adapter,probeHash);var remove=new List<string>();long bytes=0;
  foreach(var record in records){if(keep.Contains(Path.GetFullPath(record.Receipt)))continue;try{long size=OwnedFolderBytes(Path.GetDirectoryName(record.Receipt));remove.Add(Path.GetDirectoryName(record.Receipt));bytes=checked(bytes+size);}catch{plan.SkippedUnsafe++;}}
  plan.Folders=remove.ToArray();plan.BytesToFree=bytes;return plan;
 }
 public static object CleanupOldTests(string probe,string gamesRoot){var plan=PlanCleanup(probe,gamesRoot);var removed=new List<string>();var errors=new List<string>();long freed=0;foreach(string folder in plan.Folders){try{long bytes=OwnedFolderBytes(folder);Discard(Path.Combine(folder,"experiment.json"),probe);removed.Add(folder);freed=checked(freed+bytes);}catch(Exception e){errors.Add(folder+" · "+e.Message);}}return new{RemovedFolders=removed.ToArray(),BytesFreed=freed,SkippedUnsafe=plan.SkippedUnsafe,Errors=errors.ToArray()};}
 public static bool LegacyTrialNeedsRetest(string receipt,int mib,string driver){try{string full=Path.GetFullPath(receipt),root=Path.GetFullPath(TrialRoot);if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(full)!="experiment.json"||!File.Exists(full)||new FileInfo(full).Length>1048576)return false;var r=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(full));if(Convert.ToInt32(r["MiB"])!=mib||!String.Equals((string)r["Driver"],driver,StringComparison.OrdinalIgnoreCase))return false;return !r.ContainsKey("CacheReloadObserved")||!Convert.ToBoolean(r["CacheReloadObserved"])||!r.ContainsKey("CacheFileObserved")||String.IsNullOrWhiteSpace(r["CacheFileObserved"] as string);}catch{return false;}}
 public static Dictionary<string,object> Saved(string receipt){
  string full=Path.GetFullPath(receipt);Require(Path.GetDirectoryName(Path.GetDirectoryName(full)).Equals(Path.GetFullPath(TrialRoot),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Select an owned driver test record.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Test record exceeds bounds.");var r=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(full));Require(Convert.ToInt32(r["Schema"])==1&&(string)r["Kind"]=="Unreviewed isolated driver trial","Unknown driver test record.");Require(Convert.ToBoolean(r["RuntimeVerified"])&&Convert.ToInt32(r["ExitCode"])==0&&!Convert.ToBoolean(r["MemoryGuardTriggered"]),"The saved graphics check did not pass.");if(Convert.ToInt32(r["MiB"])>=2048)Require(r.ContainsKey("CacheReloadObserved")&&Convert.ToBoolean(r["CacheReloadObserved"])&&r.ContainsKey("CacheFileObserved")&&!String.IsNullOrWhiteSpace(r["CacheFileObserved"] as string),"Large-size evidence must include a nonzero read from the exact shader-cache file written by the first run.");Require(((string)r["Receipt"]).Equals(full,StringComparison.OrdinalIgnoreCase),"Test record path differs.");return r;
 }
 internal static Dictionary<string,object> ReadCandidate(string receipt){try{return Saved(receipt);}catch(IOException){}catch(InvalidDataException){}catch(InvalidOperationException){}catch(ArgumentException){}catch(KeyNotFoundException){}return null;}
 internal static bool SameSource(Dictionary<string,object> a,Dictionary<string,object> b){if(!new[]{"SourceIdentity","AdapterId","ProbeHash"}.All(k=>a.ContainsKey(k)&&b.ContainsKey(k)&&(string)a[k]==(string)b[k]))return false;string familyA=a.ContainsKey("LoadedCacheDriver")?(string)a["LoadedCacheDriver"]:(string)a["Driver"],familyB=b.ContainsKey("LoadedCacheDriver")?(string)b["LoadedCacheDriver"]:(string)b["Driver"];return String.Equals(familyA,familyB,StringComparison.OrdinalIgnoreCase);}
 public static string FindSuccessful(string probe){return FindSuccessful(probe,1024,null);}
 public static string FindSuccessful(string probe,int mib){return FindSuccessful(probe,mib,null);}
 public static string FindSuccessful(string probe,int mib,string driverName){
  if(!Directory.Exists(TrialRoot))return null;Engine.CheckPath(TrialRoot);var selected=DriverCatalog.Selected();if(selected==null)return null;string identity=DriverCatalog.Identity(selected);var records=new List<Dictionary<string,object>>();
  foreach(string folder in Directory.EnumerateDirectories(TrialRoot).Take(200)){var r=ReadCandidate(Path.Combine(folder,"experiment.json"));if(r!=null&&(string)r["SourceIdentity"]==identity&&(string)r["AdapterId"]==selected.Id)records.Add(r);}
  foreach(var tested in records.Where(r=>Convert.ToInt32(r["MiB"])==mib&&(driverName==null||(mib==512?r.ContainsKey("LoadedCacheDriver")&&(string)r["LoadedCacheDriver"]==driverName:(string)r["Driver"]==driverName))).OrderByDescending(r=>(string)r["CheckedUtc"])){
   if(mib!=512&&!records.Any(r=>Convert.ToInt32(r["MiB"])==512&&r.ContainsKey("LoadedCacheDriver")&&(string)r["LoadedCacheDriver"]==(string)tested["Driver"]&&SameSource(r,tested)))continue;
   try{VerifySuccessful((string)tested["Receipt"],probe,false);return (string)tested["Receipt"];}catch(IOException){}catch(InvalidDataException){}catch(InvalidOperationException){}catch(ArgumentException){}
  }return null;
 }
 public static string FindConfirmedCacheDriver(AdapterPackage package,string probe){
  if(package==null||!Directory.Exists(TrialRoot))return null;Engine.CheckPath(TrialRoot);string identity=DriverCatalog.Identity(package);
  foreach(string folder in Directory.EnumerateDirectories(TrialRoot).Take(200).OrderByDescending(Directory.GetLastWriteTimeUtc)){
   var r=ReadCandidate(Path.Combine(folder,"experiment.json"));if(r==null||Convert.ToInt32(r["MiB"])!=512||(string)r["SourceIdentity"]!=identity||(string)r["AdapterId"]!=package.Id||!r.ContainsKey("LoadedCacheDriver"))continue;
   string loaded=r["LoadedCacheDriver"] as string;if(!DriverCatalog.IsCacheDriverName(loaded))continue;
   try{VerifySuccessful((string)r["Receipt"],probe,false);}catch(Exception){continue;}return loaded;
  }return null;
 }
 public static Dictionary<string,object> VerifySuccessful(string receipt,string probe,bool full){
  var patched=Saved(receipt);int testedMiB=Convert.ToInt32(patched["MiB"]);Require(new[]{512,1024,2048,4096,8192,16384}.Contains(testedMiB),"Unsupported size in test receipt.");var selected=DriverCatalog.Selected();Require(selected!=null&&(string)patched["AdapterId"]==selected.Id&&(string)patched["SourceIdentity"]==DriverCatalog.Identity(selected),"The driver changed after this test. Run a new graphics test first.");
  Dictionary<string,object> control=null;if(testedMiB!=512){foreach(string folder in Directory.EnumerateDirectories(TrialRoot).Take(200)){var r=ReadCandidate(Path.Combine(folder,"experiment.json"));if(r!=null&&Convert.ToInt32(r["MiB"])==512&&SameSource(r,patched)){control=r;break;}}Require(control!=null,"The successful unchanged control is missing. Run the graphics check again.");}
  foreach(var r in control==null?new[]{patched}:new[]{control,patched}){
   string driver=(string)r["Driver"];Require(SupportedName(driver),"Unknown test driver.");string source=Path.Combine(selected.Folder,driver),folder=Path.GetDirectoryName((string)r["Receipt"]),exe=Path.Combine(folder,"ArcCache.DriverProbe.exe"),local=exe+".local";
   Require(((string)r["Source"]).Equals(source,StringComparison.OrdinalIgnoreCase)&&((string)r["SourceFolder"]).Equals(selected.Folder,StringComparison.OrdinalIgnoreCase)&&((string)r["Executable"]).Equals(exe,StringComparison.OrdinalIgnoreCase)&&((string)r["PrivateFolder"]).Equals(local,StringComparison.OrdinalIgnoreCase),"Saved test paths differ from this package.");Engine.CheckPath(exe);string savedProbe=IsolationControl.FileHash(exe);Require((string)r["ProbeHash"]==savedProbe&&CompatibleProofHelper(savedProbe,IsolationControl.FileHash(probe),driver),"Run the test again with this app version.");
   string backup=Path.Combine(folder,"backup",driver);Engine.CheckPath(backup);Require(new FileInfo(backup).Length<=128L*1024*1024,"Backup exceeds bounds.");byte[] original=File.ReadAllBytes(backup);Require(Engine.Hash(original)==(string)r["OriginalHash"]&&IsolationControl.FileHash(source)==(string)r["OriginalHash"],"Test source or backup changed.");
   string output=Path.Combine(local,driver);Engine.CheckPath(output);Require(Engine.Hash(Patch(original,Convert.ToInt32(r["MiB"]),driver))==(string)r["PatchedHash"]&&IsolationControl.FileHash(output)==(string)r["PatchedHash"],"Successful test copy changed.");
   var sources=(Dictionary<string,object>)r["Sources"];var files=(Dictionary<string,object>)r["Files"];Require(sources.Count<=128&&files.Count==sources.Count&&sources.ContainsKey(driver)&&sources.ContainsKey("igd12umd64.dll"),"Invalid test dependency list.");
   if(r.ContainsKey("LoadedCacheDriver")){string loaded=r["LoadedCacheDriver"] as string;Require(DriverCatalog.IsCacheDriverName(loaded)&&sources.ContainsKey(loaded)&&files.ContainsKey(loaded),"Loaded cache DLL is not in the signed source cohort.");if(Convert.ToInt32(r["MiB"])==512)Require((string)files[loaded]==(string)sources[loaded],"Control copy did not retain the loader-selected cache DLL unchanged.");else Require(String.Equals(loaded,driver,StringComparison.OrdinalIgnoreCase),"The size test did not load the DLL that was patched.");string marker="LoadedCacheDriver: "+loaded;Require(System.Text.RegularExpressions.Regex.Matches((string)r["Report"],System.Text.RegularExpressions.Regex.Escape(marker),System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count==2,"The loader result was not reported by both probe runs.");}
   if(full){Require(Directory.GetFiles(local).Length==files.Count&&Directory.GetDirectories(local).Length==0,"Test folder contents changed.");foreach(var entry in sources){Require(Path.GetFileName(entry.Key)==entry.Key&&entry.Key.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)&&files.ContainsKey(entry.Key),"Invalid dependency name.");string src=Path.Combine(selected.Folder,entry.Key),dst=Path.Combine(local,entry.Key);Engine.CheckPath(src);Engine.CheckPath(dst);Require(IsolationControl.FileHash(src)==(string)entry.Value&&WindowsPlatform.EmbeddedSignature(src).StartsWith("Valid"),"Driver dependency changed or is not trusted: "+entry.Key);string expected=entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase)?(string)r["PatchedHash"]:(string)entry.Value;Require((string)files[entry.Key]==expected&&IsolationControl.FileHash(dst)==expected,"Test dependency changed: "+entry.Key);}}
  }Require(DriverCatalog.Identity(DriverCatalog.Selected())==(string)patched["SourceIdentity"],"Driver changed during verification.");return patched;
 }
 public static object Discard(string receipt,string probe){
  string full=Path.GetFullPath(receipt),root=Path.GetFullPath(TrialRoot),folder=Path.GetDirectoryName(full);
  Require(Path.GetDirectoryName(folder).Equals(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Only a direct owned test folder may be removed.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Test record exceeds bounds.");
  var values=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(full));Require(Convert.ToInt32(values["Schema"])==1&&(string)values["Kind"]=="Unreviewed isolated driver trial","Unknown test record.");
  string games=GameReceiptRootOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","games");if(Directory.Exists(games))foreach(string game in Directory.EnumerateFiles(games,"*.json").Take(10000)){if(!MatchedSet.IsTrialReceipt(game))continue;var saved=MatchedSet.Read(game);Require(saved.Status=="removed"||saved.TrialReceipt!=full,"A game still uses this test. Use default settings for that game before removing these test copies.");}
  string driver=(string)values["Driver"];Require(SupportedName(driver),"Invalid driver name.");
  string exe=Path.Combine(folder,"ArcCache.DriverProbe.exe"),local=exe+".local",backup=Path.Combine(folder,"backup",driver);Engine.CheckPath(backup);
  byte[] before=File.ReadAllBytes(backup);Require(Engine.Hash(before)==(string)values["OriginalHash"],"Original backup changed. Removal stopped.");
  foreach(var process in Process.GetProcessesByName("ArcCache.DriverProbe"))using(process){try{Require(!process.MainModule.FileName.Equals(exe,StringComparison.OrdinalIgnoreCase),"Close the separate graphics check first.");}catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("A test process cannot be inspected. Close it before removing tests.");}}
  var sources=(Dictionary<string,object>)values["Sources"];Require(sources.Count<=128,"Test record exceeds bounds.");
  var allowed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);allowed[full]=null;allowed[Path.Combine(folder,"device-test.txt")]=null;allowed[backup]=(string)values["OriginalHash"];string helperHash=File.Exists(exe)?IsolationControl.FileHash(exe):IsolationControl.FileHash(probe);Require(CompatibleProofHelper(helperHash,IsolationControl.FileHash(probe),driver),"Unknown test helper stops cleanup.");allowed[exe]=helperHash;
  foreach(var entry in sources){Require(Path.GetFileName(entry.Key)==entry.Key,"Invalid dependency name.");allowed[Path.Combine(local,entry.Key)]=entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase)?Engine.Hash(Patch(before,Convert.ToInt32(values["MiB"]),driver)):(string)entry.Value;}
  var members=new List<string>(Directory.GetFiles(folder));foreach(string dir in Directory.GetDirectories(folder)){Engine.CheckPath(dir);Require(dir.Equals(local,StringComparison.OrdinalIgnoreCase)||dir.Equals(Path.Combine(folder,"backup"),StringComparison.OrdinalIgnoreCase),"An extra folder stops removal.");Require(Directory.GetDirectories(dir).Length==0,"An extra nested folder stops removal.");members.AddRange(Directory.GetFiles(dir));}
  foreach(string file in members){Engine.CheckPath(file);Require(allowed.ContainsKey(file),"An extra file stops removal: "+Path.GetFileName(file));if(allowed[file]!=null)Require(IsolationControl.FileHash(file)==allowed[file],"A changed file stops removal: "+Path.GetFileName(file));}
  // The canonical direct child and every member were checked before recursive deletion.
  Directory.Delete(folder,true);return new{Removed=true,Folder=folder};
 }
 [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern ulong GetTickCount64();
 static bool WindowsProbeReady(){
  using(var k=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options")){if(k==null||Convert.ToInt32(k.GetValue("DevOverrideEnable",0))!=1)return false;}
  if(File.Exists(IsolationControl.ReceiptPath)){Engine.CheckPath(IsolationControl.ReceiptPath);var saved=new JavaScriptSerializer().Deserialize<OverrideReceipt>(File.ReadAllText(IsolationControl.ReceiptPath));DateTime created;if(saved!=null&&saved.Status=="enabled-reboot-required"&&DateTime.TryParse(saved.CreatedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out created)&&created.ToUniversalTime()>DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64()))return false;}
  return true;
 }

 static bool Amd64Exe(string file){using(var r=new BinaryReader(File.OpenRead(file))){if(r.BaseStream.Length<64||r.ReadUInt16()!=0x5a4d)return false;r.BaseStream.Position=60;uint nt=r.ReadUInt32();if(nt>r.BaseStream.Length-26)return false;r.BaseStream.Position=nt;return r.ReadUInt32()==0x4550&&r.ReadUInt16()==0x8664;}}
}
}
