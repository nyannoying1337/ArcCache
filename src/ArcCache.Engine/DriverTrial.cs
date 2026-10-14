using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Web.Script.Serialization;
namespace ArcCap {
public static class DriverTrial {
 static void Require(bool yes,string message){if(!yes)throw new InvalidDataException(message);}
 public static Action<int,string> Progress;
 static void Report(int value,string text){if(Progress!=null)Progress(value,text);}
 // Pattern-based output is an unreviewed test artifact, never a supported game definition.
 public static byte[] Patch(byte[] original,int mib) {
  Require(original.Length>=256&&original.Length<=128*1024*1024,"Driver file size is outside test bounds.");
  Require(mib==512||mib==1024,"Unreviewed tests only support an unchanged control and a 1 GiB copy.");
  var offsets=DriverCatalog.CandidateOffsets(original);Require(offsets.Length==1,"Exactly one matching cache-setting instruction sequence is required. No patch can be guessed.");
  byte[] output=(byte[])original.Clone();
  if(mib==1024)Array.Copy(BitConverter.GetBytes(0x40000000),0,output,offsets[0]+16,4);
  byte[] normalized=(byte[])output.Clone();Array.Copy(original,offsets[0]+16,normalized,offsets[0]+16,4);
  Require(normalized.SequenceEqual(original),"Changes outside the one cache value are not allowed.");
  if(mib==1024){string hash=Engine.Hash(original);if(hash==Engine.Original)Require(Engine.Hash(output)==Engine.OneGiB,"Known Xe2 output differs.");if(hash==DriverCatalog.Xe3Original)Require(Engine.Hash(output)=="F6F9ABC40702C5EBC29D848E416510C7B0D5D5D61021B3C8D74726F02E81B9F9","Known Xe3 output differs.");}
  return output;
 }
 public static object Inspect(string file){
  string full=Path.GetFullPath(file);Engine.CheckPath(full);string name=Path.GetFileName(full);
  Require(name=="igd12um64xe2.dll"||name=="igd12um64xe3.dll","Select the Xe2 or Xe3 Intel driver file from the active package.");
  var package=DriverCatalog.Selected();Require(package!=null&&package.Folder.Equals(Path.GetDirectoryName(full),StringComparison.OrdinalIgnoreCase),"This file must belong to the selected active Intel graphics package.");
  var review=DriverCatalog.Review(full,true);bool eligible=review.CandidateCount==1&&review.Signature!=null&&review.Signature.StartsWith("Valid");
  return new{Eligible=eligible,File=full,review.Hash,review.Version,review.CandidateCount,review.Signature,Message=eligible?"A separate 1 GiB test is available. This file is not approved for game setups by this check.":"No safe test can be offered: one matching instruction sequence and a trusted original file are required."};
 }
 public static void RequireReady(){Require(!IsolationControl.Administrator,"Run Arc Cache normally for this test, not as administrator.");Require(WindowsProbeReady(),"Enable game-specific settings in Settings and restart Windows before running this separate test.");}
 public static bool Amd64Dll(string file) {
  using(var r=new BinaryReader(File.OpenRead(file))){if(r.BaseStream.Length<64||r.ReadUInt16()!=0x5a4d)return false;r.BaseStream.Position=60;uint nt=r.ReadUInt32();if(nt>r.BaseStream.Length-26)return false;r.BaseStream.Position=nt;if(r.ReadUInt32()!=0x4550||r.ReadUInt16()!=0x8664)return false;r.BaseStream.Position=nt+22;return (r.ReadUInt16()&0x2000)!=0;}
 }
 // Only this isolated probe's private directory is created. No game/system install path is accepted.
 public static object Create(string file,int mib,string probe) {
  if(IsolationControl.Administrator)throw new InvalidOperationException("Run the driver trial normally, not as administrator. No elevation is required.");
  string full=Path.GetFullPath(file);Engine.CheckPath(full);string driver=Path.GetFileName(full);Require(driver=="igd12um64xe2.dll"||driver=="igd12um64xe3.dll","Select an Xe2 or Xe3 file.");
  Require(new FileInfo(full).Length<=128*1024*1024,"Driver file exceeds test bounds.");byte[] original=File.ReadAllBytes(full),patched=Patch(original,mib);string folder=Path.GetDirectoryName(full);Report(5,"Checking the source and signed dependencies");
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
  string driver=(string)values["Driver"];Require(driver=="igd12um64xe2.dll"||driver=="igd12um64xe3.dll","Invalid test driver name.");var files=(Dictionary<string,object>)values["Files"];Require(files.Count<=128&&Directory.GetFiles(local).Length==files.Count&&Directory.GetDirectories(local).Length==0,"Experiment cohort changed.");
  Require(files.Count==sourceHashes.Count,"Experiment source/output membership differs.");
  foreach(var entry in files){Require(Path.GetFileName(entry.Key)==entry.Key,"Invalid experiment file name.");Require(sourceHashes.ContainsKey(entry.Key),"Untracked experiment dependency.");string source=Path.Combine(sourceFolder,entry.Key);Engine.CheckPath(source);Require(IsolationControl.FileHash(source)==(string)sourceHashes[entry.Key],"Source dependency changed: "+entry.Key);if(!entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase))Require((string)entry.Value==(string)sourceHashes[entry.Key],"Private dependency is not the original source: "+entry.Key);string path=Path.Combine(local,entry.Key);Engine.CheckPath(path);Require(IsolationControl.FileHash(path)==(string)entry.Value,"Changed experiment file: "+entry.Key);}
  string backup=Path.Combine(Path.GetDirectoryName(full),"backup",driver);Engine.CheckPath(backup);Require(new FileInfo(backup).Length<=128*1024*1024,"Original backup size exceeds bounds.");byte[] before=File.ReadAllBytes(backup);Require(Engine.Hash(before)==(string)values["OriginalHash"]&&Engine.Hash(before)==(string)sourceHashes[driver],"Original backup differs.");int mib=Convert.ToInt32(values["MiB"]);Require(Engine.Hash(Patch(before,mib))==IsolationControl.FileHash(Path.Combine(local,driver)),"Test bytes differ from the recorded one-value change.");
  var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe),Arguments=driver+" \""+((string)values["Adapter"]).Replace("\"","")+"\""};
  using(var process=Process.Start(start)){try{
   var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var watch=Stopwatch.StartNew();bool timedOut=false,memoryStopped=false;Report(5,"Starting the separate graphics check");while(!process.WaitForExit(200)){process.Refresh();if(process.PrivateMemorySize64>3L*1024*1024*1024){memoryStopped=true;break;}if(watch.ElapsedMilliseconds>=60000){timedOut=true;break;}Report(Math.Min(95,10+(int)(watch.ElapsedMilliseconds/750)),"Waiting for the graphics check (60 second limit)");}
   if(timedOut||memoryStopped){process.Kill();Require(process.WaitForExit(5000),"The owned test process could not be stopped. Do not start another test.");}string report=output.Result+errors.Result;int code=process.ExitCode;
   values["MemoryGuardTriggered"]=memoryStopped;values["Status"]=memoryStopped?"memory-limit-stopped":timedOut?"timeout":code==0?"device-and-trivial-pipeline-verified":"runtime-not-verified";values["RuntimeVerified"]=!timedOut&&!memoryStopped&&code==0;values["ExitCode"]=code;values["Report"]=report;values["CheckedUtc"]=DateTime.UtcNow.ToString("o");
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(full),"device-test.txt"),report);File.WriteAllText(full,json.Serialize(values));Report(100,"Graphics result saved; cache capacity is still unmeasured");return values;
  }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch{}}}
 }
 public static string TrialRoot{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials");}}
 public static Dictionary<string,object> Saved(string receipt){
  string full=Path.GetFullPath(receipt);Require(Path.GetDirectoryName(Path.GetDirectoryName(full)).Equals(Path.GetFullPath(TrialRoot),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Select an owned driver test record.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Test record exceeds bounds.");var r=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(full));Require(Convert.ToInt32(r["Schema"])==1&&(string)r["Kind"]=="Unreviewed isolated driver trial","Unknown driver test record.");Require(Convert.ToBoolean(r["RuntimeVerified"])&&Convert.ToInt32(r["ExitCode"])==0&&!Convert.ToBoolean(r["MemoryGuardTriggered"]),"The saved graphics check did not pass.");Require(((string)r["Receipt"]).Equals(full,StringComparison.OrdinalIgnoreCase),"Test record path differs.");return r;
 }
 static bool SameSource(Dictionary<string,object> a,Dictionary<string,object> b){return new[]{"SourceIdentity","OriginalHash","Driver","AdapterId","ProbeHash"}.All(k=>(string)a[k]==(string)b[k]);}
 public static string FindSuccessful(string probe){
  if(!Directory.Exists(TrialRoot))return null;Engine.CheckPath(TrialRoot);var selected=DriverCatalog.Selected();if(selected==null)return null;string identity=DriverCatalog.Identity(selected);var records=new List<Dictionary<string,object>>();
  foreach(string folder in Directory.EnumerateDirectories(TrialRoot).Take(200)){try{var r=Saved(Path.Combine(folder,"experiment.json"));if((string)r["SourceIdentity"]==identity&&(string)r["AdapterId"]==selected.Id)records.Add(r);}catch(IOException){}catch(ArgumentException){}catch(KeyNotFoundException){}catch(InvalidOperationException){}}
  foreach(var patched in records.Where(r=>Convert.ToInt32(r["MiB"])==1024).OrderByDescending(r=>(string)r["CheckedUtc"])){
   var control=records.FirstOrDefault(r=>Convert.ToInt32(r["MiB"])==512&&SameSource(r,patched));if(control==null)continue;
   try{VerifySuccessful((string)patched["Receipt"],probe,false);return (string)patched["Receipt"];}catch(IOException){}catch(InvalidOperationException){}catch(ArgumentException){}
  }return null;
 }
 public static Dictionary<string,object> VerifySuccessful(string receipt,string probe,bool full){
  var patched=Saved(receipt);Require(Convert.ToInt32(patched["MiB"])==1024,"Choose the successful 1 GiB record.");var selected=DriverCatalog.Selected();Require(selected!=null&&(string)patched["AdapterId"]==selected.Id&&(string)patched["SourceIdentity"]==DriverCatalog.Identity(selected),"The driver changed after this test. Run a new graphics test first.");
  Dictionary<string,object> control=null;foreach(string folder in Directory.EnumerateDirectories(TrialRoot).Take(200)){try{var r=Saved(Path.Combine(folder,"experiment.json"));if(Convert.ToInt32(r["MiB"])==512&&SameSource(r,patched)){control=r;break;}}catch(IOException){}catch(ArgumentException){}catch(KeyNotFoundException){}}
  Require(control!=null,"The successful unchanged test is missing. Run the graphics test again.");
  foreach(var r in new[]{control,patched}){
   string driver=(string)r["Driver"];Require(driver=="igd12um64xe2.dll"||driver=="igd12um64xe3.dll","Unknown test driver.");string source=Path.Combine(selected.Folder,driver),folder=Path.GetDirectoryName((string)r["Receipt"]),exe=Path.Combine(folder,"ArcCache.DriverProbe.exe"),local=exe+".local";
   Require(((string)r["Source"]).Equals(source,StringComparison.OrdinalIgnoreCase)&&((string)r["SourceFolder"]).Equals(selected.Folder,StringComparison.OrdinalIgnoreCase)&&((string)r["Executable"]).Equals(exe,StringComparison.OrdinalIgnoreCase)&&((string)r["PrivateFolder"]).Equals(local,StringComparison.OrdinalIgnoreCase),"Saved test paths differ from this package.");Engine.CheckPath(exe);Require(IsolationControl.FileHash(exe)==IsolationControl.FileHash(probe)&&(string)r["ProbeHash"]==IsolationControl.FileHash(probe),"Run the test again with this app version.");
   string backup=Path.Combine(folder,"backup",driver);Engine.CheckPath(backup);Require(new FileInfo(backup).Length<=128L*1024*1024,"Backup exceeds bounds.");byte[] original=File.ReadAllBytes(backup);Require(Engine.Hash(original)==(string)r["OriginalHash"]&&IsolationControl.FileHash(source)==(string)r["OriginalHash"],"Test source or backup changed.");
   string output=Path.Combine(local,driver);Engine.CheckPath(output);Require(Engine.Hash(Patch(original,Convert.ToInt32(r["MiB"])))==(string)r["PatchedHash"]&&IsolationControl.FileHash(output)==(string)r["PatchedHash"],"Successful test copy changed.");
   var sources=(Dictionary<string,object>)r["Sources"];var files=(Dictionary<string,object>)r["Files"];Require(sources.Count<=128&&files.Count==sources.Count&&sources.ContainsKey(driver)&&sources.ContainsKey("igd12umd64.dll"),"Invalid test dependency list.");
   if(full){Require(Directory.GetFiles(local).Length==files.Count&&Directory.GetDirectories(local).Length==0,"Test folder contents changed.");foreach(var entry in sources){Require(Path.GetFileName(entry.Key)==entry.Key&&entry.Key.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)&&files.ContainsKey(entry.Key),"Invalid dependency name.");string src=Path.Combine(selected.Folder,entry.Key),dst=Path.Combine(local,entry.Key);Engine.CheckPath(src);Engine.CheckPath(dst);Require(IsolationControl.FileHash(src)==(string)entry.Value&&WindowsPlatform.EmbeddedSignature(src).StartsWith("Valid"),"Driver dependency changed or is not trusted: "+entry.Key);string expected=entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase)?(string)r["PatchedHash"]:(string)entry.Value;Require((string)files[entry.Key]==expected&&IsolationControl.FileHash(dst)==expected,"Test dependency changed: "+entry.Key);}}
  }Require(DriverCatalog.Identity(DriverCatalog.Selected())==(string)patched["SourceIdentity"],"Driver changed during verification.");return patched;
 }
 public static object Discard(string receipt,string probe){
  string full=Path.GetFullPath(receipt),root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials")),folder=Path.GetDirectoryName(full);
  Require(Path.GetDirectoryName(folder).Equals(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Only a direct owned test folder may be removed.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Test record exceeds bounds.");
  var values=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(full));Require(Convert.ToInt32(values["Schema"])==1&&(string)values["Kind"]=="Unreviewed isolated driver trial","Unknown test record.");
  string games=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","games");if(Directory.Exists(games))foreach(string game in Directory.EnumerateFiles(games,"steam-*.json").Take(10000)){if(!MatchedSet.IsTrialReceipt(game))continue;var saved=MatchedSet.Read(game);Require(saved.Status=="removed"||saved.OriginalHash!=(string)values["OriginalHash"]||saved.Driver!=(string)values["Driver"],"A game still uses this test. Use default settings for that game before removing these test copies.");}
  string driver=(string)values["Driver"];Require(driver=="igd12um64xe2.dll"||driver=="igd12um64xe3.dll","Invalid driver name.");
  string exe=Path.Combine(folder,"ArcCache.DriverProbe.exe"),local=exe+".local",backup=Path.Combine(folder,"backup",driver);Engine.CheckPath(backup);
  byte[] before=File.ReadAllBytes(backup);Require(Engine.Hash(before)==(string)values["OriginalHash"],"Original backup changed. Removal stopped.");
  foreach(var process in Process.GetProcessesByName("ArcCache.DriverProbe"))using(process){try{Require(!process.MainModule.FileName.Equals(exe,StringComparison.OrdinalIgnoreCase),"Close the separate graphics check first.");}catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("A test process cannot be inspected. Close it before removing tests.");}}
  var sources=(Dictionary<string,object>)values["Sources"];Require(sources.Count<=128,"Test record exceeds bounds.");
  var allowed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);allowed[full]=null;allowed[Path.Combine(folder,"device-test.txt")]=null;allowed[backup]=(string)values["OriginalHash"];allowed[exe]=IsolationControl.FileHash(probe);
  foreach(var entry in sources){Require(Path.GetFileName(entry.Key)==entry.Key,"Invalid dependency name.");allowed[Path.Combine(local,entry.Key)]=entry.Key.Equals(driver,StringComparison.OrdinalIgnoreCase)?Engine.Hash(Patch(before,Convert.ToInt32(values["MiB"]))):(string)entry.Value;}
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
