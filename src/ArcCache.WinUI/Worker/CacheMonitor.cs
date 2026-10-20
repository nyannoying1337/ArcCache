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
//   normal driver, larger size chosen -> warn  "Normal driver · capped at 512 MB" (Default expects the normal driver)
//   game's file shrank                -> warn  "Cache file shrank · driver trimmed it"
//   cache file not open yet           -> wait  "Waiting for the game's cache file"
//   another file grows much faster    -> warn  "Another cache file is growing"
//   first minute                      -> wait  "Watching… play for a minute"
//   little growth, copy loaded/Default-> good  "Reading the prepared cache"
//   little growth, copy not confirmed -> wait  "Little compiling · copy not confirmed"
//   still growing                     -> info  "Adding new shaders · normal in new areas"
// Under it: the cache file bar (share of the game's cache size, a tick at SCSKiller's expected size) and one footer line.
enum OverlayTone {Wait,Good,Warn,Info}
class CacheMonitor : Form {
    readonly string executable,root,log,displayName;readonly CacheSnapshot initial;readonly Timer timer=new Timer();int gamePid;DateTime started;int sampleCount;string driverStatus="Driver: not checked";string observedModulePath;readonly Func<string,string> inspectDriver;readonly string cacheFile;readonly long limitBytes,expectedBytes;readonly int limitMiB;readonly string corner;readonly DateTime monitorStarted=DateTime.Now;
    // The last sample, drawn by OnPaint. The status also goes to the window title for screen readers.
    OverlayTone tone=OverlayTone.Wait;string status="Checking the driver…",fileSize,growthText,footer="";long fileBytes=-1;bool loggingFailed;float scale=1;
    // Frame times when SCSKiller's recorder is in the game's folder; else the samples in which the cache file grew.
    internal StutterFeed feed;readonly List<KeyValuePair<DateTime,long>> growthMarks=new List<KeyValuePair<DateTime,long>>();long lastFileBytes=-1;bool showingFrames;
    struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref uint length);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
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
        ReadAccent();feed=new StutterFeed(Path.GetDirectoryName(executable),new DateTimeOffset(started.ToUniversalTime()).ToUnixTimeMilliseconds()-5000);
        ClientSize=new Size(S(296),HeightFor(false));
        timer.Interval=2000;timer.Tick+=delegate{Sample();};Shown+=delegate{Sample();timer.Start();};FormClosed+=delegate{timer.Dispose();};
    }
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        // Windows 11 rounds a borderless window's corners when asked (DWMWA_WINDOW_CORNER_PREFERENCE); older Windows keeps square ones.
        int round=2;try{DwmSetWindowAttribute(Handle,33,ref round,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    }
    int S(float v){return (int)Math.Round(v*scale);}
    // WinUI's dark theme: its card, text and status colours, and the Windows accent colour's lighter shades, as WinUI
    // uses them on dark backgrounds (AccentPalette: Light3, Light2, Light1, Accent, ... as RGBA).
    static readonly Color Panel=Color.FromArgb(32,32,32),Text1=Color.FromArgb(255,255,255),Text2=Color.FromArgb(204,204,204),Text3=Color.FromArgb(150,150,150),Track=Color.FromArgb(61,61,61),
        Critical=Color.FromArgb(255,153,164),OtherHitch=Color.FromArgb(160,160,160);
    Color accent=Color.FromArgb(76,194,255),accentLight=Color.FromArgb(153,235,255);
    void ReadAccent(){
        try{
            var palette=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent","AccentPalette",null) as byte[];
            if(palette!=null&&palette.Length>=8){accent=Color.FromArgb(palette[4],palette[5],palette[6]);accentLight=Color.FromArgb(palette[0],palette[1],palette[2]);}
        }catch(System.Security.SecurityException){}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    static Color Mix(Color c,double a){return Color.FromArgb((int)(Panel.R+(c.R-Panel.R)*a),(int)(Panel.G+(c.G-Panel.G)*a),(int)(Panel.B+(c.B-Panel.B)*a));}
    Color ToneColor(OverlayTone t){return t==OverlayTone.Good?Color.FromArgb(108,203,95):t==OverlayTone.Warn?Color.FromArgb(252,225,0):t==OverlayTone.Info?accent:Text3;}
    // Segoe UI Variable (Windows 11) where installed, else Segoe UI: a missing family would fall back to Microsoft Sans Serif.
    static string Family(string variable,string classic){try{using(new FontFamily(variable))return variable;}catch(ArgumentException){return classic;}}
    static readonly string TextFamily=Family("Segoe UI Variable Text","Segoe UI"),BoldFamily=Family("Segoe UI Variable Text Semibold","Segoe UI Semibold"),SmallFamily=Family("Segoe UI Variable Small","Segoe UI");
    static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r,float radius){
        var path=new System.Drawing.Drawing2D.GraphicsPath();float d=radius*2;
        if(d<=0||d>r.Height||d>r.Width){path.AddRectangle(r);return path;}
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);Draw(e.Graphics,ClientSize.Width,ClientSize.Height);}
    void Draw(Graphics g,int width,int height){
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Panel);float pad=S(12),x=pad,w=width-pad*2,y=S(11);const TextFormatFlags Line=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
        using(var title=new Font(BoldFamily,9.5f))using(var small=new Font(TextFamily,8.25f))using(var smallBold=new Font(BoldFamily,8.25f))using(var tiny=new Font(SmallFamily,7.5f)){
            // Header: bolt, game name, cache size chip.
            float icon=S(12);var bolt=new[]{new PointF(x+icon*0.58f,y),new PointF(x+icon*0.12f,y+icon*0.6f),new PointF(x+icon*0.46f,y+icon*0.6f),new PointF(x+icon*0.38f,y+icon*1.05f),new PointF(x+icon*0.86f,y+icon*0.4f),new PointF(x+icon*0.52f,y+icon*0.4f)};
            using(var b=new SolidBrush(accent))g.FillPolygon(b,bolt);
            string chip=limitMiB<=512?"Default":Limit(limitMiB)+" copy";var chipText=TextRenderer.MeasureText(g,chip,smallBold,Size.Empty,TextFormatFlags.NoPadding);
            var chipRect=new RectangleF(x+w-chipText.Width-S(12),y-S(2),chipText.Width+S(12),chipText.Height+S(3));
            using(var path=Rounded(chipRect,S(4)))using(var b=new SolidBrush(Mix(Color.White,0.08)))g.FillPath(b,path);
            TextRenderer.DrawText(g,chip,smallBold,Rectangle.Round(chipRect),Text2,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,displayName,title,new Rectangle((int)(x+icon+S(6)),(int)y-S(3),(int)(chipRect.X-x-icon-S(12)),S(20)),Text1,Line);
            // Status pill.
            y+=S(23);var pill=new RectangleF(x,y,w,S(22));
            using(var path=Rounded(pill,S(4)))using(var b=new SolidBrush(Mix(ToneColor(tone),0.16)))g.FillPath(b,path);
            using(var b=new SolidBrush(ToneColor(tone)))g.FillEllipse(b,x+S(8),y+S(7.5f),S(7),S(7));
            TextRenderer.DrawText(g,status,smallBold,new Rectangle((int)(x+S(21)),(int)y,(int)(w-S(25)),(int)pill.Height),tone==OverlayTone.Wait?Text2:ToneColor(tone),Line|TextFormatFlags.VerticalCenter);
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
                using(var path=Rounded(bar,S(3)))using(var b=new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(x,y,w,S(6)),warn?Color.FromArgb(222,198,0):accent,warn?Color.FromArgb(255,236,110):accentLight,0f))g.FillPath(b,path);}
            bool marker=expectedBytes>0&&expectedBytes<limitBytes;
            if(marker){float mx=x+w*(float)(expectedBytes/(double)limitBytes);using(var b=new SolidBrush(Color.FromArgb(220,220,220)))g.FillRectangle(b,mx-S(1),y-S(3),Math.Max(1,S(2)),S(12));}
            y+=S(11);TextRenderer.DrawText(g,growthText??"",tiny,new Point((int)x,(int)y),Text3,TextFormatFlags.NoPadding);
            string right=marker?"tick: "+Amount(expectedBytes)+" expected":fileBytes>0?(fileBytes*100.0/limitBytes).ToString("0",CultureInfo.InvariantCulture)+"%":"";
            var rightSize=TextRenderer.MeasureText(g,right,tiny,Size.Empty,TextFormatFlags.NoPadding);TextRenderer.DrawText(g,right,tiny,new Point((int)(x+w-rightSize.Width),(int)y),Text3,TextFormatFlags.NoPadding);
            y+=S(18);
            if(feed!=null&&feed.Active)DrawFrames(g,x,y,w,tiny);else DrawGrowth(g,x,y,w,tiny);
            // Footer.
            y=height-S(33);using(var p=new Pen(Mix(Color.White,0.07)))g.DrawLine(p,x,y,x+w,y);
            TextRenderer.DrawText(g,footer,tiny,new Rectangle((int)x,(int)y+S(6),(int)w,S(14)),Text3,Line);
        }
    }
    const TextFormatFlags Tight=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine;
    void SectionTitle(Graphics g,float x,float y,float w,Font font,string left,string right,Color rightColor){
        TextRenderer.DrawText(g,left,font,new Point((int)x,(int)y),Text2,Tight);
        var size=TextRenderer.MeasureText(g,right,font,Size.Empty,Tight);TextRenderer.DrawText(g,right,font,new Point((int)(x+w-size.Width),(int)y),rightColor,Tight);
    }
    // SCSKiller's recorder: the last 60 s of frame times (each column the slowest frame in it), a dashed line at 50 ms,
    // and a dot on every slow frame, red where a shader compile caused it.
    void DrawFrames(Graphics g,float x,float y,float w,Font tiny){
        string right=feed.ShaderStutters==0?"No shader stutters yet":feed.ShaderStutters+(feed.ShaderStutters==1?" shader stutter":" shader stutters")+" · worst "+feed.WorstMs.ToString("0",CultureInfo.InvariantCulture)+" ms";
        SectionTitle(g,x,y,w,tiny,"Last 60 s"+(feed.Fps>0?" · "+feed.Fps.ToString("0",CultureInfo.InvariantCulture)+" fps":""),right,feed.ShaderStutters==0?Text3:Critical);
        y+=S(15);float h=S(GraphHeight);var box=new RectangleF(x,y,w,h);
        using(var path=Rounded(box,S(4)))using(var b=new SolidBrush(Mix(Color.Black,0.35)))g.FillPath(b,path);
        const double Top=150;Func<double,float> Y=delegate(double ms){return y+h-S(2)-(float)(Math.Min(ms,Top)/Top)*(h-S(6));};
        using(var dash=new Pen(Mix(Color.White,0.2)){DashPattern=new[]{3f,3f}})g.DrawLine(dash,x,Y(StutterFeed.HitchMs),x+w,Y(StutterFeed.HitchMs));
        var label=TextRenderer.MeasureText(g,"50 ms",tiny,Size.Empty,Tight);TextRenderer.DrawText(g,"50 ms",tiny,new Point((int)(x+S(4)),(int)(Y(StutterFeed.HitchMs)-label.Height-S(1))),Text3,Tight);
        double[] columns=feed.Columns(120);var points=new List<PointF>();
        using(var pen=new Pen(accent,Math.Max(1f,scale*1.4f)){LineJoin=System.Drawing.Drawing2D.LineJoin.Round}){
            for(int i=0;i<=columns.Length;i++){
                if(i<columns.Length&&columns[i]>=0){points.Add(new PointF(x+w*i/(columns.Length-1),Y(columns[i])));continue;}
                if(points.Count>1)g.DrawLines(pen,points.ToArray());points.Clear();
            }
        }
        foreach(var hitch in feed.Recent()){
            float hx=x+w*(float)hitch.Position,r=S(2.5f);
            using(var b=new SolidBrush(hitch.Shader?Critical:OtherHitch))g.FillEllipse(b,hx-r,Y(hitch.Ms)-r,r*2,r*2);
        }
    }
    // Without the recorder: a mark for every sample in the last 60 s in which the game's cache file grew (new shaders).
    void DrawGrowth(Graphics g,float x,float y,float w,Font tiny){
        DateTime now=DateTime.UtcNow;long grew=0;foreach(var e in growthMarks)if((now-e.Key).TotalSeconds<=60)grew+=e.Value;
        SectionTitle(g,x,y,w,tiny,"Cache growth · last 60 s",grew>0?"+"+Amount(grew):"No new shaders",grew>0?ToneColor(OverlayTone.Warn):Text3);
        y+=S(15);float h=S(StripHeight);var box=new RectangleF(x,y,w,h);
        using(var path=Rounded(box,S(4)))using(var b=new SolidBrush(Mix(Color.Black,0.35)))g.FillPath(b,path);
        using(var b=new SolidBrush(ToneColor(OverlayTone.Warn)))foreach(var e in growthMarks){
            double age=(now-e.Key).TotalSeconds;if(age>60)continue;
            float mx=x+S(3)+(w-S(6))*(float)(1-age/60);g.FillRectangle(b,mx-S(1),y+S(4),Math.Max(1,S(2)),h-S(8));
        }
    }
    const float GraphHeight=46,StripHeight=18;
    // The footer sits 10 px under the graph or strip: 170 px of card around it.
    internal int HeightFor(bool frames){return S(170+(frames?GraphHeight:StripHeight));}
    internal Bitmap PreviewImage(){Sample();ClientSize=new Size(S(296),HeightFor(feed!=null&&feed.Active));var image=new Bitmap(ClientSize.Width,ClientSize.Height);using(var g=Graphics.FromImage(image))Draw(g,image.Width,image.Height);return image;}
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
        if(limitMiB>512&&driverStatus.StartsWith("Game setup: normal or other copy loaded",StringComparison.Ordinal)){tone=OverlayTone.Warn;status="Normal driver · capped at 512 MB";return;}
        long before=0;if(name!=null)initial.Sizes.TryGetValue(name,out before);
        if(name!=null&&before>(512L<<20)+(1L<<20)&&size<before-(1L<<20)){tone=OverlayTone.Warn;status="Cache file shrank · driver trimmed it";return;}
        if(ActiveFile==null){tone=OverlayTone.Wait;status="Waiting for the game's cache file";return;}
        long otherGrowth=0;foreach(var pair in now.Sizes){if(pair.Key.Equals(name,StringComparison.OrdinalIgnoreCase))continue;long old;long grew=pair.Value-(initial.Sizes.TryGetValue(pair.Key,out old)?old:0);if(grew>otherGrowth)otherGrowth=grew;}
        if(otherGrowth>(64L<<20)&&otherGrowth>Math.Max(grewSince,0)*4){tone=OverlayTone.Warn;status="Another cache file is growing (+"+Amount(otherGrowth)+")";return;}
        if((DateTime.Now-monitorStarted).TotalSeconds<60){tone=OverlayTone.Wait;status="Watching… play for a minute";return;}
        if(grewSince<(32L<<20)){bool expected=copyLoaded||limitMiB<=512;tone=expected?OverlayTone.Good:OverlayTone.Wait;status=expected?"Reading the prepared cache":"Little compiling · copy not confirmed";return;}
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
        if(sampleCount%15==0)ReadAccent();
        try{feed.Update();}catch(IOException){}catch(UnauthorizedAccessException){}
        if(feed.Active!=showingFrames){showingFrames=feed.Active;ClientSize=new Size(S(296),HeightFor(showingFrames));}
        IntPtr front=GetForegroundWindow();uint foregroundPid;GetWindowThreadProcessId(front,out foregroundPid);Rect rect;
        // Borderless games often make their own window topmost after the overlay opened (auto-start opens it at launch),
        // which buries the overlay: put it back on top each sample, without moving, sizing or activating it.
        if(foregroundPid==gamePid&&GetWindowRect(front,out rect)){Location=Place(rect);if(!Visible)Show();SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);}else if(Visible)Hide();
        var now=CacheSnapshot.Read(root);long growth=now.Bytes-initial.Bytes;int changed=now.ChangedSince(initial);
        if(sampleCount%5==1)try{DetectOpenFile(now);}catch(Exception){}
        string name=CacheName(now);long size;
        if(name!=null&&now.Sizes.TryGetValue(name,out size)){long before;long grewSince=size-(initial.Sizes.TryGetValue(name,out before)?before:0);fileBytes=size;fileSize=Amount(size);
            if(lastFileBytes>=0&&size>lastFileBytes)growthMarks.Add(new KeyValuePair<DateTime,long>(DateTime.UtcNow,size-lastFileBytes));lastFileBytes=size;
            growthMarks.RemoveAll(delegate(KeyValuePair<DateTime,long> e){return (DateTime.UtcNow-e.Key).TotalSeconds>60;});
            growthText=(grewSince>=0?"+":"-")+Amount(Math.Abs(grewSince))+" this session"+(ActiveFile==null?" · likely file":"");Verdict(now,name,size,grewSince);}
        else{fileBytes=-1;fileSize=null;growthText=name==null?"No file grew yet":"File not found";Verdict(now,null,0,0);}
        footer="RAM "+Amount(workingSet)+(showingFrames?" · frame times from SCSKiller's recorder":" · no frame times: SCSKiller's recorder is off")+(now.Unreadable>0?" · "+now.Unreadable+" unreadable":"")+(loggingFailed?" · log unavailable":"");
        Text="Arc Cache · "+displayName+" · "+status;
        try{File.AppendAllText(log,DateTime.UtcNow.ToString("o")+","+gamePid+","+now.Bytes+","+growth+","+changed+","+now.Unreadable+","+privateBytes+","+workingSet+"\r\n");loggingFailed=false;}catch(IOException){loggingFailed=true;}catch(UnauthorizedAccessException){loggingFailed=true;}
        Invalidate();
    }
}

