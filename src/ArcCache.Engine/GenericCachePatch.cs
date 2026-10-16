using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ArcCap {
// A discovered, single-image plan. Discovery never writes files; Build only edits the
// caller-provided byte array and refuses layouts or instruction sequences it cannot prove.
public sealed class CachePatchPlan {
 public string Driver,OriginalHash,Reason;
 public int MiB,CapMatch,CapValueOffset,ReloadMatch,ReloadHelperCall,ReadHelperMatch;
 public int CapRva,ReloadRva,HelperRva,SectionRva,FileAlignment,SectionAlignment;
public bool CanBuild,NeedsReloadWrapper,IsXe1Layout;public int CapMatchCount,ReloadMatchCount,ReadHelperMatchCount,HelperCallMatchCount;
}
public static class GenericCachePatch {
 const uint ImageScnMemExecute=0x20000000;
 static readonly byte?[] Cap=Parse("48 89 5C 24 08 57 48 83 EC 20 33 FF 48 C7 41 10 ?? ?? ?? ?? 48 B8 00 00 00 00 01 00 00 00 89 39 48 89 41 08");
 // Xe-HPG's constructor has two PC-relative references between the 512 MiB
 // immediate and the next field writes. Those rel32 operands are the only
 // wildcards; the surrounding instructions remain exact.
 static readonly byte?[] Xe1Cap=Parse("48 89 5C 24 08 57 48 83 EC 20 33 FF 48 C7 41 10 ?? ?? ?? ?? 89 39 48 8D 15 ?? ?? ?? ?? 40 88 79 18 48 B8 00 00 00 00 01 00 00 00 48 89 41 08 48 8B D9 48 89 79 1C 0F 57 C0 40 88 79 26 45 33 C0 66 C7 41 24 01 00 48 83 C1 28 0F 11 01 48 89 79 10 48 89 79 18 E8 ?? ?? ?? ?? 48 8B C3");
 static readonly byte?[] Reload=Parse("48 8B 17 48 8B 8E 10 0C 00 00 E8 ?? ?? ?? ?? 4C 8B F0 48 3B C3 0F 85 ?? ?? ?? ?? 48 8D 54 24 38 48 8B 8E 10 0C 00 00 E8 ?? ?? ?? ?? 48 8B 08 48 89 8E 20 0C 00 00 4D 85 FF 74 03 4D 89 37 48 8D 4C 24 50 E8 ?? ?? ?? ?? 48 8B C7");
 static readonly byte?[] Helper=Parse("40 55 53 57 48 8D 6C 24 C1 48 81 EC C0 00 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 2F 48 8B D9 C7 45 AF 00 00 00 00 48 C7 45 B7 00 00 00 00 48 C7 45 BF 00 00 00 00 48 8B 79 20 8B C7 89 45 C7 48 8B C7 48 C1 E8 20 89 45 CB 48 8B 41 48 48 89 45 CF 48 8D 45 B7 48 89 44 24 20 4C 8D 4D AF 48 8B 49 28");
 static readonly byte[] WideTwo=Engine.Hex("48B800000000010000004889410848D1E848894110893990");
 static readonly byte[] WideFour=Engine.Hex("48B800000000010000004889410848894110893990909090");
 const int TableOffset=104;
 sealed class Section {public int Header;public uint Va,Vs,Raw,RawSize,Flags;}
 sealed class Pe {public int Nt,Opt,Table,Count,OptSize,DirCount,DirOffset,SizeImage,SizeHeaders,FileAlign,SectionAlign,Checksum;public List<Section> Sections;public uint ExRva,ExSize,SecurityOffset,SecuritySize;public int ExRaw;}
 static byte?[] Parse(string s){return s.Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x=="??"?(byte?)null:Convert.ToByte(x,16)).ToArray();}
 static void Need(bool b,string m){if(!b)throw new InvalidDataException(m);}
 static ushort U16(byte[] b,int p){Need(p>=0&&p<=b.Length-2,"Truncated PE header.");return BitConverter.ToUInt16(b,p);}
 static uint U32(byte[] b,int p){Need(p>=0&&p<=b.Length-4,"Truncated PE header.");return BitConverter.ToUInt32(b,p);}
 static void Put16(byte[] b,int p,ushort v){Array.Copy(BitConverter.GetBytes(v),0,b,p,2);}
 static void Put32(byte[] b,int p,uint v){Array.Copy(BitConverter.GetBytes(v),0,b,p,4);}
 static int Align(int n,int a){Need(a>0,"Invalid PE alignment.");return checked((n+a-1)/a*a);}
 static uint Align(uint n,uint a){Need(a>0,"Invalid PE alignment.");return checked((n+a-1)/a*a);}
 // Discovery runs for every size of the same DLL (and Build discovers again), so the pattern hits are remembered per
 // image content (SHA-256), never per array: a changed image always gets a fresh scan.
 static readonly Dictionary<string,Dictionary<byte?[],int[]>> matchMemo=new Dictionary<string,Dictionary<byte?[],int[]>>(StringComparer.Ordinal);
 static int[] Matches(byte[] b,byte?[] pattern,string imageHash){
  lock(matchMemo){Dictionary<byte?[],int[]> known;int[] hit;if(matchMemo.TryGetValue(imageHash,out known)&&known.TryGetValue(pattern,out hit))return (int[])hit.Clone();}
  int[] found=Matches(b,pattern);
  lock(matchMemo){Dictionary<byte?[],int[]> known;if(!matchMemo.TryGetValue(imageHash,out known)){if(matchMemo.Count>=8)matchMemo.Clear();known=new Dictionary<byte?[],int[]>();matchMemo[imageHash]=known;}known[pattern]=(int[])found.Clone();}
  return found;
 }
 static int[] Matches(byte[] b,byte?[] pattern){var found=new List<int>();for(int i=0;i<=b.Length-pattern.Length;i++){bool ok=true;for(int j=0;j<pattern.Length;j++)if(pattern[j].HasValue&&b[i+j]!=pattern[j].Value){ok=false;break;}if(ok)found.Add(i);}return found.ToArray();}
 public static int[] CandidateOffsets(byte[] image,string driver){return Matches(image,String.Equals(driver,Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase)?Xe1Cap:Cap);}
 static Pe ReadPe(byte[] b){
  Need(b.Length>=0x100&&b[0]==0x4d&&b[1]==0x5a,"Not a PE image.");uint nt=U32(b,0x3c);Need((ulong)nt<=(ulong)b.Length-24,"Invalid PE header.");int n=(int)nt;Need(U32(b,n)==0x4550&&U16(b,n+4)==0x8664,"Expected AMD64 PE.");
  int count=U16(b,n+6),optSize=U16(b,n+20),opt=n+24;Need(count>0&&count<97&&optSize>=0xf0&&opt+optSize<=b.Length,"Malformed PE optional header or section count.");Need(U16(b,opt)==0x20b,"Expected PE32+ image.");
  int dirCount=(int)U32(b,opt+108),dir=opt+112;Need(dirCount>=16&&dir+16*8<=opt+optSize,"Missing standard PE data directories.");
  int table=opt+optSize;Need(table+count*40<=b.Length,"Truncated section table.");var sections=new List<Section>();
  for(int i=0;i<count;i++){int h=table+i*40;var s=new Section{Header=h,Vs=U32(b,h+8),Va=U32(b,h+12),RawSize=U32(b,h+16),Raw=U32(b,h+20),Flags=U32(b,h+36)};Need(s.RawSize==0||s.Raw>=U32(b,opt+60)&&((ulong)s.Raw+s.RawSize)<=(ulong)b.Length,"Section raw data is outside the file or overlaps headers.");Need(s.Va!=0||s.Vs==0,"Invalid section virtual address.");sections.Add(s);}
  for(int i=0;i<sections.Count;i++)for(int j=i+1;j<sections.Count;j++){var a=sections[i];var c=sections[j];if(a.RawSize>0&&c.RawSize>0)Need((ulong)a.Raw+a.RawSize<=c.Raw||(ulong)c.Raw+c.RawSize<=a.Raw,"Overlapping PE raw sections.");uint av=Math.Max(a.Vs,a.RawSize),cv=Math.Max(c.Vs,c.RawSize);if(av>0&&cv>0)Need((ulong)a.Va+av<=c.Va||(ulong)c.Va+cv<=a.Va,"Overlapping PE virtual sections.");}
  int fa=checked((int)U32(b,opt+36)),sa=checked((int)U32(b,opt+32));Need(fa>0&&sa>0&&(fa&(fa-1))==0&&(sa&(sa-1))==0&&sa>=fa,"Invalid PE alignment values.");
  var p=new Pe{Nt=n,Opt=opt,OptSize=optSize,Count=count,Table=table,DirCount=dirCount,DirOffset=dir,SizeImage=checked((int)U32(b,opt+56)),SizeHeaders=checked((int)U32(b,opt+60)),FileAlign=fa,SectionAlign=sa,Checksum=opt+64,Sections=sections,ExRva=U32(b,dir+3*8),ExSize=U32(b,dir+3*8+4),SecurityOffset=U32(b,dir+4*8),SecuritySize=U32(b,dir+4*8+4)};
  Need(p.SizeHeaders>0&&p.SizeHeaders<=b.Length,"Invalid SizeOfHeaders.");Need((p.SecurityOffset==0)==(p.SecuritySize==0),"Incomplete PE certificate directory.");
  p.ExRaw=-1;if(p.ExRva!=0||p.ExSize!=0){Need(p.ExRva!=0&&p.ExSize>=12,"Incomplete exception directory.");p.ExRaw=RvaToRaw(p,p.ExRva,checked((int)p.ExSize));}
  return p;
 }
 static int RvaToRaw(Pe p,uint rva,int length){var found=p.Sections.Where(s=>rva>=s.Va&&(ulong)rva+(ulong)length<=(ulong)s.Va+s.RawSize).ToArray();Need(found.Length==1,"RVA does not map uniquely to file-backed section data.");return checked((int)(found[0].Raw+rva-found[0].Va));}
 static Section SectionForRva(Pe p,uint rva){var found=p.Sections.Where(s=>rva>=s.Va&&(ulong)rva<(ulong)s.Va+Math.Max(s.Vs,s.RawSize)).ToArray();Need(found.Length==1,"RVA does not map to exactly one PE section.");return found[0];}
 static uint FileOffsetToRva(Pe p,int off){var found=p.Sections.Where(s=>off>=s.Raw&&(ulong)off<(ulong)s.Raw+s.RawSize).ToArray();Need(found.Length==1,"Signature is outside a unique PE section.");return checked(found[0].Va+(uint)(off-found[0].Raw));}
 static uint RelTarget(byte[] b,Pe p,int opcode){Need(b[opcode]==0xe8,"Expected call instruction.");int rel=BitConverter.ToInt32(b,opcode+1);uint call=FileOffsetToRva(p,opcode);long target=(long)call+5+rel;Need(target>=0&&target<=uint.MaxValue,"Relative call target overflow.");return (uint)target;}
 public static CachePatchPlan Discover(byte[] image,string driver,int mib){bool xe1=String.Equals(driver,Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase);var plan=new CachePatchPlan{Driver=driver,MiB=mib,OriginalHash=Engine.Hash(image),IsXe1Layout=xe1};try{
  Need(mib>=128&&mib<=16384&&mib%128==0,"Choose a cache size from 128 MiB through 16 GiB in 128 MiB steps.");int[] reloadDiscovery=Matches(image,Reload,plan.OriginalHash),helperDiscovery=Matches(image,Helper,plan.OriginalHash);plan.ReloadMatchCount=reloadDiscovery.Length;plan.ReadHelperMatchCount=helperDiscovery.Length;Pe pe=ReadPe(image);byte?[] capPattern=String.Equals(driver,Xe1Patch.FileName,StringComparison.OrdinalIgnoreCase)?Xe1Cap:Cap;int[] cap=Matches(image,capPattern,plan.OriginalHash);plan.CapMatchCount=cap.Length;Need(cap.Length==1,"The cache limit instruction must match exactly once.");int capValue=cap[0]+16;Need(U32(image,capValue)==0x20000000,"The original cache limit is not the expected 512 MiB value; refusing to patch a modified DLL.");uint capRva=FileOffsetToRva(pe,cap[0]);Need((SectionForRva(pe,capRva).Flags&ImageScnMemExecute)!=0,"The cache limit site is not executable.");Need(cap[0]+36<=image.Length,"Truncated cap instruction sequence.");
   plan.CapMatch=cap[0];plan.CapValueOffset=capValue;plan.CapRva=checked((int)capRva);plan.FileAlignment=pe.FileAlign;plan.SectionAlignment=pe.SectionAlign;
  if(mib>=2048){Need(!xe1,"This Xe1 constructor needs a different wide-cap rewrite; sizes above 1 GiB are locked to prevent an unsafe patch.");Need(mib==2048||mib==4096||mib==8192||mib==16384,"Large caps support only 2, 4, 8 or 16 GiB.");Need(cap[0]+36<=image.Length,"Truncated wide-cap constructor.");}
  if(mib>=4096){int[] reload=reloadDiscovery,helper=helperDiscovery;if(reload.Length==1&&helper.Length==1){int[] calls={FindCall(Reload,1),FindCall(Reload,2),FindCall(Reload,3)};uint helperRva=FileOffsetToRva(pe,helper[0]);var helperCalls=calls.Where(i=>RelTarget(image,pe,reload[0]+i)==helperRva).ToArray();plan.HelperCallMatchCount=helperCalls.Length;Need(helperCalls.Length==1,"Exactly one call in the reload site must resolve to the discovered read helper.");int helperCall=reload[0]+helperCalls[0];uint reloadRva=FileOffsetToRva(pe,reload[0]);var codeSec=SectionForRva(pe,reloadRva);Need((codeSec.Flags&ImageScnMemExecute)!=0&&Object.ReferenceEquals(codeSec,SectionForRva(pe,helperRva)),"The reload site and read helper must be in the same executable section.");Need(calls.All(i=>Reload[i]==0xe8),"Reload pattern call layout is invalid.");plan.ReloadMatch=reload[0];plan.ReloadHelperCall=helperCall;plan.ReadHelperMatch=helper[0];plan.ReloadRva=checked((int)reloadRva);plan.HelperRva=checked((int)helperRva);plan.NeedsReloadWrapper=true;}else{Need(reload.Length==1,"The cache reload call site must match exactly once.");Need(helper.Length==1,"The cache read helper must match exactly once.");}
    int header=pe.Table+pe.Count*40;Need(header+40<=pe.SizeHeaders,"No room for an additional section header.");Need(image.Skip(header).Take(40).All(x=>x==0),"New PE section header space is not empty.");Need(!pe.Sections.Any(s=>System.Text.Encoding.ASCII.GetString(image,s.Header,8).TrimEnd('\0')==".arccap"),"The image already contains an Arc Cache section.");Need(pe.ExRaw>=0&& (ulong)pe.ExRaw+pe.ExSize<=(ulong)image.Length,"Exception table is outside the image.");
   int lastRaw=pe.Sections.Where(s=>s.RawSize>0).Max(s=>checked((int)(s.Raw+s.RawSize)));Need(lastRaw<=image.Length,"Last section raw data exceeds the image.");if(pe.SecuritySize>0){Need((ulong)pe.SecurityOffset+pe.SecuritySize==(ulong)image.Length,"Certificate is not the final file data; refusing ambiguous overlay layout.");Need(pe.SecurityOffset==(uint)lastRaw,"Certificate bytes do not begin immediately after the last section raw data.");}else Need(lastRaw==image.Length,"Unsigned image has unclassified overlay data.");
    Need(pe.ExSize%12==0,"Exception directory has an incomplete RUNTIME_FUNCTION entry.");uint lastEnd=0;for(int i=0;i<pe.ExSize/12;i++){int q=pe.ExRaw+i*12;uint begin=U32(image,q),end=U32(image,q+4),unwind=U32(image,q+8);Need(begin<end,"Malformed runtime-function entry.");Need(begin>=lastEnd,"Exception directory entries overlap or are unsorted.");SectionForRva(pe,begin);SectionForRva(pe,end-1);SectionForRva(pe,unwind);lastEnd=end;}
    plan.SectionRva=checked((int)Align((uint)pe.SizeImage,(uint)pe.SectionAlign));
  }
  plan.CanBuild=true;plan.Reason=mib>=4096?"Cap and chunked reload path are uniquely identified; an isolated test is still required.":"Cap site is unique and retains its original 512 MiB value; an isolated test is still required.";
 }catch(Exception e){plan.CanBuild=false;plan.Reason=e.Message;}return plan;}
 static int FindCall(byte?[] pattern,int ordinal){for(int i=0,count=0;i<pattern.Length-4;i++)if(pattern[i]==0xe8){if(++count==ordinal)return i;i+=4;}throw new InvalidDataException("Required relative-call instruction is missing.");}
 public static byte[] Build(byte[] image,int mib,bool legacyCertificateLayout=false){return Build(image,mib,"igd12um64xe2.dll",legacyCertificateLayout);}
 public static byte[] Build(byte[] image,int mib,string driverName,bool legacyCertificateLayout=false){var plan=Discover(image,driverName,mib);Need(plan.CanBuild,plan.Reason);if(mib<2048){byte[] small=(byte[])image.Clone();Put32(small,plan.CapValueOffset,checked((uint)mib*1048576));return small;}Need(!plan.IsXe1Layout,"Wide cap rewrites are only verified for Xe2/Xe3 constructors.");
  byte[] result=(byte[])image.Clone();if(mib==2048){Array.Copy(WideTwo,0,result,plan.CapMatch+12,WideTwo.Length);return result;}
  Pe pe=ReadPe(image);uint virtualSize=checked((uint)(TableOffset+(int)pe.ExSize+12));uint sectionRva=checked((uint)plan.SectionRva);int appendAt;
  if(legacyCertificateLayout){appendAt=Align(image.Length,pe.FileAlign);}
  else {appendAt=Align(checked((int)pe.Sections.Where(s=>s.RawSize>0).Max(s=>s.Raw+s.RawSize)),pe.FileAlign);if(pe.SecuritySize>0){Need(pe.SecurityOffset==appendAt,"Certificate data does not start at the aligned section end.");} }
  int rawSize=Align(checked((int)virtualSize),pe.FileAlign);byte[] output=new byte[checked(appendAt+rawSize)];Array.Copy(image,output,Math.Min(image.Length,appendAt));
  byte[] cap=Engine.Hex("48B800000000000000004889410848894110893990909090");Array.Copy(BitConverter.GetBytes((ulong)mib*1048576),0,cap,2,8);Array.Copy(cap,0,output,plan.CapMatch+12,24);
  // Redirect the unique call that resolves to the discovered read helper.
  Put32(output,plan.ReloadHelperCall+1,unchecked((uint)(sectionRva-(plan.ReloadRva+(uint)(plan.ReloadHelperCall-plan.ReloadMatch)+5))));
  int sh=pe.Table+pe.Count*40;byte[] name=System.Text.Encoding.ASCII.GetBytes(".arccap");Array.Copy(name,0,output,sh,name.Length);Put32(output,sh+8,virtualSize);Put32(output,sh+12,sectionRva);Put32(output,sh+16,(uint)rawSize);Put32(output,sh+20,(uint)appendAt);Put32(output,sh+36,0x60000020);Put16(output,pe.Nt+6,checked((ushort)(pe.Count+1)));Put32(output,pe.Opt+56,Align(sectionRva+virtualSize,(uint)pe.SectionAlign));
  byte[] code=(byte[])LargeCachePatch.Code.Clone();int wrapperCall=Array.IndexOf(code,(byte)0xe8);Need(wrapperCall>=0,"Wrapper call was not found.");Put32(code,wrapperCall+1,unchecked((uint)(plan.HelperRva-(sectionRva+(uint)wrapperCall+5))));Array.Copy(code,0,output,appendAt,code.Length);Array.Copy(LargeCachePatch.Unwind,0,output,appendAt+88,LargeCachePatch.Unwind.Length);
  Array.Copy(image,pe.ExRaw,output,appendAt+TableOffset,(int)pe.ExSize);int tail=appendAt+TableOffset+(int)pe.ExSize;Put32(output,tail,sectionRva);Put32(output,tail+4,sectionRva+(uint)code.Length);Put32(output,tail+8,sectionRva+88);Put32(output,pe.DirOffset+3*8,sectionRva+TableOffset);Put32(output,pe.DirOffset+3*8+4,pe.ExSize+12);
  if(!legacyCertificateLayout){Put32(output,pe.DirOffset+4*8,0);Put32(output,pe.DirOffset+4*8+4,0);}
  Put32(output,pe.Checksum,0);Put32(output,pe.Checksum,Checksum(output,pe.Checksum));return output;
 }
 static uint Checksum(byte[] b,int skip){ulong sum=0;for(int i=0;i<b.Length;i+=2){if(i==skip||i==skip+2)continue;sum+=(uint)b[i]|(uint)(i+1<b.Length?b[i+1]:0)<<8;sum=(sum&65535)+(sum>>16);}sum=(sum&65535)+(sum>>16);return unchecked((uint)(sum+(uint)b.Length));}
 // Stripped-certificate images cannot recreate the Authenticode bytes from the patch alone.
 // Normalization therefore requires the exact original receipt backup and proves the output
 // is the planner's sole expected transformation before returning that canonical original.
 public static byte[] Normalize(byte[] original,byte[] patched,int mib){Need(Build(original,mib).SequenceEqual(patched),"Patched output differs from the exact plan; normalization refused.");return (byte[])original.Clone();}
 public static bool IsExpectedOutput(byte[] original,byte[] output,int mib){try{return Build(original,mib).SequenceEqual(output);}catch{return false;}}
}
}
