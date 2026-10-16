using System.Runtime.InteropServices;

namespace ArcCache.Core;

/// <summary>Windows Restart Manager, read only: which processes have a file open. Nothing is closed or restarted.</summary>
public static class RestartManager {
    [StructLayout(LayoutKind.Sequential)] struct UniqueProcess {public int ProcessId;public System.Runtime.InteropServices.ComTypes.FILETIME StartTime;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessInfo {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string ServiceShortName;
        public int ApplicationType;public uint AppStatus;public uint TSSessionId;[MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint session,int flags,string key);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint session,uint files,string[] names,uint processes,IntPtr uniqueProcesses,uint services,string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint session,out uint needed,ref uint count,[In,Out] ProcessInfo[]? info,ref uint rebootReasons);
    const int ErrorMoreData=234;

    /// <summary>Process ids that have <paramref name="path"/> open; empty when none or when Windows can't tell.</summary>
    public static IReadOnlyList<int> Holders(string path) {
        var result=new List<int>();
        if(RmStartSession(out uint session,0,Guid.NewGuid().ToString("N"))!=0)return result;
        try {
            if(RmRegisterResources(session,1,[path],0,IntPtr.Zero,0,null)!=0)return result;
            for(int attempt=0;attempt<3;attempt++){
                uint count=0,reasons=0;int error=RmGetList(session,out uint needed,ref count,null,ref reasons);
                if(error==0)return result;
                if(error!=ErrorMoreData||needed==0)return result;
                var info=new ProcessInfo[needed];count=needed;
                if(RmGetList(session,out needed,ref count,info,ref reasons)==0){for(int i=0;i<count;i++)result.Add(info[i].Process.ProcessId);return result;}
            }
            return result;
        } finally {RmEndSession(session);}
    }
}