// SCSKiller's recorder, read only: its frame times (scskiller_frames.bin) and shader creates (scskiller_creates.csv)
// beside the game's exe, both timed in ms since the recorder loaded (SCSKiller ARCHITECTURE.md). Each sample reads what
// was added since the last; the frames file holds one launch, replaced at that launch's first frame, so only a launch
// stamped after the game started counts. A frame of 50 ms or more is classified 3 s after it ended (the recorder writes
// once a second), like SCSKiller's FrameLog without its startup and quit windows:
//   no create overlaps it and it took 5 s or more               -> a pause, not counted
//   1 s or more                                                 -> loading (a load screen, not a stutter)
//   compiles on 8 threads or more overlap it, none presenting    -> loading
//   compiles of 100 ms or more fill half of it                   -> shader stutter
//   100 creates or more overlap it                               -> loading
//   a compile of 10 ms or more overlaps it                       -> shader stutter
//   else                                                         -> other hitch
class StutterFeed {
    public const double HitchMs=50;
    const double WindowMs=60000,KeepMs=75000,SettleMs=3000;const long MaxCsvTail=4L<<20;
    struct Create {public double Start,End,Ms;public long Tid;public bool Presents;}
    public struct Hitch {public double End,Ms;public bool Shader;public double Position;}
    readonly string framesPath,createsPath;readonly long notBeforeUnixMs;
    long launchUnix,framesRead,createsRead,clockUs;double launchStartMs;bool inLaunch,createsCurrent;
    readonly Dictionary<uint,List<double>> chains=new Dictionary<uint,List<double>>();readonly Dictionary<uint,long> chainCounts=new Dictionary<uint,long>();
    readonly List<Create> creates=new List<Create>();readonly List<Hitch> hitches=new List<Hitch>();double classifiedTo,latest;
    public bool Active {get;private set;}
    public int ShaderStutters {get;private set;}
    public double WorstMs {get;private set;}
    public double Fps {get;private set;}
    public StutterFeed(string gameFolder,long notBeforeUnixMs){framesPath=Path.Combine(gameFolder,"scskiller_frames.bin");createsPath=Path.Combine(gameFolder,"scskiller_creates.csv");this.notBeforeUnixMs=notBeforeUnixMs;}
    void Reset(long unix){
        launchUnix=unix;framesRead=0;createsRead=0;clockUs=0;launchStartMs=0;inLaunch=false;createsCurrent=false;chains.Clear();chainCounts.Clear();creates.Clear();hitches.Clear();
        classifiedTo=0;latest=0;ShaderStutters=0;WorstMs=0;Fps=0;Active=false;
    }
    public void Update(){
        if(!File.Exists(framesPath)){if(launchUnix!=0)Reset(0);return;}
        using(var f=new FileStream(framesPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            long length=f.Length;if(length<36){if(launchUnix!=0)Reset(0);return;}
            var head=new byte[36];if(Fill(f,head,36)<36)return;
            if(BitConverter.ToUInt32(head,0)!=0xFFFFFFFF){if(launchUnix!=0)Reset(0);return;}
            long unix=BitConverter.ToInt64(head,4);
            if(unix!=launchUnix||length<framesRead)Reset(unix);
            if(unix<notBeforeUnixMs)return; // an earlier run's file: the recorder hasn't seen a frame of this one yet
            f.Position=framesRead;int count=(int)Math.Min(length-framesRead,16L<<20)&~3;var data=new byte[count];count=Fill(f,data,count)&~3;
            framesRead+=ReadFrames(data,count);
        }
        if(!inLaunch)return;
        ReadCreates();
        List<double> main=MainChain();if(main==null||main.Count<2)return;
        latest=main[main.Count-1];Active=true;
        Classify(main);
        int first=main.FindIndex(delegate(double t){return t>=latest-5000;});Fps=first>=0&&latest-main[first]>0?(main.Count-1-first)*1000.0/(latest-main[first]):0;
        foreach(var chain in chains.Values)chain.RemoveAll(delegate(double t){return t<latest-KeepMs;});
        creates.RemoveAll(delegate(Create c){return c.End<latest-KeepMs;});hitches.RemoveAll(delegate(Hitch h){return h.End<latest-KeepMs;});
    }
    static int Fill(Stream s,byte[] buffer,int count){int read=0;while(read<count){int n=s.Read(buffer,read,count-read);if(n<=0)break;read+=n;}return read;}
    // u32 records: 0xFFFFFFFF + u64 unix ms, u64 us since the recorder loaded, u64 QPC, u64 QPC frequency opens a launch;
    // top 4 bits 0-14 a frame of that swap chain after the low 28 bits' microseconds; 15 = that many ms without a frame.
    // Returns the bytes used: a launch record cut off at the end is read next time.
    long ReadFrames(byte[] data,int count){
        int i=0;
        for(;i+4<=count;i+=4){
            uint r=BitConverter.ToUInt32(data,i);
            if(r==0xFFFFFFFF){if(i+36>count)break;launchStartMs=BitConverter.ToInt64(data,i+12)/1000.0;clockUs=0;inLaunch=true;chains.Clear();chainCounts.Clear();i+=32;continue;}
            if(!inLaunch)continue;
            if(r>>28==15){clockUs+=(r&0x0FFFFFFF)*1000L;continue;}
            clockUs+=r&0x0FFFFFFF;uint chain=r>>28;List<double> list;
            if(!chains.TryGetValue(chain,out list)){chains[chain]=list=new List<double>();chainCounts[chain]=0;}
            list.Add(launchStartMs+clockUs/1000.0);chainCounts[chain]++;
        }
        return i;
    }
    List<double> MainChain(){List<double> best=null;long most=0;foreach(var pair in chainCounts)if(pair.Value>most){most=pair.Value;best=chains[pair.Key];}return best;}
    // Rows t_ms,kind,known,tuple_known,ms,key,proxy_ms,tid,presents of the launch whose #session carries the frames' stamp.
    void ReadCreates(){
        if(!File.Exists(createsPath))return;
        using(var f=new FileStream(createsPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            long length=f.Length;if(length<createsRead){createsRead=0;createsCurrent=false;creates.Clear();}
            bool midLine=false;if(createsRead==0&&length>MaxCsvTail){createsRead=length-MaxCsvTail;midLine=true;} // a long csv: its tail, from the next line
            f.Position=createsRead;var data=new byte[(int)Math.Min(length-createsRead,MaxCsvTail)];int count=Fill(f,data,data.Length);
            int end=Array.LastIndexOf(data,(byte)'\n',Math.Max(0,count-1));if(count==0||end<0)return;
            int start=midLine?Array.IndexOf(data,(byte)'\n')+1:0;
            createsRead+=end+1;
            foreach(string raw in System.Text.Encoding.UTF8.GetString(data,start,Math.Max(0,end+1-start)).Split('\n')){
                string line=raw.TrimEnd('\r');if(line.Length==0)continue;string[] p=line.Split(',');
                if(line[0]=='#'){
                    long stamp;
                    if(p[0]=="#session")createsCurrent=p.Length>1&&long.TryParse(p[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out stamp)&&stamp==launchUnix;
                    else if(p[0]=="#end")createsCurrent=false;
                    continue;
                }
                double t,ms;long tid=0;
                if(!createsCurrent||p.Length<5||!double.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out t)||!double.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out ms))continue;
                if(p.Length>7)long.TryParse(p[7],NumberStyles.Integer,CultureInfo.InvariantCulture,out tid);
                creates.Add(new Create{Start=t-ms,End=t,Ms=ms,Tid=tid,Presents=p.Length>8&&p[8]=="1"});
            }
        }
    }
    void Classify(List<double> main){
        double until=latest-SettleMs;
        for(int i=1;i<main.Count;i++){
            double end=main[i],ms=end-main[i-1];if(end<=classifiedTo||end>until||ms<HitchMs)continue;
            bool? shader=Cause(end-ms,end,ms);if(shader==null)continue;
            hitches.Add(new Hitch{End=end,Ms=ms,Shader=shader.Value});
            if(shader.Value){ShaderStutters++;if(ms>WorstMs)WorstMs=ms;}
        }
        if(until>classifiedTo)classifiedTo=until;
    }
    bool? Cause(double start,double end,double ms){
        int overlapping=0;bool compile=false,presenting=false;double cold=0;var threads=new HashSet<long>();
        foreach(var c in creates){
            if(c.End<=start||c.Start>=end)continue;overlapping++;
            if(c.Ms>=10){compile=true;threads.Add(c.Tid);if(c.Presents)presenting=true;}
            if(c.Ms>=100)cold+=Math.Min(c.End,end)-Math.Max(c.Start,start);
        }
        if(overlapping==0&&ms>=5000)return null;
        if(ms>=1000)return false;
        if(threads.Count>=8&&!presenting)return false;
        if(cold>=ms/2)return true;
        if(overlapping>=100)return false;
        return compile;
    }
    /// <summary>The slowest frame per column over the last 60 s, oldest first; -1 = no frame ended in that column.</summary>
    public double[] Columns(int n){
        var result=new double[n];for(int i=0;i<n;i++)result[i]=-1;
        List<double> main=MainChain();if(main==null)return result;
        for(int i=1;i<main.Count;i++){
            double age=latest-main[i];if(age>WindowMs)continue;
            int column=Math.Min(n-1,(int)((1-age/WindowMs)*(n-1)+0.5));double ms=main[i]-main[i-1];if(ms>result[column])result[column]=ms;
        }
        return result;
    }
    /// <summary>The classified slow frames of the last 60 s, Position 0 (oldest) to 1 (newest).</summary>
    public List<Hitch> Recent(){
        var result=new List<Hitch>();
        foreach(var h in hitches){double age=latest-h.End;if(age>WindowMs)continue;var copy=h;copy.Position=1-age/WindowMs;result.Add(copy);}
        return result;
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
