using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ArcCap {
static class Tests {
    static byte[] original;static int passed;static List<string> report=new List<string>();
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Operation should have been blocked");}
    static void Test(string name,Action body){body();passed++;string line="PASS "+name;report.Add(line);Console.WriteLine(line);}
    // Hashes are reused across runs only for unchanged files, and a scope that saw nothing new leaves the cache file alone.
    static void TestHashCache(){
        string root=Path.Combine(Path.GetTempPath(),"ArcCacheHashCache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string file=Path.Combine(root,"driver.dll"),cache=Path.Combine(root,"hash-cache.json");File.WriteAllText(file,"first");
            string expected=Engine.Hash(File.ReadAllBytes(file)),first;
            Check(expected==BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(file))).Replace("-",""),"CNG and managed SHA-256 differ.");
            using(IsolationControl.BeginPersistentHashCache(cache))first=IsolationControl.FileHash(file);
            Check(first==expected&&File.Exists(cache),"The first read-only hash was not saved.");
            var saved=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,Dictionary<string,object>>>(File.ReadAllText(cache));
            saved[Path.GetFullPath(file)]["Hash"]=new string('A',64);File.WriteAllText(cache,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(saved));DateTime written=File.GetLastWriteTimeUtc(cache);
            using(IsolationControl.BeginPersistentHashCache(cache))Check(IsolationControl.FileHash(file)==new string('A',64),"An unchanged file was hashed again instead of using the saved hash.");
            Check(File.GetLastWriteTimeUtc(cache)==written,"A scope without new hashes rewrote the cache.");
            File.WriteAllText(file,"second");File.SetLastWriteTimeUtc(file,File.GetLastWriteTimeUtc(file).AddSeconds(5));
            using(IsolationControl.BeginPersistentHashCache(cache))Check(IsolationControl.FileHash(file)==Engine.Hash(File.ReadAllBytes(file)),"A changed file used its old saved hash.");
            Check(IsolationControl.FileHash(file)==Engine.Hash(File.ReadAllBytes(file)),"Outside a scope the hash must be computed.");
            File.WriteAllText(cache,"{ not json");using(IsolationControl.BeginPersistentHashCache(cache))Check(IsolationControl.FileHash(file)==Engine.Hash(File.ReadAllBytes(file)),"A damaged cache file must be ignored.");
            Console.WriteLine("PASS hash cache: CNG matches managed SHA-256; unchanged files reuse, changed files and damaged caches rehash.");
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    // Repair removes only what it can prove is the setup's own, and stops on anything else.
    static void TestRepair(){
        string root=Path.Combine(Path.GetTempPath(),"ArcCacheRepair-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string exe=Path.Combine(root,"game.exe"),folder=exe+".local",driver="igd12um64xe2.dll";File.WriteAllText(exe,"game");
            Func<string,string> hash=text=>Engine.Hash(System.Text.Encoding.UTF8.GetBytes(text));
            var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{driver,hash("driver 16 GB")},{"igd12umd64.dll",hash("loader")}};
            Action fill=()=>{Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,driver),"driver 16 GB");File.WriteAllText(Path.Combine(folder,"igd12umd64.dll"),"loader");};
            // an interrupted size change: the real driver is still the receipt's, the staged next size goes
            fill();File.WriteAllText(Path.Combine(folder,driver+".pending"),"driver 2 GB");var r=new MatchedSet.RepairResult();
            MatchedSet.RepairFolder(folder,files,driver,false,r);Check(r.StatusReset&&!File.Exists(Path.Combine(folder,driver+".pending"))&&Directory.GetFiles(folder).Length==2,"The staged size change was not removed cleanly.");
            // the driver was already replaced: unclear which file is right, so nothing is removed
            File.WriteAllText(Path.Combine(folder,driver+".pending"),"x");File.WriteAllText(Path.Combine(folder,driver),"driver 2 GB");
            Reject(()=>MatchedSet.RepairFolder(folder,files,driver,false,new MatchedSet.RepairResult()));Check(File.Exists(Path.Combine(folder,driver+".pending")),"A staging file was removed although the driver had changed.");
            Directory.Delete(folder,true);
            // a removal that stopped half way is finished; a foreign file stops it untouched
            fill();File.Delete(Path.Combine(folder,"igd12umd64.dll"));r=new MatchedSet.RepairResult();MatchedSet.RepairFolder(folder,files,driver,true,r);Check(r.Removed&&!Directory.Exists(folder),"The interrupted removal was not finished.");
            fill();File.WriteAllText(Path.Combine(folder,"mod.dll"),"not ours");Reject(()=>MatchedSet.RepairFolder(folder,files,driver,true,new MatchedSet.RepairResult()));Check(Directory.GetFiles(folder).Length==3,"Files were removed next to a foreign file.");
            Directory.Delete(folder,true);
            // a failed Prepare's staging folder: removed when every file is the receipt's, kept otherwise
            string staging=folder+".pending-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,driver),"driver 16 GB");
            r=new MatchedSet.RepairResult();MatchedSet.RepairPrepareStaging(exe,files,r);Check(!Directory.Exists(staging)&&r.Done.Count==1,"The unfinished setup folder was not removed.");
            Directory.CreateDirectory(staging);File.WriteAllText(Path.Combine(staging,driver),"changed");Reject(()=>MatchedSet.RepairPrepareStaging(exe,files,new MatchedSet.RepairResult()));Check(File.Exists(Path.Combine(staging,driver)),"A changed staging file was removed.");
            string other=Path.Combine(root,"game.exe.local.pending-notaguid");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"keep.txt"),"x");Directory.Delete(staging,true);
            MatchedSet.RepairPrepareStaging(exe,files,new MatchedSet.RepairResult());Check(File.Exists(Path.Combine(other,"keep.txt")),"A folder with a foreign name was touched.");
            Console.WriteLine("PASS repair: removes only the setup's own leftovers and stops on foreign or ambiguous files.");
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    static void TestCapabilities(){
        var reviewed=DriverCatalog.LimitCapabilities(true,null);Check(reviewed.Where(x=>x.Available).Select(x=>x.MiB).SequenceEqual(new[]{512,1024,2048,8192,16384})&&reviewed.Single(x=>x.MiB==4096).Evidence.Contains("zero bytes"),"Reviewed package exposed unsafe direct 4 GiB I/O.");
        var tested=DriverCatalog.LimitCapabilities(false,"igd12um64xe2.dll");Check(tested.Where(x=>x.Available).Select(x=>x.MiB).SequenceEqual(new[]{512,1024}),"One-gigabyte test exposed unverified sizes.");
        var unknown=DriverCatalog.LimitCapabilities(false,null);Check(unknown.All(x=>!x.Available)&&unknown.Any(x=>x.MiB==4096&&x.Evidence.Contains("exact driver file")),"Unknown image exposed patch sizes or lost its reason.");
        Console.WriteLine("PASS per-size driver qualification matrix");
    }
    static void TestLoaderOnlyDriverResolution(){
        var adapter=new AdapterPackage{Id="b580-fixture",Adapter="Intel Arc B580",RegistryUserModeDriverNames=new[]{"igd12umd64.dll"}};
        string found=DriverCatalog.ResolveCacheDriverName(adapter,new[]{"igd12umd64.dll","igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"},name=>name=="igd12um64xe2.dll");
        Check(found=="igd12um64xe2.dll","loader-only registry entry resolves the unique recognised cache DLL from the adapter package");
        found=DriverCatalog.ResolveCacheDriverName(adapter,new[]{"igd12umd64.dll","igd12um64xe2.dll","igd12um64xe3.dll"},name=>name.EndsWith("xe2.dll",StringComparison.OrdinalIgnoreCase)||name.EndsWith("xe3.dll",StringComparison.OrdinalIgnoreCase));
        Check(found==null,"ambiguous cache DLL candidates remain unresolved");
        var scanned=new List<string>();found=DriverCatalog.ResolveCacheDriverNameForAdapter("Intel(R) Arc(TM) B580 Graphics","PCI\\VEN_8086&DEV_E20B",new[]{"igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"},name=>{scanned.Add(name);return true;});
        Check(found=="igd12um64xe2.dll"&&scanned.SequenceEqual(new[]{"igd12um64xe2.dll"}),"known B-series hardware must select Xe2 after scanning only its family DLL");
        found=DriverCatalog.ResolveCacheDriverNameForAdapter("Unmapped Intel GPU","PCI\\VEN_8086&DEV_FFFF",new[]{"igd12um64xe2.dll","igd12um64xe3.dll"},name=>true);
        Check(found==null,"unknown hardware with ambiguous cache DLLs must remain unresolved");
        string[] families={"igd12um64xe2.dll","igd12um64xe3.dll","igd12um64xeh.dll"};
        foreach(string[] row in new[]{new[]{"Intel Arc B390 / B370 GPU","igd12um64xe3.dll"},new[]{"Intel Arc Pro B50 / B60 Graphics","igd12um64xe2.dll"},new[]{"Intel Arc 140V GPU","igd12um64xe2.dll"}}){
          found=DriverCatalog.ResolveCacheDriverNameForAdapter(row[0],"",families,name=>name==row[1]);Check(found==row[1],"pre-control family hint for "+row[0]);
        }
        found=DriverCatalog.ResolveCacheDriverNameForAdapter("Intel Arc Graphics","",families,name=>true);Check(found==null,"generic Arc Graphics stays unresolved until a control probe identifies the loaded module");
        Check(DriverCatalog.IsCacheDriverName("igd12um64xe3.dll")&& !DriverCatalog.IsCacheDriverName("igd12umd64.dll"),"loader-reported cache module names are validated");
        Console.WriteLine("PASS loader-only Intel registry fallback");
    }
    static void Temporary(Action<string,string> body) {
        string dir=Path.Combine(Path.GetTempPath(),"ArcCapTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {string target=Path.Combine(dir,"igd12um64xe2.dll"),folder=Path.Combine(dir,"bundle");File.WriteAllBytes(target,original);Engine.Prepare(target,folder,1024);body(target,folder);}
        finally { // Only this freshly created test directory is removed.
            if(!Path.GetFullPath(dir).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected cleanup path");
            Directory.Delete(dir,true);
        }
    }
    static int Main(string[] args) {
        string isolatedTrialRoot=null;
        try {
            if(args.Length==1&&args[0]=="--isolation-proxy-tests"){
                string root=Path.Combine(Path.GetTempPath(),"ArcCacheIsolationTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                try{string exe=Path.Combine(root,"game.exe");File.WriteAllText(exe,"fixture");foreach(string name in new[]{"d3d12.dll","dxgi.dll"}){string proxy=Path.Combine(root,name);File.WriteAllText(proxy,"fixture proxy");Check(Isolation.LocalDirectXProxy(exe)==proxy,"Failed to identify "+name);try{Isolation.RequireNoLocalD3D12(exe);throw new Exception("Accepted local "+name);}catch(InvalidDataException){}File.Delete(proxy);}string foreign=exe+".local";Directory.CreateDirectory(foreign);Check(Isolation.LocalPrivatePath(exe)==foreign,"Failed to identify foreign .local directory");Console.WriteLine("PASS d3d12.dll, dxgi.dll and foreign .local blockers; no game files accessed.");return 0;}
                finally{if(Directory.Exists(root))Directory.Delete(root,true);}
            }
            if(args.Length==2&&args[0]=="--planner"){
                byte[] image=File.ReadAllBytes(args[1]);string driver=Path.GetFileName(args[1]);foreach(int mib in new[]{1024,2048,4096,8192,16384}){var p=GenericCachePatch.Discover(image,driver,mib);Console.WriteLine(driver+" "+mib+" MiB: "+(p.CanBuild?"BUILDABLE":"LOCKED")+" · "+p.Reason+" · matches cap/reload/helper/helper-call="+p.CapMatchCount+"/"+p.ReloadMatchCount+"/"+p.ReadHelperMatchCount+"/"+p.HelperCallMatchCount);if(p.CanBuild&&mib>=4096){byte[] output=GenericCachePatch.Build(image,mib,driver);Console.WriteLine("  chunked output SHA-256 "+Engine.Hash(output)+" · bytes "+output.Length);}}
                return 0;
            }
            if(args.Length==2&&args[0]=="--xe1-guard"){
                byte[] image=File.ReadAllBytes(args[1]);string driver=Path.GetFileName(args[1]);Check(String.Equals(driver,Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase),"Xe1 guard requires an xeh DLL.");
                var one=GenericCachePatch.Discover(image,driver,1024);Check(one.CanBuild,"Xe1's reviewed 1 GiB path should remain available.");
                foreach(int mib in new[]{2048,4096,8192,16384}){var plan=GenericCachePatch.Discover(image,driver,mib);Check(!plan.CanBuild&&plan.IsXe1Layout,"Xe1 wide size was not locked at "+mib+" MiB.");try{GenericCachePatch.Build(image,mib,driver);throw new Exception("Xe1 wide build unexpectedly succeeded at "+mib+" MiB.");}catch(InvalidDataException){}}
                Console.WriteLine("PASS Xe1 guard on real "+driver+": 1 GiB buildable; 2/4/8/16 GiB refused without modifying source bytes.");return 0;
            }
            if(args.Length==1&&args[0]=="--capabilities"){TestCapabilities();return 0;}
            if(args.Length==1&&args[0]=="--hash-cache"){TestHashCache();return 0;}
            if(args.Length==1&&args[0]=="--repair"){TestRepair();return 0;}
            if(args.Length==1&&args[0]=="--module-paths"){
                string self=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;var paths=WindowsPlatform.ModulePaths(System.Diagnostics.Process.GetCurrentProcess().Id);
                Check(paths.Any(p=>p.Equals(self,StringComparison.OrdinalIgnoreCase))&&paths.Any(p=>Path.GetFileName(p).Equals("kernel32.dll",StringComparison.OrdinalIgnoreCase))&&paths.All(p=>!p.StartsWith(@"\\?\",StringComparison.Ordinal)),"Module paths of this process are incomplete.");
                Console.WriteLine("PASS module paths: "+paths.Count+" modules with full paths.");return 0;
            }
            if(args.Length==1&&args[0]=="--driver-resolution"){TestLoaderOnlyDriverResolution();return 0;}
            if(args.Length==2&&args[0]=="--validate-record"){
                var receipt=MatchedSet.Read(args[1]);MatchedSet.ValidateContents(receipt);
                Console.WriteLine("PASS live game receipt and private files validate READ ONLY ("+receipt.Status+").");return 0;
            }
            if(args.Length==1&&args[0]=="--test-historical-trial-receipt"){
                string root=Path.Combine(Path.GetTempPath(),"ArcCacheHistoricalTrialFixture"),exe=Path.Combine(root,"game.exe"),originalHash=new string('A',64),patchedHash=new string('B',64),loaderHash=new string('C',64);
                var profile=new MatchedSet.ProfileInfo{Schema=2,AppId=999999,Name="fixture",Root=root,RelativeExecutable="game.exe",Hash=new string('D',64),ObservedDriverHash=originalHash,TrialReceipt=Path.Combine(root,"driver-trials","16-gb","experiment.json")};
                var controlProof=new Dictionary<string,object>{{"MiB",512},{"OriginalHash",originalHash},{"SourceFolder",Path.Combine(root,"driver-package")}};
                MatchedSet.ValidatePrepareObservation(profile,controlProof);Console.WriteLine("PASS Prepare accepts the 512 MiB control for the observed driver when the profile records a 16 GiB test.");
                var receipt=new MatchedSetReceipt{Schema=5,Trial=true,Executable=exe,Folder=exe+".local",MiB=1024,Status="changed-runtime-unverified",Game=profile,TrialReceipt=Path.Combine(root,"new-size","experiment.json"),TrialMiB=1024,Driver="igd12um64xe2.dll",OriginalHash=originalHash,TrialPatchedHash=patchedHash,OriginalFiles=new Dictionary<string,string>{{"igd12um64xe2.dll",originalHash},{"igd12umd64.dll",loaderHash}},Files=new Dictionary<string,string>{{"igd12um64xe2.dll",patchedHash},{"igd12umd64.dll",loaderHash}}};
                MatchedSet.ValidateReceipt(receipt);Console.WriteLine("PASS game observation and current size proof may be different tests of the same original DLL.");
                profile.ObservedDriverHash=patchedHash;try{MatchedSet.ValidateReceipt(receipt);throw new Exception("A game observation from another original driver was accepted.");}catch(InvalidDataException){Console.WriteLine("PASS mismatched original driver hash remains rejected.");}
                return 0;
            }
            if(args.Length==2&&args[0]=="--inspect-serialize"){
                var inspection=DriverTrial.Inspect(args[1]);string json=new JavaScriptSerializer().Serialize(inspection);var parsed=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);var sizes=(Dictionary<string,object>)parsed["Buildable"];
                Check(sizes.ContainsKey("512")&&sizes.ContainsKey("1024"),"Serialized real inspection omitted string-keyed size results.");Console.WriteLine("PASS JavaScriptSerializer serialized real DriverTrial.Inspect report for "+Path.GetFileName(args[1])+" ("+sizes.Count+" sizes).");return 0;
            }
            if(args.Length<1){Console.Error.WriteLine("Pass the supported original DLL path, and optionally a report path.");return 2;}
            isolatedTrialRoot=Path.Combine(Path.GetTempPath(),"ArcCacheEngineTrialTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(isolatedTrialRoot);DriverTrial.TrialRootOverride=isolatedTrialRoot;
            original=Engine.Read(args[0]);Check(Engine.Hash(original)==Engine.Original,"Original fixture hash differs");
            Test("known original and 1 GiB hash, exact changed byte",()=>{
                Check(Engine.Inspect(original).MiB==512,"Original cap");var after=Engine.Patch(original,1024);Check(Engine.Hash(after)==Engine.OneGiB,"Output hash");
                var changed=Enumerable.Range(0,original.Length).Where(i=>original[i]!=after[i]).ToArray();Check(changed.SequenceEqual(new[]{0x18BC63}),"Unexpected byte diff");
            });
            Test("size load observation requires exact current DLL hash and a later change clears it",()=>{
                string root=Path.Combine(Path.GetTempPath(),"ArcCapLoadReceipt-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                try{
                    string exe=Path.Combine(root,"game.exe"),folder=exe+".local",receiptPath=Path.Combine(root,"game.json"),proof=Path.Combine(root,"proof.json");File.WriteAllText(exe,"fixture");Directory.CreateDirectory(folder);
                    byte[] driver=Engine.Patch(original,1024),loader=new byte[]{1,2,3,4,5};string loaderHash=Engine.Hash(loader);
                    File.WriteAllBytes(Path.Combine(folder,"igd12um64xe2.dll"),driver);File.WriteAllBytes(Path.Combine(folder,"igd12umd64.dll"),loader);
                    var originalFiles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"igd12um64xe2.dll",Engine.Original},{"igd12umd64.dll",loaderHash}};
                    var game=new MatchedSet.ProfileInfo{Schema=2,AppId=123456,Name="fixture",Root=root,RelativeExecutable="game.exe",Hash=new string('D',64),ObservedDriverHash=Engine.Original,TrialReceipt=proof};
                    var receipt=new MatchedSetReceipt{Schema=5,Trial=true,Executable=exe,Folder=folder,MiB=1024,Status="changed-runtime-unverified",Game=game,TrialReceipt=proof,TrialMiB=1024,Driver="igd12um64xe2.dll",OriginalHash=Engine.Original,TrialPatchedHash=Engine.OneGiB,OriginalFiles=originalFiles,Files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"igd12um64xe2.dll",Engine.OneGiB},{"igd12umd64.dll",loaderHash}}};
                    File.WriteAllText(receiptPath,new JavaScriptSerializer().Serialize(receipt));
                    Reject(()=>MatchedSet.RecordSizeLoading(receiptPath,new string('0',64)));
                    Check(MatchedSet.Read(receiptPath).Status=="changed-runtime-unverified","A wrong loaded-file hash changed the saved receipt.");
                    MatchedSet.RecordSizeLoading(receiptPath,Engine.OneGiB);Check(MatchedSet.Read(receiptPath).Status=="size-loading-verified","A matching current-size driver hash was not persisted.");
                    var changed=MatchedSet.Read(receiptPath);MatchedSet.MarkChangedRuntimeUnverified(changed);Check(changed.Status=="changed-runtime-unverified","Changing cache size did not clear the previous runtime verification.");
                }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
            });
            Test("full reset preflight stops on a foreign game file before restoring Windows",()=>{
                string root=Path.Combine(Path.GetTempPath(),"ArcResetForeign-"+Guid.NewGuid().ToString("N"));string games=Path.Combine(root,"games"),exe=Path.Combine(root,"Game","game.exe"),folder=exe+".local";Directory.CreateDirectory(games);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(games,"game.json"),"fixture");File.WriteAllText(Path.Combine(folder,"foreign.dll"),"keep");string logDir=Path.Combine(root,"logs");Directory.CreateDirectory(logDir);string log=Path.Combine(logDir,"session.log");File.WriteAllText(log,"keep");int restoreCalls=0;
                var receipt=new MatchedSetReceipt{Executable=exe,Folder=folder};var hooks=new ArcCacheMaintenanceHooks{ReadReceipt=p=>receipt,ValidateContents=r=>{throw new InvalidDataException("foreign file");},RemoveReceipt=p=>{throw new Exception("must not remove during preflight");},IsGameRunning=p=>false,IsAdministrator=()=>true,OverrideEnabled=()=>true,OverrideReceiptExists=()=>true,RestoreOverride=()=>{restoreCalls++;return "restored";}};
                try{var result=ArcCacheMaintenance.Reset(root,log,hooks);Check(!result.Completed&&result.BlockedFolders.Contains(folder)&&restoreCalls==0&&File.Exists(Path.Combine(folder,"foreign.dll"))&&File.Exists(log)&&File.Exists(Path.Combine(games,"game.json")),"Foreign game files must stop reset before Windows restore or app-data deletion and identify the blocked folder.");}finally{Directory.Delete(root,true);}
            });
            Test("full reset keeps only the current session log and reports freed bytes",()=>{
                string root=Path.Combine(Path.GetTempPath(),"ArcResetSuccess-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string logs=Path.Combine(root,"logs");Directory.CreateDirectory(logs);string current=Path.Combine(logs,"current.log"),old=Path.Combine(logs,"old.log");File.WriteAllText(current,"keep this log");File.WriteAllText(old,"remove this log");string trials=Path.Combine(root,"driver-trials");Directory.CreateDirectory(trials);File.WriteAllBytes(Path.Combine(trials,"trial.bin"),new byte[1024]);File.WriteAllText(Path.Combine(root,"recent-games.json"),"[]");var hooks=new ArcCacheMaintenanceHooks{ReadReceipt=p=>null,ValidateContents=r=>{},RemoveReceipt=p=>"",IsGameRunning=p=>false,IsAdministrator=()=>true,OverrideEnabled=()=>false,OverrideReceiptExists=()=>false,RestoreOverride=()=>""};
                try{var result=ArcCacheMaintenance.Reset(root,current,hooks);Check(result.Completed&&result.BytesFreed>=1024&&File.Exists(current)&&!File.Exists(old)&&!Directory.Exists(trials)&&!File.Exists(Path.Combine(root,"recent-games.json"))&&Directory.GetFileSystemEntries(logs).SequenceEqual(new[]{current}),"Reset should delete known data, report freed space, and retain only the active log.");}finally{Directory.Delete(root,true);}
            });
            Test("driver support returns patch sizes for the exact evidence level",()=>{
                var reviewed=DriverCatalog.LimitCapabilities(true,null);Check(reviewed.Where(x=>x.Available).Select(x=>x.MiB).SequenceEqual(new[]{512,1024,2048,8192,16384})&&reviewed.Single(x=>x.MiB==4096).Evidence.Contains("zero bytes"),"Reviewed package exposed unsafe direct 4 GiB I/O.");
                var tested=DriverCatalog.LimitCapabilities(false,"igd12um64xe2.dll");Check(tested.Where(x=>x.Available).Select(x=>x.MiB).SequenceEqual(new[]{512,1024}),"One-gigabyte test exposed unverified sizes.");
                var unknown=DriverCatalog.LimitCapabilities(false,null);Check(unknown.All(x=>!x.Available)&&unknown.Any(x=>x.MiB==4096&&x.Evidence.Contains("exact driver file")),"Unknown image exposed patch sizes or lost the reason.");
            });
            Test("driver inspection report uses string keys accepted by the worker serializer",()=>{
                var inspectionReport=new DriverTrialInspection{Eligible=true,File="driver.dll",Driver="igd12um64xe2.dll",Hash="hash",Version="test",Signature="Valid",CandidateCount=1,Buildable=new Dictionary<string,bool>{{"512",true},{"1024",true},{"2048",false}},Reasons=new Dictionary<string,string>{{"2048","Needs an isolated test."}},Message="Inspection complete."};
                string json=new JavaScriptSerializer().Serialize(inspectionReport);var parsed=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);var buildable=(Dictionary<string,object>)parsed["Buildable"];
                Check(Convert.ToBoolean(buildable["512"])&&!Convert.ToBoolean(buildable["2048"]),"Worker serializer did not preserve per-size results.");
            });
            Test("bounds and cap-only normalization",()=>{
                foreach(int n in new[]{128,512,1024,2047,2048})Check(Engine.Inspect(Engine.Patch(original,n)).MiB==n,"Cap recognition");Reject(()=>Engine.Patch(original,4096));
                foreach(int n in new[]{0,127,2049,4095,4097,8193,32768})Reject(()=>Engine.Patch(original,n));
            });
            Test("wide patches pin full hashes and round-trip to original / 1 GiB",()=>{
                var wide=Engine.Patch(original,2048);Check(Engine.Hash(wide)==Engine.TwoGiB,"Wide hash mismatch");
                Check(Engine.Patch(wide,512).SequenceEqual(original),"Wide rollback differs");Check(Engine.Hash(Engine.Patch(wide,1024))==Engine.OneGiB,"Wide to 1 GiB differs");wide[0x18BC70]^=1;Reject(()=>Engine.Inspect(wide));
            });
            Test("generic 9034 planner reproduces all reviewed hashes",()=>{
                foreach(int mib in new[]{1024,2048}){string expected=mib==1024?Engine.OneGiB:Engine.TwoGiB;Check(Engine.Hash(GenericCachePatch.Build(original,mib))==expected,"Generic in-place output differs at "+mib+" MiB.");}
                var fourPlan=GenericCachePatch.Discover(original,"igd12um64xe2.dll",4096);Check(fourPlan.CanBuild&&fourPlan.NeedsReloadWrapper,"4 GiB was not routed through discovered chunked reload code.");var four=GenericCachePatch.Build(original,4096);Check(GenericCachePatch.Normalize(original,four,4096).SequenceEqual(original),"Chunked 4 GiB output did not normalize.");
                foreach(int mib in new[]{8192,16384}){string expected=mib==8192?LargeCachePatch.EightGiB:LargeCachePatch.SixteenGiB;byte[] generic=GenericCachePatch.Build(original,mib,true);Check(Engine.Hash(generic)==expected,"Generic legacy-layout output differs at "+mib+" MiB.");Check(generic.SequenceEqual(LargeCachePatch.Build(original,mib)),"Generic legacy layout is not byte-identical to the test oracle.");byte[] stripped=GenericCachePatch.Build(original,mib,false);int nt=BitConverter.ToInt32(stripped,0x3c),optional=nt+24;Check(BitConverter.ToUInt32(stripped,optional+112+4*8)==0&&BitConverter.ToUInt32(stripped,optional+112+4*8+4)==0,"Stripped output kept an Authenticode directory.");Check(stripped.Length<generic.Length,"Stripped output retained the certificate overlay.");Check(GenericCachePatch.Normalize(original,stripped,mib).SequenceEqual(original),"Stripped output did not normalize against its receipt backup.");}
            });
            Test("generic planner refuses changed caps, duplicate signatures and overlapping PE sections",()=>{
                byte[] changed=(byte[])original.Clone();changed[0x18BC60]^=1;Check(!GenericCachePatch.Discover(changed,"xe2",1024).CanBuild,"Changed source cap was accepted.");byte[] duplicate=(byte[])original.Clone();Array.Copy(original,0x18BC50,duplicate,0x190000,36);Check(!GenericCachePatch.Discover(duplicate,"xe2",1024).CanBuild,"Duplicate cap site was accepted.");byte[] overlap=(byte[])original.Clone();int nt=BitConverter.ToInt32(overlap,0x3c),table=nt+24+BitConverter.ToUInt16(overlap,nt+20);Array.Copy(overlap,table+20,overlap,table+40+20,4);Check(!GenericCachePatch.Discover(overlap,"xe2",8192).CanBuild,"Overlapping sections were accepted.");
            });
            Test("generic planner refuses duplicate or overlapping RUNTIME_FUNCTION ranges",()=>{
                byte[] bad=(byte[])original.Clone();int nt=BitConverter.ToInt32(bad,0x3c),opt=nt+24,dir=opt+112;uint rva=BitConverter.ToUInt32(bad,dir+3*8);int table=opt+BitConverter.ToUInt16(bad,nt+20),count=BitConverter.ToUInt16(bad,nt+6),raw=-1;for(int i=0;i<count;i++){int h=table+i*40;uint va=BitConverter.ToUInt32(bad,h+12),size=BitConverter.ToUInt32(bad,h+16);if(rva>=va&&rva<va+size){raw=checked((int)(BitConverter.ToUInt32(bad,h+20)+rva-va));break;}}Check(raw>=0,"Exception table was not located in fixture.");Array.Copy(bad,raw,bad,raw+12,12);Check(!GenericCachePatch.Discover(bad,"xe2",4096).CanBuild,"Overlapping runtime-function ranges were accepted.");
            });
            Test("a pass unlocks only its tested size and receipts stay in their driver family",()=>{
                var buildable=new Dictionary<int,bool>{{512,true},{1024,true},{2048,true},{4096,true},{8192,true},{16384,true}};var passedSizes=new HashSet<int>{512,2048};var caps=DriverCatalog.LimitCapabilities(buildable,passedSizes);Check(caps.Where(c=>c.Available).Select(c=>c.MiB).SequenceEqual(new[]{512,2048}),"One size pass unlocked another size.");var a=new Dictionary<string,object>{{"SourceIdentity","package-a"},{"OriginalHash","hash"},{"Driver","igd12um64xe2.dll"},{"AdapterId","gpu"},{"ProbeHash","probe"}};var b=new Dictionary<string,object>(a);b["Driver"]="igd12um64xe3.dll";Check(!DriverTrial.SameSource(a,b),"An XE2 receipt carried across into XE3.");b=new Dictionary<string,object>(a);b["SourceIdentity"]="updated-package";Check(!DriverTrial.SameSource(a,b),"A receipt carried across a driver update.");
            });
            Test("interrupted driver tests are skipped and 4 GiB changes are journaled",()=>{
                string root=Path.Combine(DriverTrial.TrialRoot,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{string receipt=Path.Combine(root,"experiment.json");File.WriteAllText(receipt,new JavaScriptSerializer().Serialize(new{Schema=1,Kind="Unreviewed isolated driver trial",Receipt=receipt,RuntimeVerified=false,ExitCode=0,MemoryGuardTriggered=false}));Check(DriverTrial.ReadCandidate(receipt)==null,"An interrupted preparing receipt was not skipped.");}finally{Directory.Delete(root,true);}
                Check(DriverTrial.RequiresChangeJournal(512,4096)&&DriverTrial.RequiresChangeJournal(4096,512)&&!DriverTrial.RequiresChangeJournal(512,2048),"A 4 GiB layout transition lacks a recovery journal.");
            });
            Test("reload proof requires nonzero bytes and legacy large trials are marked for retest",()=>{
                Check(DriverTrial.HasObservedCacheRead("CacheReadBytes: 4096\r\n")&&!DriverTrial.HasObservedCacheRead("CacheReadBytes: 0\r\n")&&!DriverTrial.HasObservedCacheRead("cache read unavailable"),"Cache reload evidence accepted missing or zero-byte reads.");
                Check(DriverTrial.SingleObservedCacheWrite("CacheFileWritten: c:\\cache\\pipeline.bin\r\n")==@"c:\cache\pipeline.bin","Single cache-file write was not selected.");Check(DriverTrial.SingleObservedCacheWrite("CacheFileWritten: c:\\cache\\a.bin\r\nCacheFileWritten: c:\\cache\\b.bin\r\n")==null,"Ambiguous cache-file writes were accepted.");
                string folder=Path.Combine(DriverTrial.TrialRoot,"legacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);try{string old=Path.Combine(folder,"experiment.json");File.WriteAllText(old,new JavaScriptSerializer().Serialize(new{MiB=16384,Driver="igd12um64xe2.dll",ReloadRunVerified=true}));Check(DriverTrial.LegacyTrialNeedsRetest(old,16384,"igd12um64xe2.dll"),"Legacy trial without exact-file reload evidence was not marked for retest.");File.WriteAllText(old,new JavaScriptSerializer().Serialize(new{MiB=16384,Driver="igd12um64xe2.dll",ReloadRunVerified=true,CacheReloadObserved=true,CacheFileObserved="cache.bin"}));Check(!DriverTrial.LegacyTrialNeedsRetest(old,16384,"igd12um64xe2.dll"),"Reload-verified trial was incorrectly marked stale.");Check(!DriverTrial.LegacyTrialNeedsRetest(old,8192,"igd12um64xe2.dll")&&!DriverTrial.LegacyTrialNeedsRetest(old,16384,"igd12um64xe3.dll"),"A receipt crossed size or driver-family boundaries.");}finally{Directory.Delete(folder,true);}
            });
            Test("change-started receipt is durable before the DLL replace checkpoint",()=>{
                string root=Path.Combine(Path.GetTempPath(),"ArcCapChangeCheckpoint-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{string receiptPath=Path.Combine(root,"game.json"),target=Path.Combine(root,"igd12um64xe2.dll");byte[] before={1,2,3},after={4,5,6};File.WriteAllBytes(target,before);var receipt=new MatchedSetReceipt{Status="changed-runtime-unverified",Executable=Path.Combine(root,"game.exe"),Folder=Path.Combine(root,"game.exe.local"),MiB=512,Files=new Dictionary<string,string>()};bool interrupted=false;try{MatchedSet.SaveChangeStarted(receiptPath,receipt,()=>{interrupted=true;throw new IOException("simulated power loss before staging/replacement");});}catch(IOException){}var saved=new JavaScriptSerializer().Deserialize<MatchedSetReceipt>(File.ReadAllText(receiptPath));Check(interrupted&&saved.Status=="change-started"&&File.ReadAllBytes(target).SequenceEqual(before),"Interrupted pre-replace change did not leave a durable recovery marker and untouched target.");}finally{Directory.Delete(root,true);}
            });
            Test("unknown bytes refused",()=>{var b=(byte[])original.Clone();b[0x190000]^=1;Reject(()=>Engine.Patch(b,1024));});
            Test("malformed PE refused",()=>{var b=(byte[])original.Clone();Array.Copy(new byte[]{255,255,255,255},0,b,0x3c,4);Reject(()=>Engine.Inspect(b));});
            Test("AI equivalent, access/protection/owner changes differ",()=>{
                string s="O:SYG:SYD:(A;ID;FA;;;SY)(A;ID;0x1200a9;;;WD)";
                Check(Engine.Equivalent(Engine.Canonical(s),Engine.Canonical(s.Replace("D:","D:AI"))),"AI difference");
                Check(!Engine.Equivalent(Engine.Canonical(s),Engine.Canonical(s.Replace("0x1200a9","FA"))),"Rights difference");
                Check(!Engine.Equivalent(Engine.Canonical(s),Engine.Canonical(s.Replace("D:","D:P"))),"Protection difference");
                Check(!Engine.Equivalent(Engine.Canonical(s),Engine.Canonical(s.Replace("O:SY","O:BA"))),"Owner difference");
            });
            Test("real Windows ACL backup, install and rollback on temporary file",()=>Temporary((target,folder)=>{
                var b=Engine.Validate(folder);Check(Engine.Equivalent(Engine.Security(target),b.Security),"ACL backup");
                Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,false);Check(Engine.Hash(Engine.Read(target))==Engine.OneGiB,"Install");
                Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,true);Check(Engine.Read(target).SequenceEqual(original),"Rollback");
                Check(Engine.Equivalent(Engine.Security(target),b.Security),"ACL rollback");
            }));
            Test("target drift refused",()=>Temporary((target,folder)=>{var b=Engine.Read(target);b[100]^=1;File.WriteAllBytes(target,b);Reject(()=>Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,false));Check(Engine.Read(target).SequenceEqual(b),"Target overwritten");}));
            Test("tampered bundle refused",()=>Temporary((target,folder)=>{string file=Path.Combine(folder,"patched.dll");var b=Engine.Read(file);b[100]^=1;File.WriteAllBytes(file,b);Reject(()=>Engine.Validate(folder));}));
            Test("ACL drift refused",()=>Temporary((target,folder)=>{
                var altered=Engine.Security(target);altered.Owner="altered";Reject(()=>Engine.Replace(folder,p=>altered,Engine.RestoreSecurity,false));Check(Engine.Read(target).SequenceEqual(original),"Target changed");
            }));
            Test("ACL failure leaves target unchanged",()=>Temporary((target,folder)=>{
                bool failed=false;try{Engine.Replace(folder,Engine.Security,(p,a)=>{throw new UnauthorizedAccessException("simulated denial");},false);}catch(UnauthorizedAccessException){failed=true;}
                Check(failed&&Engine.Read(target).SequenceEqual(original),"Denial modified target");Check(Engine.Validate(folder).Status=="failed-target-unchanged","Failure journal");
            }));
            Test("locked target leaves bytes unchanged",()=>Temporary((target,folder)=>{
                bool failed=false;using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read)){try{Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,false);}catch(IOException){failed=true;}}
                Check(failed&&Engine.Read(target).SequenceEqual(original),"Locked replacement should fail");Check(Engine.Validate(folder).Status=="failed-target-unchanged","Failure journal");
            }));
            Test("post-replacement failure journal and rollback",()=>Temporary((target,folder)=>{
                bool failed=false;try{Engine.Replace(folder,p=>{if(p==target&&Engine.Hash(Engine.Read(p))==Engine.OneGiB)throw new UnauthorizedAccessException("simulated post-replace query failure");return Engine.Security(p);},Engine.RestoreSecurity,false);}catch(UnauthorizedAccessException){failed=true;}
                Check(failed&&Engine.Validate(folder).Status=="recovery-required","Recovery journal");
                Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,true);Check(Engine.Read(target).SequenceEqual(original),"Recovery rollback");
            }));
            Test("confirmation mismatch refused",()=>Temporary((target,folder)=>{var approved=Engine.Validate(folder);approved.Target+=".other";Reject(()=>Engine.Replace(folder,Engine.Security,Engine.RestoreSecurity,false,approved));Check(Engine.Read(target).SequenceEqual(original),"Target changed");}));
            Test("normal mode installation gate refuses",()=>{bool failed=false;try{WindowsPlatform.RequireInstallEnvironment("C:\\not-a-driver.dll");}catch(InvalidOperationException){failed=true;}Check(failed,"Environment gate did not block");});
            Test("manifest isolation policy fails closed without developer override",()=>{
                Check(!Isolation.DotLocalCandidate(true,false),"Manifest must block simple redirection");
                Check(Isolation.DotLocalCandidate(false,false),"Unmanifested executable is a candidate, not a guarantee");
                Check(Isolation.DotLocalCandidate(true,true),"Existing developer override permits candidate evaluation");
            });
            Test("resource-only application manifest inspection",()=>{
                string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"IntelArcCacheCap.exe");
                Check(Isolation.Manifest(exe).Contains("asInvoker"),"Native GUI manifest should be detected");
            });
            Test("developer setting rejects unelevated writes",()=>{
                if(IsolationControl.Administrator)throw new Exception("Run this test suite unelevated; it must not alter Windows settings");
                bool rejected=false;try{IsolationControl.Enable();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Enable must require manual elevation");
            });
            Test("unknown game build refuses private-copy setup",()=>Temporary((target,folder)=>{
                string gameDir=Path.Combine(Path.GetDirectoryName(target),"x64_dx12");Directory.CreateDirectory(gameDir);string exe=Path.Combine(gameDir,"witcher3.exe");File.WriteAllText(exe,"unknown executable");
                Reject(()=>IsolationControl.PreparePrivateCopy(exe,target,Path.Combine(gameDir,"receipt.json"),1024));Check(!Directory.Exists(exe+".local"),"Unknown game setup changed files");
            }));
            string gameFixture=Isolation.FindWitcher();
            if(gameFixture!=null&&System.Diagnostics.Process.GetProcessesByName("witcher3").Length==0)Test("Witcher private-copy preparation, conflict refusal and owned removal on TEMPORARY copy",()=>Temporary((target,folder)=>{
                string gameDir=Path.Combine(Path.GetDirectoryName(target),"x64_dx12");Directory.CreateDirectory(gameDir);string exe=Path.Combine(gameDir,"witcher3.exe");File.Copy(gameFixture,exe);
                string receipt=Path.Combine(Path.GetDirectoryName(target),"private-copy.json");var r=IsolationControl.PreparePrivateCopy(exe,target,receipt,1024);
                Check(File.Exists(Path.Combine(r.Folder,"igd12um64xe2.dll"))&&r.DllHash==Engine.OneGiB,"Private copy failed");
                bool rejected=false;try{IsolationControl.PreparePrivateCopy(exe,target,receipt,1024);}catch(IOException){rejected=true;}Check(rejected,"Existing setup must not be overwritten");
                string extra=Path.Combine(r.Folder,"unowned.txt");File.WriteAllText(extra,"unowned");Reject(()=>IsolationControl.RemovePrivateCopy(receipt));Check(File.Exists(extra),"Unowned file was deleted");File.Delete(extra);
                IsolationControl.RemovePrivateCopy(receipt);Check(!Directory.Exists(r.Folder)&&File.Exists(exe),"Removal affected original executable");
                File.WriteAllText(Path.Combine(gameDir,"d3d12.dll"),"unverified local proxy");
                Reject(()=>IsolationControl.PreparePrivateCopy(exe,target,Path.Combine(Path.GetDirectoryName(target),"second-receipt.json"),1024));
                Check(!Directory.Exists(r.Folder),"Local proxy guard must prevent setup");
            }));
            Test("preflight detects an existing D3D12 proxy without loading it",()=>{
                Temporary((target,folder)=>{
                    string local=Path.Combine(Path.GetDirectoryName(target),"d3d12.dll");File.WriteAllText(local,"unverified proxy");
                    Check(Isolation.LocalD3D12(target)==local,"Local proxy must be detected");Reject(()=>Isolation.RequireNoLocalD3D12(target));
                    Check(File.ReadAllText(local)=="unverified proxy","Inspection must not change or execute the proxy");
                    File.Delete(local);string dxgi=Path.Combine(Path.GetDirectoryName(target),"dxgi.dll");File.WriteAllText(dxgi,"unverified proxy");
                    Check(Isolation.LocalDirectXProxy(target)==dxgi,"Local DXGI proxy must be detected");Reject(()=>Isolation.RequireNoLocalD3D12(target));
                    File.Delete(dxgi);string foreign=target+".local";Directory.CreateDirectory(foreign);
                    Check(Isolation.LocalPrivatePath(target)==foreign,"A pre-existing per-game .local folder must be detected");Directory.Delete(foreign);
                });
            });
            Test("native module enumeration sees this process kernel32",()=>{
                Check(WindowsPlatform.ModulePaths(System.Diagnostics.Process.GetCurrentProcess().Id).Any(p=>Path.GetFileName(p).Equals("kernel32.dll",StringComparison.OrdinalIgnoreCase)),"Native module enumeration failed");
            });
            Test("matched receipt rejects changed profile, extra DLL and unsupported cap",()=>{
                var r=new MatchedSetReceipt{Schema=3,Executable=@"C:\Temporary\bin\x64_dx12\witcher3.exe",Folder=@"C:\Temporary\bin\x64_dx12\witcher3.exe.local",MiB=512,Files=new Dictionary<string,string>(MatchedSet.Originals)};
                MatchedSet.ValidateReceipt(r);r.Files["igd12um64xe2.dll"]=Engine.OneGiB;r.MiB=1024;MatchedSet.ValidateReceipt(r);
                r.Files["igd12um64xe2.dll"]=Engine.TwoGiB;r.MiB=2048;MatchedSet.ValidateReceipt(r);
                r.Files["igd12um64xe2.dll"]=Engine.FourGiB;r.MiB=4096;MatchedSet.ValidateReceipt(r);
                r.Files["igd12um64xe2.dll"]=Engine.OneGiB;r.MiB=1024;
                r.MiB=2047;Reject(()=>MatchedSet.ValidateReceipt(r));r.MiB=1024;
                r.Files.Add("unowned.dll",Engine.Original);Reject(()=>MatchedSet.ValidateReceipt(r));r.Files.Remove("unowned.dll");
                r.Files["igd12umd64.dll"]=Engine.Original;Reject(()=>MatchedSet.ValidateReceipt(r));
            });
            Test("debug-helper receipt pins both helpers and rejects missing or unowned entries",()=>{
                var r=new MatchedSetReceipt{Schema=4,Executable=@"C:\Temporary\bin\x64_dx12\witcher3.exe",Folder=@"C:\Temporary\bin\x64_dx12\witcher3.exe.local",MiB=512,Files=new Dictionary<string,string>(MatchedSet.Originals)};
                foreach(var f in MatchedSet.DebugHelpers)r.Files.Add(f.Key,f.Value);MatchedSet.ValidateReceipt(r);
                r.Files["dbghelp.dll"]=Engine.Original;Reject(()=>MatchedSet.ValidateReceipt(r));r.Files["dbghelp.dll"]=MatchedSet.DebugHelpers["dbghelp.dll"];
                r.Schema=3;Reject(()=>MatchedSet.ValidateReceipt(r));r.Schema=4;
                r.Files.Remove("dbgcore.dll");Reject(()=>MatchedSet.ValidateReceipt(r));r.Files.Add("unowned.dll",Engine.Original);Reject(()=>MatchedSet.ValidateReceipt(r));
            });
            Test("qualified Windows debug sources have exact hashes",()=>MatchedSet.ValidateDebugSources());
            Test("unknown or malformed local debug helper blocks instead of being replaced",()=>Temporary((target,folder)=>{
                string exe=Path.Combine(Path.GetDirectoryName(target),"game.exe"),helper=Path.Combine(Path.GetDirectoryName(target),"dbghelp.dll");
                Check(!MatchedSet.NeedsDebugRoutingFix(exe),"No helper incorrectly needs fix");File.WriteAllText(helper,"unqualified DLL");Reject(()=>MatchedSet.NeedsDebugRoutingFix(exe));Check(File.ReadAllText(helper)=="unqualified DLL","Inspection changed bundled file");
            }));
            if(System.Diagnostics.Process.GetProcessesByName("witcher3").Length==0)Test("29-file recovery removes tracked Windows helpers and preserves bundled game files",()=>Temporary((target,folder)=>{
                string exe=Path.Combine(Path.GetDirectoryName(target),"bin","x64_dx12","witcher3.exe"),local=exe+".local";Directory.CreateDirectory(local);
                string bundled=Path.Combine(Path.GetDirectoryName(exe),"dbghelp.dll");File.WriteAllText(bundled,"bundled game file must survive");
                var r=new MatchedSetReceipt{Schema=4,Executable=exe,Folder=local,MiB=512,Status="prepared-runtime-unverified",Files=new Dictionary<string,string>(MatchedSet.Originals)};
                string driver=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\DriverStore\FileRepository\iigd_dch_d.inf_amd64_6a5efc7401ff0ebc");
                foreach(var f in MatchedSet.Originals)File.Copy(Path.Combine(driver,f.Key),Path.Combine(local,f.Key));
                foreach(var f in MatchedSet.DebugHelpers){r.Files.Add(f.Key,f.Value);File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),f.Key),Path.Combine(local,f.Key));}
                MatchedSet.ValidateContents(r);string receipt=Path.Combine(Path.GetDirectoryName(target),"debug-recovery.json");File.WriteAllText(receipt,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(r));
                MatchedSet.Remove(receipt);Check(!Directory.Exists(local)&&MatchedSet.Read(receipt).Status=="removed","Tracked files not fully removed");Check(File.ReadAllText(bundled)=="bundled game file must survive","Bundled DLL changed during rollback");
            }));
            Test("runtime profiles need no title whitelist and recovery metadata is self-contained",()=>{
                var profile=new MatchedSet.ProfileInfo{Schema=1,AppId=987654,Name="Fixture",Root=@"C:\Temporary\ObservedGame",RelativeExecutable=@"Engine\Win64\renderer.exe",Hash=Engine.Original,ObservedDriverHash=Engine.Original};
                string exe=MatchedSet.ProfileExecutable(profile);
                var receipt=new MatchedSetReceipt{Schema=3,Executable=exe,Folder=exe+".local",MiB=512,Files=new Dictionary<string,string>(MatchedSet.Originals),Game=profile};
                MatchedSet.ValidateReceipt(receipt);
                profile.RelativeExecutable=@"..\escaped.exe";Reject(()=>MatchedSet.ProfileExecutable(profile));profile.RelativeExecutable=@"Engine\Win64\renderer.exe";
                profile.ObservedDriverHash=Engine.OneGiB;Reject(()=>MatchedSet.ValidateReceipt(receipt));profile.ObservedDriverHash=Engine.Original;
                profile.Hash="unknown";Reject(()=>MatchedSet.ValidateReceipt(receipt));profile.Hash=Engine.Original;
                receipt.Executable=@"C:\Temporary\Other\renderer.exe";Reject(()=>MatchedSet.ValidateReceipt(receipt));
            });
            Test("matched receipt rejects arbitrary folder and changed contents",()=>Temporary((target,folder)=>{
                var r=new MatchedSetReceipt{Schema=3,Executable=Path.Combine(Path.GetDirectoryName(target),"bin","x64_dx12","witcher3.exe"),MiB=512,Files=new Dictionary<string,string>(MatchedSet.Originals)};
                r.Folder=folder;Reject(()=>MatchedSet.ValidateReceipt(r));r.Folder=r.Executable+".local";Directory.CreateDirectory(r.Folder);
                File.WriteAllText(Path.Combine(r.Folder,"unowned.txt"),"preserve");Reject(()=>MatchedSet.ValidateContents(r));
                Check(File.ReadAllText(Path.Combine(r.Folder,"unowned.txt"))=="preserve","Validation deleted unowned contents");
            }));
            if(args.Length>2)Test("live matched-set receipt and all pinned files validate READ ONLY",()=>MatchedSet.ValidateContents(MatchedSet.Read(args[2])));
            if(System.Diagnostics.Process.GetProcessesByName("witcher3").Length>0)report.Add("SKIP temporary single-DLL setup/removal: Witcher running; process guard intentionally applies.");
            Console.WriteLine("Passed "+passed+" tests. No installed driver replacement performed.");
            if(args.Length>1)File.WriteAllLines(args[1],report.Concat(new[]{"Passed "+passed+" tests; transactions used temporary files and real Windows ACL APIs."}));return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{DriverTrial.TrialRootOverride=null;if(isolatedTrialRoot!=null&&Directory.Exists(isolatedTrialRoot))Directory.Delete(isolatedTrialRoot,true);}
    }
}
}
