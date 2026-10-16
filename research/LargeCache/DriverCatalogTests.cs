using System;using System.IO;using ArcCap;
class DriverCatalogTests {
 static int count;static void Check(bool good,string name){if(!good)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static int Main(string[]args){string root=Path.Combine(Path.GetTempPath(),"ArcDriverReview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{
  var package=new AdapterPackage{Adapter="Intel Arc B580",Version="32.0.101.9034",Folder=root};string initial=DriverCatalog.Identity(package);
  Check(initial==DriverCatalog.Identity(package),"unchanged metadata has stable identity");
  File.WriteAllText(Path.Combine(root,"igd12um64xe2.dll"),"test fixture");Check(initial!=DriverCatalog.Identity(package),"new DLL changes package identity without hashing DLL contents");
  string identity=DriverCatalog.Identity(package);package.Version="32.0.101.9999";Check(identity!=DriverCatalog.Identity(package),"registry driver version changes identity");string reason;
  Check(!DriverCatalog.CohortQualified(package,out reason),"unknown new version remains blocked even on B580");
  package.Version=DriverCatalog.KnownVersion;package.Adapter="Intel Arc B390";Check(!DriverCatalog.CohortQualified(package,out reason),"Xe3 hardware cannot inherit Xe2 qualification");
  var builtIn=new AdapterPackage{Id="integrated-fixture",Adapter="Intel built-in graphics",Version=DriverCatalog.KnownVersion,Folder=root};
  var dedicated=new AdapterPackage{Id="dedicated-fixture",Adapter="Intel add-in graphics",Version=DriverCatalog.KnownVersion,Folder=root};
  Check(DriverCatalog.Choose(new[]{dedicated,builtIn},builtIn.Id)==builtIn,"Built-in graphics can be selected without a B580 name preference");
  Check(DriverCatalog.Choose(new[]{dedicated,builtIn},"missing-fixture")==dedicated,"Missing selected chip falls back only to an available chip");
  var loaderOnly=new AdapterPackage{Id="b580-fixture",Adapter="Intel Arc B580",Folder=root,RegistryUserModeDriverNames=new[]{"igd12umd64.dll"}};
  string resolved=DriverCatalog.ResolveCacheDriverName(loaderOnly,new[]{"igd12umd64.dll","igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"},name=>name=="igd12um64xe2.dll");
  Check(resolved=="igd12um64xe2.dll","Loader-only registry entry resolves the unique recognised cache DLL in its active package");
  Check(DriverCatalog.ResolveCacheDriverName(new[]{"igd12umd64.dll"},new[]{"igd12umd64.dll","igd12um64xe2.dll","igd12um64xe3.dll"},name=>name.EndsWith("xe2.dll")||name.EndsWith("xe3.dll"))==null,"Ambiguous cache DLL candidates remain unresolved");
  Func<string,string> known=path=>MatchedSet.Originals[Path.GetFileName(path)];
  Check(DriverCatalog.CohortQualified(builtIn,known,out reason)&&DriverCatalog.CohortQualified(dedicated,known,out reason),"Reviewed bytes qualify independently of built-in/add-in model name");
  Check(!DriverCatalog.CohortQualified(builtIn,path=>"CHANGED",out reason),"Built-in selection cannot override a changed dependency");
  builtIn.Version="unreviewed-version";Check(!DriverCatalog.CohortQualified(builtIn,known,out reason),"Built-in selection cannot override an unknown driver version");
  byte[] original=File.ReadAllBytes(args[0]);Check(DriverCatalog.CandidateOffsets(original).Length==1,"qualified Xe2 constructor is found once in executable section");
  byte[] xe3=File.ReadAllBytes(args[1]);bool reviewedXe3=Engine.Hash(xe3)==DriverCatalog.Xe3Original;Check(DriverCatalog.CandidateOffsets(xe3).Length==1,"Xe3 constructor is found independently at a different offset");
  string candidate=Path.Combine(root,"igd12um64xe3.dll");File.WriteAllBytes(candidate,xe3);var review=DriverCatalog.Review(candidate,false);
  Check(reviewedXe3?review.State=="ReviewedXe3ExperimentalConstructor"&&!review.PatchEnabled&&review.ExperimentalTestEnabled:review.State=="Unqualified"&&!review.PatchEnabled&&!review.ExperimentalTestEnabled,reviewedXe3?"Reviewed Xe3 permits an isolated device test without inheriting a game profile":"Unreviewed Xe3 stays locked while remaining structurally inspectable");
  xe3[0x2000]^=1;File.WriteAllBytes(candidate,xe3);review=DriverCatalog.Review(candidate,false);
  Check(review.CandidateCount==1&&review.StructurallyPatchable&&review.State=="Unqualified"&&!review.PatchEnabled&&!review.ExperimentalTestEnabled,"matching signature in unknown image never enables patching");
  if(reviewedXe3)foreach(int mib in new[]{512,1024,2048,4096}){byte[] source=File.ReadAllBytes(args[1]);byte[] patched=Xe3Experiment.Patch(source,mib);Check(patched.Length==source.Length,"Xe3 test copy keeps image size "+mib);byte[] normalized=(byte[])patched.Clone();Array.Copy(source,0x1900ec,normalized,0x1900ec,24);Check(Engine.Hash(normalized)==DriverCatalog.Xe3Original,"Xe3 patch changes only the reviewed constructor "+mib);}
  else{bool unknownRefused=false;try{Xe3Experiment.Patch(xe3,512);}catch(InvalidDataException){unknownRefused=true;}Check(unknownRefused,"Xe3 test refuses an unreviewed driver despite a matching constructor pattern");}
  bool refused=false;try{Xe3Experiment.Patch(xe3,1024);}catch(InvalidDataException){refused=true;}Check(refused,"Unknown structural match cannot become a patched experiment");
  byte[] malformed=new byte[512];Check(DriverCatalog.CandidateOffsets(malformed).Length==0,"malformed PE has no trusted signature candidate");
  Console.WriteLine("Passed "+count+" driver catalogue checks. Only temporary fixtures were changed.");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}finally{Directory.Delete(root,true);}}
}
