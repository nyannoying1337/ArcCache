using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// External, read-only HUD. No DLL injection, hooks, driver writes or cache deletion.
class CacheSnapshot {
    public long Bytes;public int Files,Unreadable;public Dictionary<string,long> Sizes=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
    public static CacheSnapshot Read(string root) {
        var result=new CacheSnapshot();
        try {
            if(!Directory.Exists(root)){result.Unreadable++;return result;}
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cache root is a reparse point.");
            foreach(string path in Directory.GetFiles(root))try {
                var f=new FileInfo(path);if((f.Attributes&FileAttributes.ReparsePoint)!=0){result.Unreadable++;continue;}
                long size=f.Length;result.Bytes=checked(result.Bytes+size);result.Files++;result.Sizes[f.Name]=size;
            }catch(IOException){result.Unreadable++;}catch(UnauthorizedAccessException){result.Unreadable++;}
        }catch(IOException){result.Unreadable++;}catch(UnauthorizedAccessException){result.Unreadable++;}
        return result;
    }
    public int ChangedSince(CacheSnapshot before) {
        int count=0;foreach(var pair in Sizes){long old;if(!before.Sizes.TryGetValue(pair.Key,out old)||old!=pair.Value)count++;}
        foreach(string name in before.Sizes.Keys)if(!Sizes.ContainsKey(name))count++;return count;
    }
}

