using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;using ArcCap;
class DriverTrialTests {
 static int count;static void Check(bool good,string name){if(!good)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}static void Reject(Action body,string name){try{body();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
 static int Main(string[] args){
  byte[] original=File.ReadAllBytes(args[0]),xe3=File.ReadAllBytes(args[1]);
  Check(Engine.Hash(DriverTrial.Patch(original,1024))==Engine.OneGiB,"Known Xe2 test reproduces the reviewed 1 GiB output");
  Check(Engine.Hash(DriverTrial.Patch(xe3,1024))=="F6F9ABC40702C5EBC29D848E416510C7B0D5D5D61021B3C8D74726F02E81B9F9","Known Xe3 test reproduces the reviewed 1 GiB output");
  byte[] unknown=(byte[])original.Clone();unknown[0x2000]^=1;int offset=DriverCatalog.CandidateOffsets(unknown).Single();byte[] output=DriverTrial.Patch(unknown,1024);
  Check(output.Length==unknown.Length&&BitConverter.ToInt32(output,offset+16)==0x40000000,"Unreviewed candidate produces only a 1 GiB test copy");
  Array.Copy(unknown,offset+16,output,offset+16,4);Check(output.SequenceEqual(unknown),"Every byte outside the selected value is preserved");
  Check(DriverTrial.Patch(unknown,512).SequenceEqual(unknown),"Original control is byte identical");
  Reject(()=>DriverTrial.Patch(unknown,2048),"Unreviewed larger limits cannot be requested");
  var malformed=(byte[])unknown.Clone();malformed[0]=0;Reject(()=>DriverTrial.Patch(malformed,1024),"Malformed image rejected");
  malformed=(byte[])unknown.Clone();malformed[offset+16]^=1;Reject(()=>DriverTrial.Patch(malformed,1024),"Altered constructor rejected");
  // Duplicate inside the same executable section, away from the original signature.
  malformed=(byte[])unknown.Clone();Array.Copy(unknown,offset,malformed,offset+128,36);Reject(()=>DriverTrial.Patch(malformed,1024),"Ambiguous matches rejected");
  Reject(()=>Engine.Inspect(DriverTrial.Patch(unknown,1024)),"A passing byte-level trial never becomes an approved game definition");
  string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","driver-trials");Directory.CreateDirectory(root);string folder=Path.Combine(root,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"backup"));Directory.CreateDirectory(Path.Combine(folder,"ArcCache.DriverProbe.exe.local"));
  try{
   string driver="igd12um64xe2.dll",receipt=Path.Combine(folder,"experiment.json");File.WriteAllBytes(Path.Combine(folder,"backup",driver),unknown);File.WriteAllBytes(Path.Combine(folder,"ArcCache.DriverProbe.exe.local",driver),DriverTrial.Patch(unknown,1024));
   var record=new{Schema=1,Kind="Unreviewed isolated driver trial",Driver=driver,OriginalHash=Engine.Hash(unknown),MiB=1024,Sources=new Dictionary<string,string>{{driver,Engine.Hash(unknown)}}};File.WriteAllText(receipt,new JavaScriptSerializer().Serialize(record));
   File.WriteAllText(Path.Combine(folder,"extra.txt"),"preserve me");Reject(()=>DriverTrial.Discard(receipt,args[2]),"Extra test files stop cleanup");Check(Directory.Exists(folder),"Failed cleanup preserves the folder");File.Delete(Path.Combine(folder,"extra.txt"));
   string privateFile=Path.Combine(folder,"ArcCache.DriverProbe.exe.local",driver);File.WriteAllText(privateFile,"changed");Reject(()=>DriverTrial.Discard(receipt,args[2]),"Changed test driver stops cleanup");File.WriteAllBytes(privateFile,DriverTrial.Patch(unknown,1024));
   DriverTrial.Discard(receipt,args[2]);Check(!Directory.Exists(folder),"Only the verified owned test folder is removed");
   Reject(()=>DriverTrial.Discard(Path.Combine(Path.GetTempPath(),"experiment.json"),args[2]),"Cleanup cannot target outside the owned test root");
  }finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
  string gameRoot=Path.Combine(Path.GetTempPath(),"ArcGameTrialFixture-"+Guid.NewGuid());Directory.CreateDirectory(gameRoot);
  try{
   string exe=Path.Combine(gameRoot,"fixture.exe"),receiptPath=Path.Combine(gameRoot,"game.json"),local=exe+".local",source=Path.Combine(gameRoot,"not-a-test.json");File.WriteAllText(exe,"fixture");Directory.CreateDirectory(local);
   string driver="igd12um64xe2.dll",originalHash=Engine.Hash(unknown),patchedHash=Engine.Hash(DriverTrial.Patch(unknown,1024)),loaderHash=Engine.Hash(new byte[]{1,2,3});
   var profile=new MatchedSet.ProfileInfo{Schema=2,AppId=999999,Name="fixture",Root=gameRoot,RelativeExecutable="fixture.exe",Hash=IsolationControl.FileHash(exe),ObservedDriverHash=originalHash,TrialReceipt=source};
   var record=new MatchedSetReceipt{Schema=5,Trial=true,Executable=exe,Folder=local,MiB=1024,Status="changed-runtime-unverified",Game=profile,TrialReceipt=source,Driver=driver,OriginalHash=originalHash,TrialPatchedHash=patchedHash,OriginalFiles=new Dictionary<string,string>{{driver,originalHash},{"igd12umd64.dll",loaderHash}},Files=new Dictionary<string,string>{{driver,patchedHash},{"igd12umd64.dll",loaderHash}}};
   MatchedSet.ValidateReceipt(record);Check(true,"Explicit game-trial record preserves its original and patched source hashes");
   record.MiB=4096;Reject(()=>MatchedSet.ValidateReceipt(record),"Unreviewed game trials cannot request larger limits");record.MiB=1024;
   record.Files[driver]="CHANGED";Reject(()=>MatchedSet.ValidateReceipt(record),"Changed patched hash rejected");record.Files[driver]=patchedHash;
   record.OriginalFiles["..\\escape.dll"]=loaderHash;Reject(()=>MatchedSet.ValidateReceipt(record),"Trial dependency cannot escape its game folder");record.OriginalFiles.Remove("..\\escape.dll");
   record.Trial=false;Reject(()=>MatchedSet.ValidateReceipt(record),"Trial record cannot pretend to be regular support");record.Trial=true;
   Reject(()=>DriverTrial.Saved(source),"Successful proof cannot be read from an arbitrary path");
   File.WriteAllBytes(Path.Combine(local,driver),DriverTrial.Patch(unknown,1024));File.WriteAllBytes(Path.Combine(local,"igd12umd64.dll"),new byte[]{1,2,3});File.WriteAllText(receiptPath,new JavaScriptSerializer().Serialize(record));
   File.WriteAllText(Path.Combine(local,"extra.txt"),"keep");Reject(()=>MatchedSet.Remove(receiptPath),"Extra game files stop rollback");File.Delete(Path.Combine(local,"extra.txt"));
   MatchedSet.Remove(receiptPath);Check(!Directory.Exists(local)&&File.Exists(exe),"Game-trial rollback removes only recorded copies without requiring the old test proof");
  }finally{Directory.Delete(gameRoot,true);}
  Console.WriteLine("Passed "+count+" driver-trial checks. No runtime driver was loaded.");return 0;
 }
}
