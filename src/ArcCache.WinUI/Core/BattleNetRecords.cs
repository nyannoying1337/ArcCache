using System.Text;
namespace ArcCache.Core;
// Minimal bounded protobuf reader for Battle.net's install-record fields.
public static class BattleNetRecords {
    sealed record Field(int Number,byte[] Value);
    static ulong Varint(byte[] data,ref int p){ulong result=0;for(int shift=0;shift<70;shift+=7){if(p>=data.Length)throw new InvalidDataException("Truncated Battle.net varint.");byte b=data[p++];if(shift==63&&b>1)throw new InvalidDataException("Battle.net integer overflow.");result|=(ulong)(b&127)<<shift;if((b&128)==0)return result;}throw new InvalidDataException("Invalid Battle.net integer.");}
    static List<Field> Fields(byte[] data){var result=new List<Field>();int p=0,count=0;while(p<data.Length){if(++count>20000)throw new InvalidDataException("Too many Battle.net fields.");ulong tag=Varint(data,ref p);int field=checked((int)(tag>>3)),wire=(int)(tag&7);if(field==0)throw new InvalidDataException("Invalid Battle.net field.");int size;if(wire==0){_=Varint(data,ref p);continue;}if(wire==1)size=8;else if(wire==5)size=4;else if(wire==2){ulong n=Varint(data,ref p);if(n>4*1024*1024)throw new InvalidDataException("Battle.net field exceeds limit.");size=(int)n;}else throw new InvalidDataException("Unsupported Battle.net wire type.");if(size>data.Length-p)throw new InvalidDataException("Truncated Battle.net field.");if(wire==2)result.Add(new(field,data.AsSpan(p,size).ToArray()));p+=size;}return result;}
    static string? Text(List<Field> fields,int number){var field=fields.FirstOrDefault(f=>f.Number==number);if(field==null)return null;return new UTF8Encoding(false,true).GetString(field.Value);}
    public static IReadOnlyList<InstalledEntry> Parse(byte[] data){
        if(data.Length>4*1024*1024)throw new InvalidDataException("Battle.net database exceeds limit.");var result=new List<InstalledEntry>();
        foreach(var product in Fields(data).Where(f=>f.Number==1).Take(10000)){
            var fields=Fields(product.Value);string? id=Text(fields,1);var install=fields.FirstOrDefault(f=>f.Number==3);if(id==null||install==null||id is "agent" or "battle.net" or "bna")continue;
            string? path=Text(Fields(install.Value),1);if(path==null||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal)||!Directory.Exists(path))continue;
            if(!File.Exists(Path.Combine(path,".build.info")))continue;
            string name=id switch{"wow"=>"World of Warcraft","diablo3"=>"Diablo III","fenris"=>"Diablo IV","s2"=>"StarCraft II","prometheus"=>"Overwatch","w3"=>"Warcraft III",_=>Path.GetFileName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))};
            result.Add(new("Battle.net",id,name,path));
        }return result;
    }
}
