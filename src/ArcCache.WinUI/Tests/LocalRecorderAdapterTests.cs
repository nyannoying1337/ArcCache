using ArcCache.Core;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

static class LocalRecorderAdapterTests {
    static Dictionary<int,(string Library,string Function)> Imports(byte[] image) {
        using var pe=new PEReader(new MemoryStream(image));var headers=pe.PEHeaders;
        int Offset(int rva){foreach(var s in headers.SectionHeaders)if(rva>=s.VirtualAddress&&rva<s.VirtualAddress+s.SizeOfRawData)return s.PointerToRawData+rva-s.VirtualAddress;throw new InvalidDataException();}
        uint U32(int at)=>BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at,4));
        ulong U64(int at)=>BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at,8));
        string Name(int rva){int at=Offset(rva),end=Array.IndexOf(image,(byte)0,at);return Encoding.ASCII.GetString(image,at,end-at);}
        int directory=Offset(headers.PEHeader!.ImportTableDirectory.RelativeVirtualAddress);var result=new Dictionary<int,(string,string)>();
        for(int d=directory;U32(d+12)!=0;d+=20){
            string library=Name((int)U32(d+12));int names=(int)U32(d),iat=(int)U32(d+16);
            for(int i=0;;i++){
                ulong value=U64(Offset(names+i*8));if(value==0)break;
                string function=(value&0x8000000000000000)!=0?"#"+(value&65535):Name((int)value+2);
                result.Add(iat+i*8,(library,function));
            }
        }
        return result;
    }
    public static void Run(Action<string,Action> test,Action<bool,string> check) {
        test("Local recorder adapter preserves supplied code and redirects only Windows loader imports",()=>{
            string? recorder=null;for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null&&recorder==null;d=d.Parent){string p=Path.Combine(d.FullName,"out","SCSKiller","native","d3d12.dll");if(File.Exists(p))recorder=p;}
            if(recorder==null)throw new Exception("Build the public SCSKiller recorder before running adapter tests.");
            string folder=Path.Combine(Path.GetTempPath(),"ArcLocalAdapter-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            try{
                byte[] original=File.ReadAllBytes(recorder);var before=Imports(original);string output=Path.Combine(folder,"adapted.dll");
                LocalRecorderAdapter.Prepare(recorder,output);byte[] adapted=File.ReadAllBytes(output);var after=Imports(adapted);
                check(before.Count==after.Count,"Adaptation added or lost IAT slots.");int replaced=0;
                foreach(var entry in before){
                    check(after.ContainsKey(entry.Key),"An original IAT address moved.");var actual=after[entry.Key];
                    if(entry.Value.Function is "LoadLibraryW" or "LoadLibraryExW"){
                        check(actual.Library==LocalRecorderAdapter.LoaderName&&actual.Function=="ArcCache"+entry.Value.Function,"Loader import was not redirected.");replaced++;
                    }else check(actual==entry.Value,"A non-loader import changed.");
                }
                check(replaced>0,"No Windows loader imports were tested.");
                using var oldPe=new PEReader(new MemoryStream(original));using var newPe=new PEReader(new MemoryStream(adapted));
                var code=oldPe.PEHeaders.SectionHeaders.Single(s=>s.Name==".text");
                check(original.AsSpan(code.PointerToRawData,code.SizeOfRawData).SequenceEqual(adapted.AsSpan(code.PointerToRawData,code.SizeOfRawData)),"The supplied recorder's executable code changed.");
                check(SHA256.HashData(original).SequenceEqual(SHA256.HashData(File.ReadAllBytes(recorder))),"The supplied original was modified.");
                var oldVersion=FileVersionInfo.GetVersionInfo(recorder);var version=FileVersionInfo.GetVersionInfo(output);
                check(version.ProductVersion==oldVersion.ProductVersion&&version.ProductName==oldVersion.ProductName&&LocalRecorderAdapter.IsAdapted(output)&&ScsKillerBridge.IsDotLocalAware(output),"Product identity or adapter metadata is wrong.");
                bool rejected=false;try{LocalRecorderAdapter.RedirectImports(adapted);}catch(InvalidDataException){rejected=true;}check(rejected,"An already adapted recorder was patched again.");
                byte[] signed=(byte[])original.Clone();int optional=oldPe.PEHeaders.PEHeaderStartOffset;
                BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(optional+112+4*8,4),512);
                rejected=false;try{LocalRecorderAdapter.RedirectImports(signed);}catch(InvalidDataException){rejected=true;}check(rejected,"A signed recorder would lose its signature.");
                rejected=false;try{LocalRecorderAdapter.Prepare(recorder,recorder);}catch(InvalidDataException){rejected=true;}check(rejected,"The adapter allowed overwriting the supplied original.");
            }finally{Directory.Delete(folder,true);}
        });
    }
}
