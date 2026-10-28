using System;
using System.Diagnostics;
using System.IO;

namespace ArcCache.Core {
// Shared page/overlay state table:
// no recognised recorder -> No recorder; missing/unreadable ini or invalid value -> Unavailable;
// recognised recorder with an ini but no traversal key -> Off (older recorder);
// traversal_suballocate=0 -> Off; traversal_suballocate=1 -> On.
// This reports the recorder setting, not measured effectiveness or runtime pooling.
public sealed class RecorderSettings {
    public readonly bool? TraversalEnabled;
    public readonly string TraversalText;
    RecorderSettings(bool? enabled,string text){TraversalEnabled=enabled;TraversalText=text;}
    public static RecorderSettings FromLines(string[] lines,bool recorder) {
        if(!recorder)return new RecorderSettings(null,"No recorder");
        bool? enabled=false;
        foreach(string raw in lines){string line=raw.Trim();if(line.StartsWith(";",StringComparison.Ordinal)||line.StartsWith("#",StringComparison.Ordinal))continue;
            int eq=line.IndexOf('=');if(eq<0||line.Substring(0,eq).Trim()!="traversal_suballocate")continue;
            string value=line.Substring(eq+1).Trim();enabled=value=="1"?(bool?)true:value=="0"?(bool?)false:null;
        }
        return new RecorderSettings(enabled,enabled==true?"On":enabled==false?"Off":"Unavailable");
    }
    public static RecorderSettings Read(string executable) {
        try {
            string folder=Path.GetDirectoryName(Path.GetFullPath(executable))??String.Empty;
            if(folder.Length==0)return new RecorderSettings(null,"Unavailable");
            string dll=Path.Combine(folder,"d3d12.dll");
            if(!File.Exists(dll))return FromLines(new string[0],false);
            var version=FileVersionInfo.GetVersionInfo(dll);
            if(version.ProductName!="SCSKiller"||version.FileDescription!="SCSKiller D3D12 proxy")return FromLines(new string[0],false);
            string ini=Path.Combine(folder,"scskiller.ini");return File.Exists(ini)?FromLines(File.ReadAllLines(ini),true):new RecorderSettings(null,"Unavailable");
        }catch(IOException){return new RecorderSettings(null,"Unavailable");}catch(UnauthorizedAccessException){return new RecorderSettings(null,"Unavailable");}
    }
}
}
