using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Diagnostics;using Microsoft.Win32;using System.Runtime.InteropServices;using System.Text;
namespace ArcCap {
public class AdapterPackage {public string Id,Adapter,Version,Folder,UserModeDriverName;public string[] RegistryUserModeDriverNames;}
public class DriverFileReview {public string Name,Version,Hash,Signature,State,Error;public long Size;public int CandidateCount;public int[] CandidateOffsets;public bool PatchEnabled,StructurallyPatchable,ExperimentalTestEnabled;public string Assessment;}
public class DriverLimitReview {public int MiB;public bool Available,Buildable,Tested;public string Evidence;}
public static class DriverCatalog {
 public const string KnownVersion="32.0.101.9034",Xe3Original="029E091F5B67A7418B39900E2AC4EAC9EF64767595E0138FF6569CC42FEB4911";
 // Cache adapter-family resolution by file metadata. Status asks Selected() several
 // times, so unchanged driver binaries should only be scanned on the first lookup.
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> CacheDriverResolution=new System.Collections.Concurrent.ConcurrentDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 public static bool PrefersXe1(string adapter){return System.Text.RegularExpressions.Regex.IsMatch(adapter??"",@"\bA\d{2,3}(?:M)?\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);}
 public static readonly string[] Names={"igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"};
 public static bool IsCacheDriverName(string name){return Names.Contains(Path.GetFileName(name??""),StringComparer.OrdinalIgnoreCase);}
 [StructLayout(LayoutKind.Sequential)]struct DeviceInfo {public uint Size;public Guid ClassGuid;public uint DevInst;public UIntPtr Reserved;}
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr window,uint flags);
 [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref DeviceInfo info);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set,ref DeviceInfo info,uint property,out uint type,byte[] buffer,uint size,out uint required);
 [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
 [DllImport("cfgmgr32.dll")]static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref DeviceInfo info,StringBuilder value,uint size,out uint required);
 static Dictionary<string,string> ActiveClassKeys(){
  Guid guid=new Guid("4d36e968-e325-11ce-bfc1-08002be10318");IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,2);
  if(set==new IntPtr(-1))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());var keys=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  try{for(uint i=0;;i++){var info=new DeviceInfo{Size=(uint)Marshal.SizeOf(typeof(DeviceInfo))};if(!SetupDiEnumDeviceInfo(set,i,ref info)){if(Marshal.GetLastWin32Error()==259)break;throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
   uint status,problem;if(CM_Get_DevNode_Status(out status,out problem,info.DevInst,0)!=0||(status&8)==0)continue;
   uint type,required;byte[] value=new byte[1024];if(!SetupDiGetDeviceRegistryProperty(set,ref info,9,out type,value,(uint)value.Length,out required)||type!=1||required>value.Length)continue;
   string name=Encoding.Unicode.GetString(value,0,(int)required).TrimEnd('\0');string prefix="{"+guid.ToString()+"}\\";
   if(name.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){string sub=name.Substring(prefix.Length);if(sub.Length==4&&sub.All(Char.IsDigit)){var instance=new StringBuilder(1024);uint length;if(SetupDiGetDeviceInstanceId(set,ref info,instance,(uint)instance.Capacity,out length))keys[sub]=instance.ToString();}}
  }}finally{SetupDiDestroyDeviceInfoList(set);}return keys;
 }
 public static List<AdapterPackage> Packages(){
  var active=ActiveClassKeys();var result=new List<AdapterPackage>();const string root=@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
  using(var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))using(var display=hive.OpenSubKey(root)){
   if(display==null)return result;
   foreach(string sub in display.GetSubKeyNames().Where(n=>active.ContainsKey(n)))using(var key=display.OpenSubKey(sub)){
    string adapter=key.GetValue("DriverDesc") as string,provider=key.GetValue("ProviderName") as string;
    if(((adapter??"")+" "+(provider??"")).IndexOf("Intel",StringComparison.OrdinalIgnoreCase)<0)continue;
    object raw=key.GetValue("UserModeDriverName");string[] names=raw as string[]??(raw is string?new[]{(string)raw}:new string[0]);
    string packagePath=names.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(path=>Path.IsPathRooted(path)&&(Path.GetFileName(path).Equals("igd12umd64.dll",StringComparison.OrdinalIgnoreCase)||Names.Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase)));
    if(packagePath==null)continue;string folder=Path.GetDirectoryName(Path.GetFullPath(packagePath));try{Engine.CheckPath(folder);}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
    var registryNames=names.Select(n=>Path.GetFileName(Environment.ExpandEnvironmentVariables(n))).Where(n=>!String.IsNullOrWhiteSpace(n)).ToArray();
    string[] packageFiles=Names.Where(n=>File.Exists(Path.Combine(folder,n))).ToArray();
    string cacheName=ResolveCacheDriverNameCached(adapter,active[sub],folder,registryNames,packageFiles);
    result.Add(new AdapterPackage{Id=active[sub],Adapter=adapter??"Intel GPU",Version=key.GetValue("DriverVersion") as string,Folder=folder,UserModeDriverName=cacheName,RegistryUserModeDriverNames=registryNames});
   }
  }return result;
 }
 // UserModeDriverName often lists only the DX12 loader. In that case, infer the
 // cache DLL only when one file in this active adapter package has a unique,
 // structurally buildable cache-cap signature. Multiple matches remain unresolved.
 public static string ResolveCacheDriverName(IEnumerable<string> registeredNames,IEnumerable<string> packageFiles,Func<string,bool> recognized){
  if(recognized==null)return null;var files=new HashSet<string>((packageFiles??Enumerable.Empty<string>()).Where(n=>!String.IsNullOrWhiteSpace(n)).Select(Path.GetFileName),StringComparer.OrdinalIgnoreCase);
  var listed=(registeredNames??Enumerable.Empty<string>()).Select(Path.GetFileName).Where(n=>Names.Contains(n,StringComparer.OrdinalIgnoreCase)&&files.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  if(listed.Length==1)return listed[0];
  var candidates=(listed.Length>1?listed:files.Where(n=>Names.Contains(n,StringComparer.OrdinalIgnoreCase))).Where(n=>{try{return recognized(n);}catch{return false;}}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  return candidates.Length==1?candidates[0]:null;
 }
 public static string ResolveCacheDriverName(AdapterPackage package,IEnumerable<string> packageFiles,Func<string,bool> recognized){return package==null?null:ResolveCacheDriverName(package.RegistryUserModeDriverNames,packageFiles,recognized);}
 static string ResolveCacheDriverNameCached(string adapter,string instanceId,string folder,string[] registryNames,string[] packageFiles){
  var fields=new List<string>{folder??"",adapter??"",instanceId??"",String.Join(",",registryNames??new string[0])};
  foreach(string name in (packageFiles??new string[0]).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase)){var info=new FileInfo(Path.Combine(folder,name));info.Refresh();fields.Add(name+":"+(info.Exists?info.Length+":"+info.LastWriteTimeUtc.Ticks:"missing"));}
  string key=String.Join("|",fields);string cached;if(CacheDriverResolution.TryGetValue(key,out cached))return cached=="-"?null:cached;
  string result=RegisteredCacheDriverName(registryNames,packageFiles);
  if(result==null)result=ResolveCacheDriverNameForAdapter(adapter,instanceId,packageFiles,name=>RecognizedCacheFile(folder,name));
  CacheDriverResolution[key]=result??"-";return result;
 }
 static string RegisteredCacheDriverName(IEnumerable<string> registeredNames,IEnumerable<string> packageFiles){
  var files=new HashSet<string>((packageFiles??Enumerable.Empty<string>()).Where(n=>!String.IsNullOrWhiteSpace(n)).Select(Path.GetFileName),StringComparer.OrdinalIgnoreCase);
  var listed=(registeredNames??Enumerable.Empty<string>()).Select(Path.GetFileName).Where(n=>Names.Contains(n,StringComparer.OrdinalIgnoreCase)&&files.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return listed.Length==1?listed[0]:null;
 }
 public static string ResolveCacheDriverNameForAdapter(string adapter,string instanceId,IEnumerable<string> packageFiles,Func<string,bool> recognized){
  if(recognized==null)return null;var files=new HashSet<string>((packageFiles??Enumerable.Empty<string>()).Where(n=>!String.IsNullOrWhiteSpace(n)).Select(Path.GetFileName),StringComparer.OrdinalIgnoreCase);
  // This is only a pre-control hint. A control probe's LoadedCacheDriver is authoritative.
  // Keep unlisted marketing names unresolved rather than guessing a DLL family.
  string name=adapter??"";string preferred=PrefersXe1(name)?"igd12um64xeh.dll":null;
  if(preferred==null&&System.Text.RegularExpressions.Regex.IsMatch(name,@"\bB390\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))preferred="igd12um64xe3.dll";
  if(preferred==null&&System.Text.RegularExpressions.Regex.IsMatch(name,@"\bB\d{3}\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))preferred="igd12um64xe2.dll";
  if(preferred==null&&System.Text.RegularExpressions.Regex.IsMatch(name,@"\b(?:B\s?(?:50|60)|(?:130|140)V)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))preferred="igd12um64xe2.dll";
  if(preferred!=null&&files.Contains(preferred)){try{if(recognized(preferred))return preferred;}catch{}}
  return ResolveCacheDriverName(new string[0],files,recognized);
 }
 static bool RecognizedCacheFile(string folder,string name){
  if(!Names.Contains(name,StringComparer.OrdinalIgnoreCase))return false;string file=Path.Combine(folder,name);if(!File.Exists(file))return false;
  try{byte[] image=File.ReadAllBytes(file);return GenericCachePatch.Discover(image,name,1024).CanBuild;}catch{return false;}
 }
 public static string SelectionPath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","graphics-selection.json");}}
 public static AdapterPackage Choose(IEnumerable<AdapterPackage> packages,string id){var list=packages.ToArray();return list.FirstOrDefault(p=>!String.IsNullOrEmpty(id)&&String.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase))??list.FirstOrDefault();}
 public static AdapterPackage Selected(){string id=null;try{if(File.Exists(SelectionPath)){Engine.CheckPath(SelectionPath);if(new FileInfo(SelectionPath).Length<=8192)id=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(File.ReadAllText(SelectionPath));}}catch(IOException){}catch(ArgumentException){}return Choose(Packages(),id);}
 public static void Select(string id){if(!Packages().Any(p=>String.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("This graphics chip is no longer available. Refresh the list and choose another.");string root=Path.GetDirectoryName(SelectionPath);Directory.CreateDirectory(root);Engine.CheckPath(root);if(File.Exists(SelectionPath))Engine.CheckPath(SelectionPath);File.WriteAllText(SelectionPath,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(id));}

 public static string Identity(AdapterPackage p){
  if(p==null)return "no-active-intel-package";var fields=new List<string>{p.Id,p.Adapter,p.Version,p.Folder};
  foreach(string name in MatchedSet.Originals.Keys.Concat(Names).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase)){
   string file=Path.Combine(p.Folder,name);var info=new FileInfo(file);fields.Add(name+":"+(info.Exists?info.Length+":"+info.LastWriteTimeUtc.Ticks:"missing"));
  }return Engine.Hash(System.Text.Encoding.UTF8.GetBytes(String.Join("\n",fields)));
 }
 public static int[] CandidateOffsets(byte[] bytes){
  var xe1=GenericCachePatch.CandidateOffsets(bytes,Xe1Patch.FileName);if(xe1.Length>0)return xe1;
  byte[] signature=Engine.Hex("48895c2408574883ec2033ff48c741100000002048b80000000001000000893948894108");var found=new List<int>();
  if(bytes.Length<256||bytes[0]!=77||bytes[1]!=90)return found.ToArray();int nt=BitConverter.ToInt32(bytes,0x3c);
  if(nt<0||nt>bytes.Length-26||BitConverter.ToUInt32(bytes,nt)!=0x4550||BitConverter.ToUInt16(bytes,nt+4)!=0x8664)return found.ToArray();
  int optional=BitConverter.ToUInt16(bytes,nt+20);if(optional<112||nt+24+optional>bytes.Length||BitConverter.ToUInt16(bytes,nt+24)!=0x20b)return found.ToArray();
  int table=nt+24+optional,count=BitConverter.ToUInt16(bytes,nt+6);
  if(count>96||table<0||table>bytes.Length-count*40)return found.ToArray();
  for(int section=0;section<count;section++){int q=table+40*section;if((BitConverter.ToUInt32(bytes,q+36)&0x20000000)==0)continue;
   uint start=BitConverter.ToUInt32(bytes,q+20),size=BitConverter.ToUInt32(bytes,q+16);if((ulong)start+size>(ulong)bytes.Length)continue;
   int end=(int)(start+size)-signature.Length;
   for(int i=(int)start;i<=end;i++){if(bytes[i]!=signature[0])continue;int j=1;while(j<signature.Length&&bytes[i+j]==signature[j])j++;if(j==signature.Length){found.Add(i);if(found.Count>=64)return found.ToArray();}}
  }return found.ToArray();
 }
 public static DriverFileReview Review(string file,bool signatures){
  var r=new DriverFileReview{Name=Path.GetFileName(file),State="Missing",CandidateOffsets=new int[0]};
  try{Engine.CheckPath(file);var info=new FileInfo(file);r.Size=info.Length;if(r.Size<256||r.Size>128L*1024*1024)throw new InvalidDataException("DLL size outside review bounds.");
   byte[] bytes=File.ReadAllBytes(file);r.Hash=Engine.Hash(bytes);r.Version=FileVersionInfo.GetVersionInfo(file).FileVersion;r.CandidateOffsets=r.Name.Equals(Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase)?GenericCachePatch.CandidateOffsets(bytes,r.Name):CandidateOffsets(bytes);r.CandidateCount=r.CandidateOffsets.Length;
   if(r.Name.Equals("igd12um64xe2.dll",StringComparison.OrdinalIgnoreCase)){try{var inspection=Engine.Inspect(bytes);r.State=inspection.Hash==Engine.Original?"QualifiedOriginal":"QualifiedModified";r.PatchEnabled=inspection.Hash==Engine.Original;}catch(InvalidDataException){r.State="Unqualified";}}
   else if(r.Name.Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase)&&r.Hash==Xe3Original)r.State="ReviewedXe3ExperimentalConstructor";
   else r.State="Unqualified";
   r.StructurallyPatchable=r.CandidateCount==1;
   r.ExperimentalTestEnabled=r.Name.Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase)&&r.Hash==Xe3Original&&r.CandidateCount==1&&r.CandidateOffsets[0]==0x1900e0||r.Name.Equals(Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase)&&r.CandidateCount==1;
   r.Assessment=r.Name.Equals(Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase)&&r.CandidateCount==1?"This Xe1 file has a unique cache-setting match. Each size still needs its own isolated test.":r.ExperimentalTestEnabled?"This driver file can be tested. Create a separate test copy, then run the graphics check. Games and larger caches are not verified yet.":r.PatchEnabled?"This file matches a supported version. The other files in the installed driver must also match before a game can be set up.":r.CandidateCount==1?"This file looks similar to a supported version, but it still needs review before a test copy can be offered. No files were changed.":r.CandidateCount>1?"The check found more than one possible location to change. This build cannot safely choose one; keep default settings.":"This build does not recognise a supported cache setting in this file. Keep default settings.";
   if(signatures)r.Signature=WindowsPlatform.EmbeddedSignature(file);
  }catch(Exception e){r.Error=e.Message;r.State="Unavailable";r.Assessment="The DLL could not be inspected: "+e.Message;r.ExperimentalTestEnabled=false;r.PatchEnabled=false;}return r;
 }
 public static bool CohortQualified(AdapterPackage p,out string reason){return CohortQualified(p,delegate(string file){Engine.CheckPath(file);return IsolationControl.FileHash(file);},out reason);}
 public static bool CohortQualified(AdapterPackage p,Func<string,string> readHash,out string reason){
  if(p==null){reason="No active Intel package found.";return false;}
  if(p.Version!=KnownVersion){reason="This driver version is not supported yet. Your normal graphics settings are unchanged.";return false;}
  foreach(var entry in MatchedSet.Originals){string file=Path.Combine(p.Folder,entry.Key);try{if(readHash(file)!=entry.Value){reason="Unqualified package file: "+entry.Key;return false;}}catch(Exception e){reason=e.Message;return false;}}
  reason="The driver files match a supported version. Set up a game to check that its own copy works.";return true;
 }
 public static DriverLimitReview[] LimitCapabilities(bool qualified,string testedDriver){
  int[] limits={512,1024,2048,4096,8192,16384};
  return limits.Select(mib=>{bool legacy=qualified&&mib!=4096;bool tested=legacy||mib<=1024&&!String.IsNullOrEmpty(testedDriver);return new DriverLimitReview{MiB=mib,Available=tested,Buildable=legacy||mib<=1024&&!String.IsNullOrEmpty(testedDriver),Tested=tested,Evidence=
   legacy?mib>=8192?"Exact reviewed Xe2 patch and reload code; game capacity remains unmeasured.":"Exact reviewed driver package and patch output.":
   qualified&&mib==4096?"The direct 4 GiB read can request zero bytes. Use a chunked size test first.":
   mib==512&& !String.IsNullOrEmpty(testedDriver)?"Unchanged driver copy passed its isolated load check.":
   mib==1024&& !String.IsNullOrEmpty(testedDriver)?"This exact driver file passed its isolated 1 GiB load check.":
   "No verified patch for this exact driver file."};}).ToArray();
 }
 public static DriverLimitReview[] LimitCapabilities(IDictionary<int,bool> buildable,ISet<int> passed,string largeReason=null){
  int[] limits={512,1024,2048,4096,8192,16384};return limits.Select(mib=>{
   bool can=mib==512||buildable!=null&&buildable.ContainsKey(mib)&&buildable[mib],tested=passed!=null&&passed.Contains(mib);
   string evidence=tested?mib>=2048?"Passed its own test on this exact driver. Driver copy loaded; cache capacity not measured.":mib==512?"Unchanged driver copy passed on this exact driver.":"Passed its own test on this exact driver.":
    can?"Needs its own test on this exact driver.":mib>=8192&&!String.IsNullOrEmpty(largeReason)?largeReason:mib==512?"Unchanged control has not passed on this driver.":"This driver file's cache code wasn't recognised.";
   return new DriverLimitReview{MiB=mib,Available=tested,Buildable=can,Tested=tested,Evidence=evidence};
  }).ToArray();
 }
}
}
