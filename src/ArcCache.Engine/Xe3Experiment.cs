using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using System.Web.Script.Serialization;
namespace ArcCap {
public static class Xe3Experiment {
 public const string OneGiB="F6F9ABC40702C5EBC29D848E416510C7B0D5D5D61021B3C8D74726F02E81B9F9";
 public const string TwoGiB="A9ADC9BEB2C9B3C4A0CADA7C057DA16F3EF866C0BDE0B1CA44F965C79FC5E686",FourGiB="C9B043204D7BA788B3E3C1C6FAD1BF048CAB548499BB79F3CBBA616E33224A8D";
 static void Require(bool yes,string message){if(!yes)throw new InvalidDataException(message);}
 public static byte[] Patch(byte[] original,int mib) {
  Require(original.Length==42191848&&Engine.Hash(original)==DriverCatalog.Xe3Original,"No reviewed Xe3 patch definition for these bytes. Unknown images can be scanned but cannot be patched.");
  int[] offsets=DriverCatalog.CandidateOffsets(original);Require(offsets.Length==1&&offsets[0]==0x1900e0,"Reviewed Xe3 constructor mapping differs.");
  Require(new[]{512,1024,2048,4096}.Contains(mib),"Xe3 experiments support only original 512 MiB and 1/2/4 GiB. Larger reload code is Xe2-specific.");
  byte[] output=(byte[])original.Clone();
  if(mib==1024)Array.Copy(BitConverter.GetBytes(0x40000000),0,output,offsets[0]+16,4);
  else if(mib>=2048)Array.Copy(Engine.Hex(mib==2048?"48B800000000010000004889410848D1E848894110893990":"48B800000000010000004889410848894110893990909090"),0,output,offsets[0]+12,24);
  byte[] normalized=(byte[])output.Clone();Array.Copy(original,offsets[0]+12,normalized,offsets[0]+12,24);
  Require(normalized.SequenceEqual(original),"Unexpected changes outside the Xe3 constructor.");
  string expected=mib==512?DriverCatalog.Xe3Original:mib==1024?OneGiB:mib==2048?TwoGiB:FourGiB;
  Require(Engine.Hash(output)==expected,"Xe3 full-output hash differs from the reviewed patch.");return output;
 }
 public static bool Amd64Dll(string file) {
  using(var r=new BinaryReader(File.OpenRead(file))){if(r.BaseStream.Length<64||r.ReadUInt16()!=0x5a4d)return false;r.BaseStream.Position=60;uint nt=r.ReadUInt32();if(nt>r.BaseStream.Length-26)return false;r.BaseStream.Position=nt;if(r.ReadUInt32()!=0x4550||r.ReadUInt16()!=0x8664)return false;r.BaseStream.Position=nt+22;return (r.ReadUInt16()&0x2000)!=0;}
 }
 // Only this isolated probe's private directory is created. No game/system install path is accepted.
 public static object Create(string file,int mib,string probe) {
  if(IsolationControl.Administrator)throw new InvalidOperationException("Run the Xe3 experiment normally, not as administrator. No elevation is required.");
  string full=Path.GetFullPath(file);Engine.CheckPath(full);Require(Path.GetFileName(full).Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase),"Select igd12um64xe3.dll.");
  Require(new FileInfo(full).Length==42191848,"No reviewed Xe3 definition for this file size.");byte[] original=File.ReadAllBytes(full),patched=Patch(original,mib);string folder=Path.GetDirectoryName(full);
  var package=DriverCatalog.Packages().FirstOrDefault(p=>p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));Require(package!=null,"Device-test dependencies must come from a currently active Intel driver package. Offline files may be scanned, but cannot be used for this device test.");string identityBefore=DriverCatalog.Identity(package);
  Engine.CheckPath(probe);Require(File.Exists(probe)&&Amd64Exe(probe),"This build does not include the compiled x64 Xe3 device probe.");
  var sources=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);long total=0;
  var candidates=Directory.EnumerateFiles(folder,"*.dll").Take(257).ToArray();Require(candidates.Length<=256,"Driver cohort exceeds experiment bounds.");
  foreach(string source in candidates) {
   Engine.CheckPath(source);Require(sources.Count<128,"Driver cohort exceeds experiment bounds.");if(!Amd64Dll(source))continue;
   total=checked(total+new FileInfo(source).Length);Require(total<=2L*1024*1024*1024,"Driver cohort exceeds the 2 GiB experiment bound.");
   Require(WindowsPlatform.EmbeddedSignature(source).StartsWith("Valid"),"Dependency needs valid offline embedded trust: "+Path.GetFileName(source)+". Catalog-only/unknown dependencies are not automatically accepted.");
   sources.Add(Path.GetFileName(source),IsolationControl.FileHash(source));
  }
  Require(sources.ContainsKey("igd12um64xe3.dll")&&sources.ContainsKey("igd12umd64.dll"),"The active package is missing required Xe3/loader files.");
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-tests");Directory.CreateDirectory(root);Engine.CheckPath(root);
  string target=Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);
  string exe=Path.Combine(target,"ArcCache.Xe3Probe.exe"),local=exe+".local";Directory.CreateDirectory(local);Directory.CreateDirectory(Path.Combine(target,"backup"));
  File.WriteAllBytes(Path.Combine(target,"backup","igd12um64xe3.dll"),original);var security=Engine.Security(full);
  var result=new Dictionary<string,object>{{"Schema",1},{"Kind","Xe3 isolated device experiment"},{"Source",full},{"SourceFolder",folder},{"SourceIdentity",identityBefore},{"MiB",mib},{"OriginalHash",DriverCatalog.Xe3Original},{"PatchedHash",Engine.Hash(patched)},{"OriginalAcl",security},{"Sources",sources},{"PrivateFolder",local},{"RuntimeVerified",false},{"Status","preparing"}};
  string receipt=Path.Combine(target,"experiment.json");var json=new JavaScriptSerializer();File.WriteAllText(receipt,json.Serialize(result));
  foreach(var source in sources){string src=Path.Combine(folder,source.Key),dst=Path.Combine(local,source.Key);Require(IsolationControl.FileHash(src)==source.Value,"Source changed during preparation: "+source.Key);File.Copy(src,dst);Require(IsolationControl.FileHash(dst)==source.Value,"Copy hash mismatch: "+source.Key);}
  Require(Engine.Hash(File.ReadAllBytes(Path.Combine(local,"igd12um64xe3.dll")))==DriverCatalog.Xe3Original,"Copied Xe3 original differs.");
  File.WriteAllBytes(Path.Combine(local,"igd12um64xe3.dll"),patched);Require(Engine.Hash(File.ReadAllBytes(Path.Combine(local,"igd12um64xe3.dll")))==Engine.Hash(patched),"Patched private copy verification failed.");
  Require(DriverCatalog.Packages().Any(p=>p.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase)&&DriverCatalog.Identity(p)==identityBefore),"Active package changed during preparation. Do not run this bundle.");
  string probeHash=IsolationControl.FileHash(probe);File.Copy(probe,exe);Require(IsolationControl.FileHash(exe)==probeHash,"Probe copy differs.");
  var outputs=new Dictionary<string,string>(sources,StringComparer.OrdinalIgnoreCase);outputs["igd12um64xe3.dll"]=Engine.Hash(patched);result["Files"]=outputs;result["ProbeHash"]=probeHash;result["Status"]="prepared-no-runtime-test";result["Receipt"]=receipt;result["Executable"]=exe;
  File.WriteAllText(receipt,json.Serialize(result));return result;
 }
 public static object Run(string receipt,string shippedProbe) {
  if(IsolationControl.Administrator)throw new InvalidOperationException("Run this experiment without administrator rights.");
  string full=Path.GetFullPath(receipt),root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-tests"));
  Require(full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full)=="experiment.json","Only an owned isolated experiment receipt may be run.");Engine.CheckPath(full);Require(new FileInfo(full).Length<=1048576,"Experiment receipt exceeds bounds.");
  var json=new JavaScriptSerializer();var values=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(full));
  Require(Convert.ToInt32(values["Schema"])==1&&(string)values["Kind"]=="Xe3 isolated device experiment","Unknown experiment schema.");
  string exe=Path.Combine(Path.GetDirectoryName(full),"ArcCache.Xe3Probe.exe"),local=exe+".local";
  Require((string)values["Executable"]==exe&&(string)values["PrivateFolder"]==local,"Experiment paths differ from owned receipt.");
  Require(WindowsProbeReady(),"Enable Use private drivers and restart Windows before running this device test.");
  Require(IsolationControl.FileHash(exe)==IsolationControl.FileHash(shippedProbe)&&(string)values["ProbeHash"]==IsolationControl.FileHash(shippedProbe),"Owned probe differs from this build.");
  string sourceFolder=(string)values["SourceFolder"];Engine.CheckPath(sourceFolder);var package=DriverCatalog.Packages().FirstOrDefault(p=>p.Folder.Equals(sourceFolder,StringComparison.OrdinalIgnoreCase));Require(package!=null&&DriverCatalog.Identity(package)==(string)values["SourceIdentity"],"The active package changed. Prepare a new bundle; do not reuse this one.");
  var sourceHashes=(Dictionary<string,object>)values["Sources"];
  var files=(Dictionary<string,object>)values["Files"];Require(files.Count<=128&&Directory.GetFiles(local).Length==files.Count&&Directory.GetDirectories(local).Length==0,"Experiment cohort changed.");
  Require(files.Count==sourceHashes.Count,"Experiment source/output membership differs.");
  foreach(var entry in files){Require(Path.GetFileName(entry.Key)==entry.Key,"Invalid experiment file name.");Require(sourceHashes.ContainsKey(entry.Key),"Untracked experiment dependency.");string source=Path.Combine(sourceFolder,entry.Key);Engine.CheckPath(source);Require(IsolationControl.FileHash(source)==(string)sourceHashes[entry.Key],"Source dependency changed: "+entry.Key);if(!entry.Key.Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase))Require((string)entry.Value==(string)sourceHashes[entry.Key],"Private dependency is not the original source: "+entry.Key);string path=Path.Combine(local,entry.Key);Engine.CheckPath(path);Require(IsolationControl.FileHash(path)==(string)entry.Value,"Changed experiment file: "+entry.Key);}
  string backup=Path.Combine(Path.GetDirectoryName(full),"backup","igd12um64xe3.dll");Engine.CheckPath(backup);Require(new FileInfo(backup).Length==42191848,"Original backup size differs.");byte[] before=File.ReadAllBytes(backup);int mib=Convert.ToInt32(values["MiB"]);Require(Engine.Hash(Patch(before,mib))==IsolationControl.FileHash(Path.Combine(local,"igd12um64xe3.dll")),"Experimental Xe3 bytes differ from reviewed patch.");
  var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe)};
  using(var process=Process.Start(start)){
   var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();bool timedOut=!process.WaitForExit(60000);
   if(timedOut){process.Kill();process.WaitForExit();}string report=output.Result+errors.Result;int code=process.ExitCode;
   values["Status"]=timedOut?"timeout":code==0?"device-and-trivial-pipeline-verified":"runtime-not-verified";values["RuntimeVerified"]=!timedOut&&code==0;values["ExitCode"]=code;values["Report"]=report;values["CheckedUtc"]=DateTime.UtcNow.ToString("o");
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(full),"device-test.txt"),report);File.WriteAllText(full,json.Serialize(values));return values;
  }
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
