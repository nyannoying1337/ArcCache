using System;using System.IO;using System.Collections.Generic;
class CacheMonitorTests {
    static int passed;static void Check(bool yes,string what){if(!yes)throw new Exception(what);passed++;Console.WriteLine("PASS "+what);}
    static bool UpdatedActive(StutterFeed feed){feed.Update();return feed.Active;}
    // scskiller_frames.bin and scskiller_creates.csv as the recorder writes them: the launch starts at 1000 ms on its clock.
    // sameLaunch false: the csv's compiles belong to an earlier launch.
    static void WriteRecording(string folder,long unix,bool sameLaunch){
        var frames=new List<uint>();double t=1000,shaderEnd=0,loadEnd=0;
        foreach(int ms in Durations()){frames.Add((uint)(ms*1000));t+=ms;if(ms==120)shaderEnd=t;if(ms==1500)loadEnd=t;}
        using(var w=new BinaryWriter(File.Create(Path.Combine(folder,"scskiller_frames.bin")))){w.Write(0xFFFFFFFFu);w.Write(unix);w.Write(1000000L);w.Write(0L);w.Write(10000000L);foreach(uint f in frames)w.Write(f);}
        File.WriteAllText(Path.Combine(folder,"scskiller_creates.csv"),"#session,"+(sameLaunch?unix:unix-99999)+",game.exe\n#clock,1000\n"+(shaderEnd-30).ToString(System.Globalization.CultureInfo.InvariantCulture)+",G,0,0,40.0,abc,0.1,5,1\n"
            +(loadEnd-500).ToString(System.Globalization.CultureInfo.InvariantCulture)+",G,0,0,40.0,def,0.1,5,1\n");
    }
    static IEnumerable<int> Durations(){for(int i=0;i<300;i++)yield return 16;yield return 120;for(int i=0;i<200;i++)yield return 16;yield return 80;yield return 1500;for(int i=0;i<400;i++)yield return 16;}
    [STAThread]static void Main(string[] args){
        if(args.Length==2&&args[0]=="--replay-recorder"){
            var replay=new StutterFeed(args[1],0);replay.Update();Check(replay.Active,"Existing recorder frame data is readable");
            Console.WriteLine("Recorder replay succeeded; no game or recorder files changed.");return;
        }
        string root=Path.Combine(Path.GetTempPath(),"ArcCacheMonitorTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {File.WriteAllBytes(Path.Combine(root,"a"),new byte[100]);var initial=CacheSnapshot.Read(root);Check(initial.Bytes==100&&initial.Files==1&&initial.Unreadable==0,"Initial file size measured");
            Check(ArcCache.Core.RecorderSettings.FromLines(new[]{"traversal_suballocate=1"},true).TraversalText=="On","Overlay shows traversal On from the recorder setting");
            Check(ArcCache.Core.RecorderSettings.FromLines(new[]{"traversal_suballocate=0"},true).TraversalText=="Off","Overlay shows traversal Off from the recorder setting");
            Check(ArcCache.Core.RecorderSettings.FromLines(new[]{"traversal_suballocate=1"},false).TraversalText=="No recorder","Stale settings never claim a recorder is enabled");
            Check(ArcCache.Core.RecorderSettings.FromLines(new[]{";traversal_suballocate=1","traversal_suballocate=broken"},true).TraversalEnabled==null,"Invalid traversal setting is unavailable");
            Check(ArcCache.Core.RecorderSettings.FromLines(new[]{"traversal_suballocate=1","traversal_suballocate=0"},true).TraversalText=="Off","Last traversal value wins consistently in the page and overlay");
            Check(CacheSnapshot.Read(root).ChangedSince(initial)==0,"Idle cache reports zero change");
            File.WriteAllBytes(Path.Combine(root,"a"),new byte[150]);File.WriteAllBytes(Path.Combine(root,"b"),new byte[20]);var grown=CacheSnapshot.Read(root);Check(grown.Bytes-initial.Bytes==70&&grown.ChangedSince(initial)==2,"Growth includes new and enlarged files");
            File.Delete(Path.Combine(root,"a"));var shrunk=CacheSnapshot.Read(root);Check(shrunk.Bytes-initial.Bytes==-80&&shrunk.ChangedSince(initial)==2,"Eviction reports signed net decrease");
            Check(initial.Bytes==100,"Monitoring baseline remains fixed");
            // A recording: 16 ms frames, a 120 ms frame under a 40 ms compile, an 80 ms frame with nothing under it.
            string game=Path.Combine(root,"game");Directory.CreateDirectory(game);long unix=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            WriteRecording(game,unix,true);var feed=new StutterFeed(game,unix-60000);feed.Update();
            Check(feed.Active&&feed.ShaderStutters==1&&Math.Abs(feed.WorstMs-120)<0.01,"Recorder: a slow frame under a compile is a shader stutter");
            Check(feed.Recent().Count==3&&feed.Recent().FindAll(delegate(StutterFeed.Hitch h){return !h.Shader;}).Count==2,"Recorder: a slow frame without a compile is another hitch, a 1.5 s one under a compile is loading");
            Check(Math.Abs(feed.Fps-62.5)<1,"Recorder: frame rate from the last 5 s");
            double[] columns=feed.Columns(120);Check(columns.Length==120&&columns[119]>=15&&Array.Exists(columns,delegate(double c){return c>=119;}),"Recorder: the graph's columns keep the slowest frame");
            feed.Update();Check(feed.ShaderStutters==1,"Recorder: a second read counts nothing twice");
            Check(!new StutterFeed(game,unix+60000).Active&&UpdatedActive(new StutterFeed(game,unix+60000))==false,"Recorder: a launch from before the game started is ignored");
            WriteRecording(game,unix,false);var noCompiles=new StutterFeed(game,unix-60000);noCompiles.Update();Check(noCompiles.Active&&noCompiles.ShaderStutters==0,"Recorder: another launch's compiles don't count");
            string ff7=@"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII REMAKE\End\Binaries\Win64";
            if(File.Exists(Path.Combine(ff7,"scskiller_frames.bin"))){var real=new StutterFeed(ff7,0);real.Update();Console.WriteLine("FF7 recording: active "+real.Active+", "+real.ShaderStutters+" shader stutters, worst "+real.WorstMs.ToString("0")+" ms, "+real.Fps.ToString("0")+" fps, "+real.Recent().Count+" slow frames in its last 60 s");Check(real.Active,"Recorder: a real recording reads");}
            if(args.Length==1){
                using(var overlay=new CacheMonitor(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,null,root,Path.Combine(root,"logs"),null,16384,871L<<20,"Ready or Not","top-right",true)){
                    using(var preview=overlay.PreviewImage()){preview.Save(Path.ChangeExtension(args[0],".growth.png"));Check(overlay.StatusText.Length>0&&overlay.Text.Contains("Ready or Not"),"Overlay renders the game name and a status");}
                    WriteRecording(game,unix,true);overlay.feed=new StutterFeed(game,unix-60000);
                    using(var preview=overlay.PreviewImage(ArcCache.Core.RecorderSettings.FromLines(new[]{"traversal_suballocate=1"},true))){preview.Save(args[0]);Check(preview.Height>overlay.HeightFor(false)-1&&overlay.recorderSettings.TraversalText=="On","Overlay renders traversal On with the frame-time graph");}
                    int samples=overlay.DiskSampleCount;long logBytes=0;foreach(string f in Directory.GetFiles(Path.Combine(root,"logs")))logBytes+=new FileInfo(f).Length;
                    for(int i=0;i<20;i++)overlay.FastTick();long after=0;foreach(string f in Directory.GetFiles(Path.Combine(root,"logs")))after+=new FileInfo(f).Length;
                    Check(overlay.DiskSampleCount==samples&&after==logBytes,"Smooth refresh does not repeat disk samples or CSV writes");
                    Check(feed.VisualAdvanceMilliseconds>=0&&feed.VisualAdvanceMilliseconds<=1000,"Smooth graph motion is bounded between actual recorder samples");
                }
            }
            Check(CacheMonitor.Amount(528L<<20)=="528 MB"&&CacheMonitor.Amount(2900L<<20)=="2.8 GB","Overlay sizes use the app's format");Check(CacheSnapshot.Read(Path.Combine(root,"missing")).Unreadable==1,"Missing folder reports read failure");
        }finally{Directory.Delete(root,true);}Console.WriteLine("Passed "+passed+" monitor checks; only temporary fixture files were changed.");}
}
