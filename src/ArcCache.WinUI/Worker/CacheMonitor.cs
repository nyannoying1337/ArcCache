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
class CacheMonitor : Form {
    readonly string executable,root,log;readonly CacheSnapshot initial;readonly Label text;readonly Timer timer=new Timer();int gamePid;DateTime started;int sampleCount;string driverStatus="Driver: not checked";string observedModulePath;readonly Func<string,string> inspectDriver;readonly string cacheFile;readonly long limitBytes;readonly DateTime monitorStarted=DateTime.Now;
    struct Rect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,System.Text.StringBuilder path,ref uint length);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x20|0x08000000|0x80;return p;}}
    static string Number(long value){return (value/(1024.0*1024)).ToString("0.00",CultureInfo.InvariantCulture)+" MiB";}
    // cacheFile: this game's Intel cache file when Arc Cache has linked it (64 hex), else null: then the file that grew the
    // most since the overlay started is shown as the likely one. limitMiB: the game's cache size from its setup record.
    public CacheMonitor(string exe,Func<string,string> inspect=null,string cacheRoot=null,string logFolder=null,string linkedCacheFile=null,int limitMiB=512) {
        inspectDriver=inspect;cacheFile=linkedCacheFile!=null&&linkedCacheFile.Length==64&&System.Text.RegularExpressions.Regex.IsMatch(linkedCacheFile,"^[0-9A-Fa-f]{64}$")?linkedCacheFile:null;limitBytes=(long)Math.Max(512,limitMiB)<<20;
        executable=Path.GetFullPath(exe);root=cacheRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Intel","ShaderCache");
        foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))using(p){IntPtr h=OpenProcess(0x1000,false,(uint)p.Id);try{if(h==IntPtr.Zero)continue;var path=new System.Text.StringBuilder(4096);uint n=4096;if(QueryFullProcessImageName(h,0,path,ref n)&&path.ToString().Equals(executable,StringComparison.OrdinalIgnoreCase)){gamePid=p.Id;started=p.StartTime;break;}}finally{if(h!=IntPtr.Zero)CloseHandle(h);}}
        if(gamePid==0)throw new InvalidOperationException("Launch the selected game before starting the overlay.");
        initial=CacheSnapshot.Read(root);string logs=logFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","logs");Directory.CreateDirectory(logs);log=Path.Combine(logs,"cache-monitor-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".csv");
        File.WriteAllText(log,"utc,pid,shared_intel_bytes,net_growth_bytes,changed_files,unreadable_files,private_bytes,working_set_bytes\r\n");
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.FromArgb(25,28,35);Opacity=0.88;ClientSize=new Size(520,236);
        text=new Label{Dock=DockStyle.Fill,Padding=new Padding(12),ForeColor=Color.White,Font=new Font("Segoe UI",11),BackColor=BackColor};Controls.Add(text);
        timer.Interval=2000;timer.Tick+=delegate{Sample();};Shown+=delegate{Sample();timer.Start();};FormClosed+=delegate{timer.Dispose();};
    }
    internal Bitmap PreviewImage(){Sample();var image=new Bitmap(Width,Height);using(var g=Graphics.FromImage(image)){g.Clear(BackColor);TextRenderer.DrawText(g,text.Text,text.Font,new Rectangle(12,12,Width-24,Height-24),text.ForeColor,TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);}return image;}
    string GameCacheLine(CacheSnapshot now) {
        string name=ActiveFile;string label=detectedFile!=null&&cacheFile!=null&&!detectedFile.Equals(cacheFile,StringComparison.OrdinalIgnoreCase)?"This game's cache file (open in the game; the saved link was another file)":"This game's cache file";
        if(name==null){long best=0;foreach(var pair in now.Sizes){long old;long grew=pair.Value-(initial.Sizes.TryGetValue(pair.Key,out old)?old:0);if(grew>best){best=grew;name=pair.Key;}}label="Likely cache file (grew most)";if(name==null)return "This game's cache file: not linked yet (no file grew)";}
        long size;if(!now.Sizes.TryGetValue(name,out size))return label+": not found";
        long before;long grewSince=size-(initial.Sizes.TryGetValue(name,out before)?before:0);
        double percent=limitBytes>0?size*100.0/limitBytes:0;
        return label+": "+Number(size)+" of "+(limitBytes/(1024.0*1024*1024)).ToString("0.##",CultureInfo.InvariantCulture)+" GiB ("+percent.ToString("0.#",CultureInfo.InvariantCulture)+"%) · "+(grewSince>=0?"+":"")+Number(grewSince)+"\r\n"+CacheVerdict(now,name,size,grewSince);
    }
    // Is the game using its prepared cache? Hits read the file without growing it; misses compile and append. The stock
    // driver trims a file over 512 MiB, so a shrinking file means the private copy isn't in use; another file growing
    // much faster means the game is filling a different cache (another exe name). Read only: sizes, never contents.
    string CacheVerdict(CacheSnapshot now,string name,long size,long grewSince) {
        if(ActiveFile==null)return "Cache use: waiting until the game opens its cache file";
        long before;initial.Sizes.TryGetValue(name,out before);
        if(before>(512L<<20)+(1L<<20)&&size<before-(1L<<20))return "Cache use: WARNING - the file shrank; the normal 512 MB driver may have trimmed it";
        // The verdict follows the driver line above it: with the normal driver the larger limit does not apply at all.
        bool copyLoaded=driverStatus.StartsWith("Game setup: custom copy loaded",StringComparison.Ordinal);
        if(driverStatus.StartsWith("Game setup: normal or other copy loaded",StringComparison.Ordinal))return "Cache use: WARNING - the normal driver is loaded, so this game is limited to 512 MB";
        string other=null;long otherGrowth=0;foreach(var pair in now.Sizes){if(pair.Key.Equals(name,StringComparison.OrdinalIgnoreCase))continue;long old;long grew=pair.Value-(initial.Sizes.TryGetValue(pair.Key,out old)?old:0);if(grew>otherGrowth){otherGrowth=grew;other=pair.Key;}}
        if(otherGrowth>(64L<<20)&&otherGrowth>Math.Max(grewSince,0)*4)return "Cache use: another cache file grows faster ("+other.Substring(0,8)+"..., +"+Number(otherGrowth)+")";
        if((DateTime.Now-monitorStarted).TotalSeconds<60)return "Cache use: watching... (play for a minute)";
        if(grewSince<(32L<<20))return copyLoaded?"Cache use: GOOD - reading the prepared cache (only +"+Number(Math.Max(grewSince,0))+" new)":"Cache use: little new compiling (+"+Number(Math.Max(grewSince,0))+"), but the driver copy isn't confirmed";
        return "Cache use: the game is still adding shaders (+"+Number(grewSince)+") - normal for new areas";
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
    void Sample() {
        long privateBytes=0,workingSet=0;
        try{using(var p=Process.GetProcessById(gamePid)){if(p.HasExited||p.StartTime!=started){Close();return;}privateBytes=p.PrivateMemorySize64;workingSet=p.WorkingSet64;
            if(sampleCount++%5==0){bool found=false;try{foreach(ProcessModule m in p.Modules)if((m.ModuleName.Equals("igd12um64xe2.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("igd12um64xe3.dll",StringComparison.OrdinalIgnoreCase)||m.ModuleName.Equals("igd12um64xeh.dll",StringComparison.OrdinalIgnoreCase))){found=true;string path=m.FileName;if(path==observedModulePath)break;observedModulePath=path;driverStatus=path.StartsWith(executable+".local\\",StringComparison.OrdinalIgnoreCase)?"Game setup: custom copy loaded":"Game setup: normal or other copy loaded";if(inspectDriver!=null){try{driverStatus+=" · "+inspectDriver(path);}catch{driverStatus+=" · size not checked";}}break;}if(!found){driverStatus="Game setup: not confirmed";observedModulePath=null;}}catch{driverStatus="Game setup: could not be checked";observedModulePath=null;}}
        }}catch{Close();return;}
        IntPtr front=GetForegroundWindow();uint foregroundPid;GetWindowThreadProcessId(front,out foregroundPid);Rect rect;
        if(foregroundPid==gamePid&&GetWindowRect(front,out rect)){Location=new Point(Math.Max(rect.Left,rect.Right-Width-18),rect.Top+42);if(!Visible)Show();}else if(Visible)Hide();
        var now=CacheSnapshot.Read(root);long growth=now.Bytes-initial.Bytes;int changed=now.ChangedSince(initial);
        if(sampleCount%5==1)try{DetectOpenFile(now);}catch(Exception){}
        text.Text="Arc Cache · "+Path.GetFileNameWithoutExtension(executable)+"\r\n"+driverStatus+"\r\n"+GameCacheLine(now)+"\r\nAllocated memory: "+Number(privateBytes)+" · RAM in use: "+Number(workingSet)+"\r\nIntel cache (all apps): "+Number(now.Bytes)+"\r\nSize change: "+(growth>=0?"+":"")+Number(growth)+" · Changed files: "+changed+"\r\nUpdates every 2 seconds · files not readable: "+now.Unreadable+"";
        try{File.AppendAllText(log,DateTime.UtcNow.ToString("o")+","+gamePid+","+now.Bytes+","+growth+","+changed+","+now.Unreadable+","+privateBytes+","+workingSet+"\r\n");}catch(IOException){text.Text+="\r\nLogging unavailable";}catch(UnauthorizedAccessException){text.Text+="\r\nLogging unavailable";}
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