// What the overlay shows, from one state (AGENTS.md). Status pill, first match wins:
//   driver not checked yet            -> wait  "Checking the driver…"
//   normal driver loaded              -> warn  "Normal driver · capped at 512 MB"
//   game's file shrank                -> warn  "Cache file shrank · driver trimmed it"
//   cache file not open yet           -> wait  "Waiting for the game's cache file"
//   another file grows much faster    -> warn  "Another cache file is growing"
//   first minute                      -> wait  "Watching… play for a minute"
//   little growth, copy loaded        -> good  "Reading the prepared cache"
//   little growth, copy not confirmed -> wait  "Little compiling · copy not confirmed"
//   still growing                     -> info  "Adding new shaders · normal in new areas"
// Under it: the cache file bar (share of the game's cache size, a tick at SCSKiller's expected size) and one footer line.
enum OverlayTone {Wait,Good,Warn,Info}
class CacheMonitor : Form {
    readonly string executable,root,log,displayName;readonly CacheSnapshot initial;readonly Timer timer=new Timer();int gamePid;DateTime started;int sampleCount;string driverStatus="Driver: not checked";string observedModulePath;readonly Func<string,string> inspectDriver;readonly string cacheFile;readonly long limitBytes,expectedBytes;readonly int limitMiB;readonly string corner;readonly DateTime monitorStarted=DateTime.Now;
    // The last sample, drawn by OnPaint. The status also goes to the window title for screen readers.
    OverlayTone tone=OverlayTone.Wait;string status="Checking the driver…",fileSize,growthText,footer="";long fileBytes=-1;bool loggingFailed;float scale=1;
    struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref uint length);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x20|0x08000000|0x80;return p;}}
    /// <summary>"528 MB", "2.7 GB": the app's format.</summary>
    internal static string Amount(long bytes){return bytes>=1L<<30?(bytes/(double)(1L<<30)).ToString("0.0",CultureInfo.InvariantCulture)+" GB":(bytes/(double)(1L<<20)).ToString("0",CultureInfo.InvariantCulture)+" MB";}
    static string Limit(int mib){return mib>=1024?(mib/1024)+" GB":mib+" MB";}
    // cacheFile: this game's Intel cache file when Arc Cache has linked it (64 hex), else null: then the file that grew the
    // most since the overlay started is shown as the likely one. limitMiB: the game's cache size from its setup record.
    // expected: SCSKiller's estimated cache size from the game's last check (0 = none). name: the game's name for the header.
    // place: the game window's corner (top-right, top-left, bottom-right, bottom-left); click-through, so it isn't dragged.
    public CacheMonitor(string exe,Func<string,string> inspect=null,string cacheRoot=null,string logFolder=null,string linkedCacheFile=null,int limitMiB=512,long expected=0,string name=null,string place="top-right") {
        corner=place=="top-left"||place=="bottom-right"||place=="bottom-left"?place:"top-right";
        inspectDriver=inspect;cacheFile=linkedCacheFile!=null&&linkedCacheFile.Length==64&&System.Text.RegularExpressions.Regex.IsMatch(linkedCacheFile,"^[0-9A-Fa-f]{64}$")?linkedCacheFile:null;this.limitMiB=Math.Max(512,limitMiB);limitBytes=(long)this.limitMiB<<20;expectedBytes=Math.Max(0,expected);
        executable=Path.GetFullPath(exe);root=cacheRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Intel","ShaderCache");
        displayName=string.IsNullOrWhiteSpace(name)?Path.GetFileNameWithoutExtension(executable):name.Trim();
        foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))using(p){IntPtr h=OpenProcess(0x1000,false,(uint)p.Id);try{if(h==IntPtr.Zero)continue;var path=new System.Text.StringBuilder(4096);uint n=4096;if(QueryFullProcessImageName(h,0,path,ref n)&&path.ToString().Equals(executable,StringComparison.OrdinalIgnoreCase)){gamePid=p.Id;started=p.StartTime;break;}}finally{if(h!=IntPtr.Zero)CloseHandle(h);}}
        if(gamePid==0)throw new InvalidOperationException("Launch the selected game before starting the overlay.");
        initial=CacheSnapshot.Read(root);string logs=logFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","logs");Directory.CreateDirectory(logs);log=Path.Combine(logs,"cache-monitor-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".csv");
        File.WriteAllText(log,"utc,pid,shared_intel_bytes,net_growth_bytes,changed_files,unreadable_files,private_bytes,working_set_bytes\r\n");
        using(var g=CreateGraphics())scale=Math.Max(1f,g.DpiX/96f);
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Panel;Opacity=0.92;DoubleBuffered=true;Text="Arc Cache";
        ClientSize=new Size(S(296),S(150));
        timer.Interval=2000;timer.Tick+=delegate{Sample();};Shown+=delegate{Sample();timer.Start();};FormClosed+=delegate{timer.Dispose();};
    }
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        // Windows 11 rounds a borderless window's corners when asked (DWMWA_WINDOW_CORNER_PREFERENCE); older Windows keeps square ones.
        int round=2;try{DwmSetWindowAttribute(Handle,33,ref round,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    }
    int S(float v){return (int)Math.Round(v*scale);}
    static readonly Color Panel=Color.FromArgb(22,25,31),Text1=Color.FromArgb(242,244,247),Text2=Color.FromArgb(154,164,176),Text3=Color.FromArgb(127,137,148),Accent=Color.FromArgb(54,191,245),Track=Color.FromArgb(52,55,61);
    static Color Mix(Color c,double a){return Color.FromArgb((int)(Panel.R+(c.R-Panel.R)*a),(int)(Panel.G+(c.G-Panel.G)*a),(int)(Panel.B+(c.B-Panel.B)*a));}
    static Color ToneColor(OverlayTone t){return t==OverlayTone.Good?Color.FromArgb(60,207,134):t==OverlayTone.Warn?Color.FromArgb(240,165,48):Color.FromArgb(111,168,255);}
    static Color ToneText(OverlayTone t){return t==OverlayTone.Good?Color.FromArgb(127,225,174):t==OverlayTone.Warn?Color.FromArgb(255,196,107):Color.FromArgb(156,195,255);}
    static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r,float radius){
        var path=new System.Drawing.Drawing2D.GraphicsPath();float d=radius*2;
        if(d<=0||d>r.Height||d>r.Width){path.AddRectangle(r);return path;}
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Draw(e.Graphics,ClientSize.Width,ClientSize.Height);}
    void Draw(Graphics g,int width,int height){
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Panel);float pad=S(12),x=pad,w=width-pad*2,y=S(11);const TextFormatFlags Line=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
        using(var title=new Font("Segoe UI Semibold",9.5f))using(var small=new Font("Segoe UI",8.25f))using(var smallBold=new Font("Segoe UI Semibold",8.25f))using(var tiny=new Font("Segoe UI",7.5f)){
            // Header: bolt, game name, cache size chip.
            float icon=S(12);var bolt=new[]{new PointF(x+icon*0.58f,y),new PointF(x+icon*0.12f,y+icon*0.6f),new PointF(x+icon*0.46f,y+icon*0.6f),new PointF(x+icon*0.38f,y+icon*1.05f),new PointF(x+icon*0.86f,y+icon*0.4f),new PointF(x+icon*0.52f,y+icon*0.4f)};
            using(var b=new SolidBrush(Accent))g.FillPolygon(b,bolt);
            string chip=limitMiB<=512?"Default":Limit(limitMiB)+" copy";var chipText=TextRenderer.MeasureText(g,chip,smallBold,Size.Empty,TextFormatFlags.NoPadding);
            var chipRect=new RectangleF(x+w-chipText.Width-S(12),y-S(2),chipText.Width+S(12),chipText.Height+S(3));
            using(var path=Rounded(chipRect,S(5)))using(var b=new SolidBrush(Mix(Color.White,0.09)))g.FillPath(b,path);
            TextRenderer.DrawText(g,chip,smallBold,Rectangle.Round(chipRect),Color.FromArgb(185,194,204),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,displayName,title,new Rectangle((int)(x+icon+S(6)),(int)y-S(3),(int)(chipRect.X-x-icon-S(12)),S(20)),Text1,Line);
            // Status pill.
            y+=S(23);var pill=new RectangleF(x,y,w,S(22));
            using(var path=Rounded(pill,S(6)))using(var b=new SolidBrush(Mix(ToneColor(tone),0.18)))g.FillPath(b,path);
            using(var b=new SolidBrush(ToneColor(tone)))g.FillEllipse(b,x+S(8),y+S(7.5f),S(7),S(7));
            TextRenderer.DrawText(g,status,smallBold,new Rectangle((int)(x+S(21)),(int)y,(int)(w-S(25)),(int)pill.Height),ToneText(tone),Line|TextFormatFlags.VerticalCenter);
            // Cache file row: name left, "528 MB / 16 GB" right.
            y+=S(31);TextRenderer.DrawText(g,"Cache file",small,new Point((int)x,(int)y),Text2,TextFormatFlags.NoPadding);
            string ofLimit=" / "+Limit(limitMiB);var ofSize=TextRenderer.MeasureText(g,ofLimit,small,Size.Empty,TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,ofLimit,small,new Point((int)(x+w-ofSize.Width),(int)y),Text2,TextFormatFlags.NoPadding);
            string have=fileSize??"not linked yet";var haveSize=TextRenderer.MeasureText(g,have,smallBold,Size.Empty,TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,have,smallBold,new Point((int)(x+w-ofSize.Width-haveSize.Width),(int)y),Text1,TextFormatFlags.NoPadding);
            // Bar, with a tick at SCSKiller's expected size.
            y+=S(18);var track=new RectangleF(x,y,w,S(6));
            using(var path=Rounded(track,S(3)))using(var b=new SolidBrush(Track))g.FillPath(b,path);
            if(fileBytes>0){float fill=(float)Math.Min(1,fileBytes/(double)limitBytes);var bar=new RectangleF(x,y,Math.Max(S(6),w*fill),S(6));bool warn=tone==OverlayTone.Warn;
                using(var path=Rounded(bar,S(3)))using(var b=new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(x,y,w,S(6)),warn?Color.FromArgb(224,154,44):Color.FromArgb(54,183,240),warn?Color.FromArgb(255,196,107):Color.FromArgb(95,208,255),0f))g.FillPath(b,path);}
            bool marker=expectedBytes>0&&expectedBytes<limitBytes;
            if(marker){float mx=x+w*(float)(expectedBytes/(double)limitBytes);using(var b=new SolidBrush(Color.FromArgb(214,221,229)))g.FillRectangle(b,mx-S(1),y-S(3),Math.Max(1,S(2)),S(12));}
            y+=S(11);TextRenderer.DrawText(g,growthText??"",tiny,new Point((int)x,(int)y),Text3,TextFormatFlags.NoPadding);
            string right=marker?"tick: "+Amount(expectedBytes)+" expected":fileBytes>0?(fileBytes*100.0/limitBytes).ToString("0",CultureInfo.InvariantCulture)+"%":"";
            var rightSize=TextRenderer.MeasureText(g,right,tiny,Size.Empty,TextFormatFlags.NoPadding);TextRenderer.DrawText(g,right,tiny,new Point((int)(x+w-rightSize.Width),(int)y),Text3,TextFormatFlags.NoPadding);
            // Footer.
            y+=S(19);using(var p=new Pen(Mix(Color.White,0.07)))g.DrawLine(p,x,y,x+w,y);
            TextRenderer.DrawText(g,footer,tiny,new Rectangle((int)x,(int)y+S(6),(int)w,S(14)),Text3,Line);
        }
    }
    internal Bitmap PreviewImage(){Sample();var image=new Bitmap(ClientSize.Width,ClientSize.Height);using(var g=Graphics.FromImage(image))Draw(g,image.Width,image.Height);return image;}
    internal string StatusText {get{return status;}}
    // The file the game has open, else the saved link, else the file that grew most since the overlay started.
    string CacheName(CacheSnapshot now) {
        string name=ActiveFile;if(name!=null)return name;long best=0;
        foreach(var pair in now.Sizes){long old;long grew=pair.Value-(initial.Sizes.TryGetValue(pair.Key,out old)?old:0);if(grew>best){best=grew;name=pair.Key;}}
        return name;
    }
    // Is the game using its prepared cache? Hits read the file without growing it; misses compile and append. The stock
    // driver trims a file over 512 MiB, so a shrinking file means the private copy isn't in use; another file growing
    // much faster means the game is filling a different cache (another exe name). Read only: sizes, never contents.
    void Verdict(CacheSnapshot now,string name,long size,long grewSince) {
        bool copyLoaded=driverStatus.StartsWith("Game setup: custom copy loaded",StringComparison.Ordinal);
        if(driverStatus=="Driver: not checked"){tone=OverlayTone.Wait;status="Checking the driver…";return;}
        // The verdict follows the driver: with the normal driver the larger limit does not apply at all.
        if(driverStatus.StartsWith("Game setup: normal or other copy loaded",StringComparison.Ordinal)){tone=OverlayTone.Warn;status="Normal driver · capped at 512 MB";return;}
        long before=0;if(name!=null)initial.Sizes.TryGetValue(name,out before);
        if(name!=null&&before>(512L<<20)+(1L<<20)&&size<before-(1L<<20)){tone=OverlayTone.Warn;status="Cache file shrank · driver trimmed it";return;}
        if(ActiveFile==null){tone=OverlayTone.Wait;status="Waiting for the game's cache file";return;}
        long otherGrowth=0;foreach(var pair in now.Sizes){if(pair.Key.Equals(name,StringComparison.OrdinalIgnoreCase))continue;long old;long grew=pair.Value-(initial.Sizes.TryGetValue(pair.Key,out old)?old:0);if(grew>otherGrowth)otherGrowth=grew;}
        if(otherGrowth>(64L<<20)&&otherGrowth>Math.Max(grewSince,0)*4){tone=OverlayTone.Warn;status="Another cache file is growing (+"+Amount(otherGrowth)+")";return;}
        if((DateTime.Now-monitorStarted).TotalSeconds<60){tone=OverlayTone.Wait;status="Watching… play for a minute";return;}
        if(grewSince<(32L<<20)){tone=copyLoaded?OverlayTone.Good:OverlayTone.Wait;status=copyLoaded?"Reading the prepared cache":"Little compiling · copy not confirmed";return;}
        tone=OverlayTone.Info;status="Adding new shaders · normal in new areas";
    }
    // The file the game itself has open (Restart Manager) beats the saved link: a compile also fills the game's separate
    // D3D11 cache file, which can end up linked by mistake. Checked every 10 s until found.
    string detectedFile;
    string ActiveFile {get{return detectedFile??cacheFile;}}
    void DetectOpenFile(CacheSnapshot now){
        if(detectedFile!=null)return;string found=null;
        foreach(string name in now.Sizes.Keys){
            if(name.Length!=64)continue;
            if(!OpenFiles.Holders(Path.Combine(root,name)).Contains(gamePid))continue;
            if(found!=null)return; // several: keep the saved link
            found=name;
        }
        detectedFile=found;
    }
    // Below the title bar at the top (windowed games have one), above the bottom edge otherwise; never left of the window.
    Point Place(Rect game){
        bool left=corner.EndsWith("left",StringComparison.Ordinal),top=corner.StartsWith("top",StringComparison.Ordinal);
        int x=left?game.Left+S(18):Math.Max(game.Left,game.Right-Width-S(18));
        int y=top?game.Top+S(42):Math.Max(game.Top,game.Bottom-Height-S(18));
        return new Point(x,y);
    }
    void Sample() {
        long privateBytes=0,workingSet=0;
        try{using(var p=Process.GetProcessById(gamePid)){if(p.HasExited||p.StartTime!=started){Close();return;}privateBytes=p.PrivateMemorySize64;workingSet=p.WorkingSet64;
            if(sampleCount++%5==0){bool found=false;try{foreach(ProcessModule m in p.Modules)if((m.ModuleName.Equals("igd12um64xe2.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("igd12um64xeh.dll",StringComparison.OrdinalIgnoreCase))){found=true;string path=m.FileName;if(path==observedModulePath)break;observedModulePath=path;driverStatus=path.StartsWith(executable+".local\\",StringComparison.OrdinalIgnoreCase)?"Game setup: custom copy loaded":"Game setup: normal or other copy loaded";if(inspectDriver!=null){try{driverStatus+=" · "+inspectDriver(path);}catch{driverStatus+=" · size not checked";}}break;}if(!found){driverStatus="Game setup: not confirmed";observedModulePath=null;}}catch{driverStatus="Game setup: could not be checked";observedModulePath=null;}}
        }}catch{Close();return;}
        IntPtr front=GetForegroundWindow();uint foregroundPid;GetWindowThreadProcessId(front,out foregroundPid);Rect rect;
        if(foregroundPid==gamePid&&GetWindowRect(front,out rect)){Location=Place(rect);if(!Visible)Show();}else if(Visible)Hide();
        var now=CacheSnapshot.Read(root);long growth=now.Bytes-initial.Bytes;int changed=now.ChangedSince(initial);
        if(sampleCount%5==1)try{DetectOpenFile(now);}catch(Exception){}
        string name=CacheName(now);long size;
        if(name!=null&&now.Sizes.TryGetValue(name,out size)){long before;long grewSince=size-(initial.Sizes.TryGetValue(name,out before)?before:0);fileBytes=size;fileSize=Amount(size);
            growthText=(grewSince>=0?"+":"-")+Amount(Math.Abs(grewSince))+" this session"+(ActiveFile==null?" · likely file":"");Verdict(now,name,size,grewSince);}
        else{fileBytes=-1;fileSize=null;growthText=name==null?"No file grew yet":"File not found";Verdict(now,null,0,0);}
        footer="RAM "+Amount(workingSet)+" · all Intel caches "+Amount(now.Bytes)+(now.Unreadable>0?" · "+now.Unreadable+" unreadable":"")+(loggingFailed?" · log unavailable":"");
        Text="Arc Cache · "+displayName+" · "+status;
        try{File.AppendAllText(log,DateTime.UtcNow.ToString("o")+","+gamePid+","+now.Bytes+","+growth+","+changed+","+now.Unreadable+","+privateBytes+","+workingSet+"\r\n");loggingFailed=false;}catch(IOException){loggingFailed=true;}catch(UnauthorizedAccessException){loggingFailed=true;}
        Invalidate();
    }
}

// Windows Restart Manager, read only: which processes have a file open.
static class OpenFiles {
    [StructLayout(LayoutKind.Sequential)] struct UniqueProcess {public int ProcessId;public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessInfo {public UniqueProcess Process;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string AppName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string ServiceShortName;public int ApplicationType;public uint AppStatus;public uint TSSessionId;[MarshalAs(UnmanagedType.Bool)] public bool Restartable;}
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint session,int flags,string key);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint session,uint files,string[] names,uint processes,IntPtr uniqueProcesses,uint services,string[] serviceNames);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint session,out uint needed,ref uint count,[In,Out] ProcessInfo[] info,ref uint rebootReasons);
    public static List<int> Holders(string path){
        var result=new List<int>();uint session;if(RmStartSession(out session,0,Guid.NewGuid().ToString("N"))!=0)return result;
        try{
            if(RmRegisterResources(session,1,new[]{path},0,IntPtr.Zero,0,null)!=0)return result;
            for(int attempt=0;attempt<3;attempt++){
                uint needed,count=0,reasons=0;int error=RmGetList(session,out needed,ref count,null,ref reasons);
                if(error==0||error!=234||needed==0)return result;
                var info=new ProcessInfo[needed];count=needed;
                if(RmGetList(session,out needed,ref count,info,ref reasons)==0){for(int i=0;i<count;i++)result.Add(info[i].Process.ProcessId);return result;}
            }
            return result;
        }finally{RmEndSession(session);}
    }
}

// System RAM is not a measurement of Intel's shader cache in memory.
class GraphicsStatistics {
 [StructLayout(LayoutKind.Sequential)]class MemoryStatus {public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus)),Load;public ulong Total,Available,PageTotal,PageAvailable,VirtualTotal,VirtualAvailable,Extended;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatus status);
 public static object Read(){var memory=new MemoryStatus();if(!GlobalMemoryStatusEx(memory))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Intel","ShaderCache");var cache=CacheSnapshot.Read(root);return new{RamTotal=memory.Total,RamAvailable=memory.Available,DiskBytes=cache.Bytes,cache.Files,cache.Unreadable,CheckedUtc=DateTime.UtcNow.ToString("o")};}
}
