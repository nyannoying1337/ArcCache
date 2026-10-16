using System;using System.IO;using System.Linq;
namespace ArcCap {
// Xe1/Xe-HPG has a different constructor layout. Never infer support from a similar constant.
public static class Xe1Patch {
 public const string FileName="igd12um64xeh.dll";
 public sealed class Definition{public string Version,Original,OneGiB;public int Size,Offset,Rva;public byte[] Context;}
 public static readonly Definition[] Definitions={
  new Definition{Version="32.0.101.9034",Size=63841952,Offset=0x130F940,Rva=0x1310540,Original="FDA1637450E828E5D00F445B127861A599C08CC39FB55172A0107ABBF1A7758E",OneGiB="135D6BFE2EB13BAB24DE231E97AF1CB5563AADA769E84107AAC5162DA8662D97",Context=Engine.Hex("48895c2408574883ec2033ff48c74110000000208939488d158385ee004088791848b8000000000100000048894108488bd94889791c0f57c0408879264533c066c7412401004883c1280f11014889791048897918e8f61dd9fe488bc3897b4866c7434c0001488b5c24304883c4205fc3")},
  new Definition{Version="32.0.101.9150",Size=64555688,Offset=0x1368C20,Rva=0x1369820,Original="D42F3E08F5A2A0CA73D16C6219496CD333427B7CD79B443F4407C2182A8934D1",OneGiB="8B4B1216F636AA120CC09736DDC084811A4561D0FFF7042B5C05FB8AE021C9D0",Context=Engine.Hex("48895c2408574883ec2033ff48c74110000000208939488d1593a8f2004088791848b8000000000100000048894108488bd94889791c0f57c0408879264533c066c7412401004883c1280f11014889791048897918e82699d3fe488bc3897b4866c7434c0001488b5c24304883c4205fc3")},
 };
 static void Require(bool yes,string text){if(!yes)throw new InvalidDataException(text);}
 public static Definition DefinitionFor(byte[] bytes){string hash=Engine.Hash(bytes);return Definitions.FirstOrDefault(d=>d.Size==bytes.Length&&d.Original==hash);}
 static void Layout(byte[] bytes,Definition d){
  Require(bytes.Length==d.Size&&bytes[0]==77&&bytes[1]==90,"Xe1 image size/header differs.");int nt=BitConverter.ToInt32(bytes,60);Require(nt>=64&&nt<bytes.Length-26&&BitConverter.ToUInt32(bytes,nt)==0x4550&&BitConverter.ToUInt16(bytes,nt+4)==0x8664&&BitConverter.ToUInt16(bytes,nt+24)==0x20b,"Expected an AMD64 PE32+ Xe1 driver.");int table=nt+24+BitConverter.ToUInt16(bytes,nt+20),count=BitConverter.ToUInt16(bytes,nt+6),matches=0;Require(count>0&&count<=96&&table>=0&&table<=bytes.Length-count*40,"Invalid Xe1 section table.");
  for(int i=0;i<count;i++){int q=table+i*40;uint va=BitConverter.ToUInt32(bytes,q+12),raw=BitConverter.ToUInt32(bytes,q+20),size=BitConverter.ToUInt32(bytes,q+16),flags=BitConverter.ToUInt32(bytes,q+36);if(va<=d.Rva&&(ulong)d.Rva+(uint)d.Context.Length<=(ulong)va+size){Require((ulong)raw+(uint)d.Rva-va==(ulong)d.Offset&&(flags&0x20000000)!=0,"Xe1 constructor does not map to executable code.");matches++;}}
  Require(matches==1&&bytes.Skip(d.Offset).Take(d.Context.Length).SequenceEqual(d.Context),"Xe1 instruction context changed.");
 }
 public static int[] CandidateOffsets(byte[] bytes){var d=DefinitionFor(bytes);if(d==null)return new int[0];Layout(bytes,d);return new[]{d.Offset};}
 public static byte[] Patch(byte[] original,int mib){
  var plan=GenericCachePatch.Discover(original,FileName,mib);Require(plan.CanBuild,plan.Reason);var result=GenericCachePatch.Build(original,mib,FileName);var d=DefinitionFor(original);
  if(d!=null&&mib==1024)Require(Engine.Hash(result)==d.OneGiB,"Xe1 output differs from the reviewed 1 GiB fixture.");
  return result;
 }
}
}
