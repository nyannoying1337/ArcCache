using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ArcCache.Core;

/// <summary>Adapts an unsigned, user-provided x64 recorder locally. No recorder code is compiled into Arc Cache.
/// Only Windows loader imports and the local copy's compatibility/version metadata change. The source is never written.
/// PE import descriptors: https://learn.microsoft.com/windows/win32/debug/pe-format#import-directory-table</summary>
public static class LocalRecorderAdapter {
    public const string LoaderName="ArcCache.Loader.dll";
    public const string SystemFolder="ArcCache.System";
    public const string SystemDll="d3d12.arccache-system.dll";
    public const string Marker="exe.local-aware";
    public static bool IsAdapted(string? file)=>file!=null&&File.Exists(file)&&System.Diagnostics.FileVersionInfo.GetVersionInfo(file).PrivateBuild=="Arc Cache local loader adapter";
    public static void Prepare(string source,string destination) {
        if(Path.GetFullPath(source).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The supplied recorder must stay unchanged.");
        byte[] original=File.ReadAllBytes(source);Validate(original);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.Copy(source,destination,false);
        UpdateVersion(destination);
        File.WriteAllBytes(destination,RedirectImports(File.ReadAllBytes(destination)));
    }
    static ushort U16(byte[] b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at,2));
    static uint U32(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at,4));
    static ulong U64(byte[] b,int at)=>BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at,8));
    static void W16(byte[] b,int at,int n)=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at,2),checked((ushort)n));
    static void W32(byte[] b,int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at,4),n);
    static int Align(int n,int a)=>checked((n+a-1)/a*a);
    static int Validate(byte[] b) {
        if(b.Length<256||b.Length>128*1024*1024||U16(b,0)!=0x5a4d)throw new InvalidDataException("Not a supported recorder DLL.");
        int pe=checked((int)U32(b,0x3c));
        if(pe<64||pe>b.Length-264||U32(b,pe)!=0x4550||U16(b,pe+4)!=0x8664||U16(b,pe+24)!=0x20b)throw new InvalidDataException("The local adapter requires an x64 PE32+ DLL.");
        if((U16(b,pe+22)&0x2000)==0||U16(b,pe+20)<240)throw new InvalidDataException("Not a supported x64 DLL.");
        // Do not invalidate an Authenticode signature. A signed supplied recorder needs native support instead.
        if(U32(b,pe+24+112+4*8)!=0||U32(b,pe+24+112+4*8+4)!=0)throw new InvalidDataException("This recorder is signed; Arc Cache leaves signed files unchanged. Use its native driver-copy support or turn recording off.");
        return pe;
    }
    static string Ascii(byte[] b,int at) {
        int end=at;while(end<b.Length&&end-at<512&&b[end]!=0)end++;
        if(end>=b.Length||end-at>=512)throw new InvalidDataException("Invalid PE import name.");
        return Encoding.ASCII.GetString(b,at,end-at);
    }
    public static byte[] RedirectImports(byte[] b) {
        try{return RewriteImports(b);}catch(Exception e) when(e is ArgumentException or IndexOutOfRangeException or OverflowException){throw new InvalidDataException("Unsupported or malformed recorder import table. The supplied original is unchanged.",e);}
    }
    static byte[] RewriteImports(byte[] b) {
        int pe=Validate(b),opt=pe+24,sections=U16(b,pe+6),table=opt+U16(b,pe+20);
        if(sections is <1 or >96||table+sections*40>b.Length)throw new InvalidDataException("Invalid PE sections.");
        int fileAlign=checked((int)U32(b,opt+36)),sectionAlign=checked((int)U32(b,opt+32));
        if(fileAlign<512||fileAlign>65536||(fileAlign&(fileAlign-1))!=0||sectionAlign<fileAlign||(sectionAlign&(sectionAlign-1))!=0)throw new InvalidDataException("Unsupported PE alignment.");
        int Rva(uint rva,int size=1) {
            for(int s=0;s<sections;s++){
                int h=table+s*40;uint start=U32(b,h+12),rawSize=U32(b,h+16),raw=U32(b,h+20);
                if(rva>=start&&(ulong)rva+checked((uint)size)<=(ulong)start+rawSize){int at=checked((int)(raw+rva-start));if(at>=0&&at<=b.Length-size)return at;}
            }
            throw new InvalidDataException("Import address is outside the PE image.");
        }
        uint nextRva=0;int firstRaw=b.Length;
        for(int s=0;s<sections;s++){
            int h=table+s*40;if(Ascii(b,h)==".arcimp")throw new InvalidDataException("This is already a local adapter copy; select the original recorder.");
            nextRva=Math.Max(nextRva,checked(U32(b,h+12)+Math.Max(U32(b,h+8),U32(b,h+16))));
            if(U32(b,h+16)>0)firstRaw=Math.Min(firstRaw,checked((int)U32(b,h+20)));
        }
        int newHeader=table+sections*40;
        if(newHeader+40>firstRaw||newHeader+40>U32(b,opt+60))throw new InvalidDataException("The recorder has no spare PE section header for a local adapter.");
        int import=Rva(U32(b,opt+120),20);
        var descriptors=new List<byte[]>();
        var redirected=new List<(byte[] Descriptor,List<ulong> Names,string? Function)>();
        for(int i=0;i<256;i++){
            int at=checked(import+i*20);if(at>b.Length-20)throw new InvalidDataException("Truncated import directory.");
            byte[] d=b.AsSpan(at,20).ToArray();if(d.All(x=>x==0))break;
            uint names=U32(d,0),iat=U32(d,16);string library=Ascii(b,Rva(U32(d,12)));
            bool windows=library.Equals("KERNEL32.dll",StringComparison.OrdinalIgnoreCase)||library.Equals("KERNELBASE.dll",StringComparison.OrdinalIgnoreCase)||library.StartsWith("api-ms-win-core-libraryloader-",StringComparison.OrdinalIgnoreCase);
            if(!windows){descriptors.Add(d);continue;}
            if(names==0)throw new InvalidDataException("The recorder's loader imports have no name table.");
            var run=new List<ulong>();int runStart=0;
            for(int n=0;n<8192;n++){
                ulong thunk=U64(b,Rva(checked(names+(uint)n*8),8));
                string? function=thunk!=0&&(thunk&0x8000000000000000)==0?Ascii(b,Rva(checked((uint)thunk),3)+2):null;
                bool replace=function is "LoadLibraryW" or "LoadLibraryExW";
                if(thunk==0||replace){
                    if(run.Count>0){byte[] part=(byte[])d.Clone();W32(part,16,checked(iat+(uint)runStart*8));redirected.Add((part,run,null));run=[];}
                    if(thunk==0)break;
                    byte[] loader=(byte[])d.Clone();W32(loader,16,checked(iat+(uint)n*8));redirected.Add((loader,[thunk],"ArcCache"+function));runStart=n+1;
                }else{if(run.Count==0)runStart=n;run.Add(thunk);}
                if(n==8191)throw new InvalidDataException("Unterminated import table.");
            }
            if(i==255)throw new InvalidDataException("Unterminated import directory.");
        }
        if(!redirected.Any(r=>r.Function!=null))throw new InvalidDataException("This recorder does not use a supported Windows loader import. Its original file is unchanged.");
        int descriptorCount=descriptors.Count+redirected.Count;
        uint baseRva=checked((uint)Align(checked((int)nextRva),sectionAlign));
        using var payload=new MemoryStream();using var writer=new BinaryWriter(payload);
        writer.Write(new byte[(descriptorCount+1)*20]);
        uint Put(byte[] bytes,int alignment=1){while(payload.Position%alignment!=0)writer.Write((byte)0);uint rva=checked(baseRva+(uint)payload.Position);writer.Write(bytes);return rva;}
        uint loaderName=Put(Encoding.ASCII.GetBytes(LoaderName+"\0"));
        foreach(var part in redirected){
            byte[] d=part.Descriptor;var names=part.Names;
            if(part.Function!=null){uint name=Put([0,0,..Encoding.ASCII.GetBytes(part.Function+"\0")],2);names=[name];W32(d,12,loaderName);}
            using var nt=new MemoryStream();using(var nw=new BinaryWriter(nt,Encoding.UTF8,true)){foreach(ulong n in names)nw.Write(n);nw.Write(0UL);}
            W32(d,0,Put(nt.ToArray(),8));W32(d,4,0);W32(d,8,0);descriptors.Add(d);
        }
        payload.Position=0;foreach(byte[] d in descriptors)writer.Write(d);
        byte[] data=payload.ToArray();int rawAt=Align(b.Length,fileAlign),rawSize=Align(data.Length,fileAlign);
        byte[] output=new byte[checked(rawAt+rawSize)];b.CopyTo(output,0);data.CopyTo(output,rawAt);
        output.AsSpan(newHeader,40).Clear();Encoding.ASCII.GetBytes(".arcimp").CopyTo(output,newHeader);
        W32(output,newHeader+8,checked((uint)data.Length));W32(output,newHeader+12,baseRva);W32(output,newHeader+16,checked((uint)rawSize));W32(output,newHeader+20,checked((uint)rawAt));W32(output,newHeader+36,0x40000040);
        W16(output,pe+6,sections+1);W32(output,opt+56,checked((uint)Align(checked((int)baseRva+data.Length),sectionAlign)));W32(output,opt+8,checked(U32(b,opt+8)+(uint)rawSize));W32(output,opt+64,0);
        W32(output,opt+120,baseRva);W32(output,opt+124,checked((uint)(descriptorCount+1)*20));
        // The original IAT is kept at its original addresses. Its slots are written by the new import descriptors.
        W32(output,opt+112+11*8,0);W32(output,opt+112+11*8+4,0); // invalidate any bound-import cache
        return output;
    }

    sealed class VersionBlock {
        public ushort Type;public string Key="";public byte[] Value=[];public List<VersionBlock> Children=[];
        public static VersionBlock Read(byte[] b,int at,int limit,int depth=0) {
            if(depth>8||at>limit-6)throw new InvalidDataException("Invalid version resource.");
            int length=U16(b,at),count=U16(b,at+2),cursor=at+6,end=checked(at+length);ushort type=U16(b,at+4);
            if(length<6||end>limit)throw new InvalidDataException("Invalid version resource length.");
            int start=cursor;while(cursor<end-1&&U16(b,cursor)!=0)cursor+=2;
            if(cursor>=end-1)throw new InvalidDataException("Invalid version key.");
            string key=Encoding.Unicode.GetString(b,start,cursor-start);cursor=Align(cursor+2,4);int bytes=checked(count*(type==1?2:1));
            if(cursor>end-bytes)throw new InvalidDataException("Invalid version value.");
            var node=new VersionBlock{Type=type,Key=key,Value=b.AsSpan(cursor,bytes).ToArray()};cursor=Align(cursor+bytes,4);
            while(cursor<end-5&&U16(b,cursor)>0){var child=Read(b,cursor,end,depth+1);node.Children.Add(child);cursor=Align(cursor+U16(b,cursor),4);}
            return node;
        }
        public byte[] Write() {
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write((ushort)0);writer.Write(checked((ushort)(Type==1?Value.Length/2:Value.Length)));writer.Write(Type);writer.Write(Encoding.Unicode.GetBytes(Key+"\0"));
            void Pad(){while(stream.Position%4!=0)writer.Write((byte)0);}
            Pad();writer.Write(Value);foreach(var child in Children){Pad();writer.Write(child.Write());}
            byte[] bytes=stream.ToArray();W16(bytes,0,bytes.Length);return bytes;
        }
        public void String(string key,string value) {
            var node=Children.FirstOrDefault(c=>c.Key==key);if(node==null){node=new VersionBlock{Type=1,Key=key};Children.Add(node);}node.Value=Encoding.Unicode.GetBytes(value+"\0");
        }
    }
    delegate bool LanguageCallback(IntPtr module,IntPtr type,IntPtr name,ushort language,IntPtr parameter);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryExW(string file,IntPtr reserved,uint flags);
    [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool EnumResourceLanguagesW(IntPtr module,IntPtr type,IntPtr name,LanguageCallback callback,IntPtr parameter);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr FindResourceExW(IntPtr module,IntPtr type,IntPtr name,ushort language);
    [DllImport("kernel32.dll")]static extern uint SizeofResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32.dll")]static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32.dll")]static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr BeginUpdateResourceW(string file,bool deleteExisting);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool UpdateResourceW(IntPtr handle,IntPtr type,IntPtr name,ushort language,byte[] data,uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool EndUpdateResourceW(IntPtr handle,bool discard);
    static void UpdateVersion(string file) {
        IntPtr module=LoadLibraryExW(file,IntPtr.Zero,0x22);if(module==IntPtr.Zero)throw new Win32Exception();
        var resources=new List<(ushort Language,byte[] Bytes)>();
        try{
            LanguageCallback callback=(_,_,_,language,_)=>{
                IntPtr resource=FindResourceExW(module,new IntPtr(16),new IntPtr(1),language);uint size=SizeofResource(module,resource);
                if(size<52||size>1024*1024)throw new InvalidDataException("Unsupported recorder version resource.");
                byte[] bytes=new byte[size];Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length);resources.Add((language,bytes));return true;
            };
            if(!EnumResourceLanguagesW(module,new IntPtr(16),new IntPtr(1),callback,IntPtr.Zero))throw new Win32Exception();
        }finally{FreeLibrary(module);}
        IntPtr update=BeginUpdateResourceW(file,false);if(update==IntPtr.Zero)throw new Win32Exception();bool committed=false;
        try{
            foreach(var entry in resources){
                var root=VersionBlock.Read(entry.Bytes,0,entry.Bytes.Length);
                if(root.Key!="VS_VERSION_INFO"||root.Value.Length<52||U32(root.Value,0)!=0xfeef04bd)throw new InvalidDataException("Unsupported fixed version information.");
                uint ms=U32(root.Value,8),ls=U32(root.Value,12);if(ls==uint.MaxValue)throw new InvalidDataException("Recorder version cannot be advanced safely.");ls++;
                W32(root.Value,12,ls);string version=$"{ms>>16}.{ms&65535}.{ls>>16}.{ls&65535}";
                foreach(var table in root.Children.Where(c=>c.Key=="StringFileInfo").SelectMany(c=>c.Children)){table.String("Comments",Marker);table.String("FileVersion",version);table.String("PrivateBuild","Arc Cache local loader adapter");}
                byte[] bytes=root.Write();if(!UpdateResourceW(update,new IntPtr(16),new IntPtr(1),entry.Language,bytes,checked((uint)bytes.Length)))throw new Win32Exception();
            }
            if(!EndUpdateResourceW(update,false))throw new Win32Exception();committed=true;
        }finally{if(!committed)EndUpdateResourceW(update,true);}
    }
}
