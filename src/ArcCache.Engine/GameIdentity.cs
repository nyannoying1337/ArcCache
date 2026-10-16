using System;using System.IO;using System.Linq;using System.Security.Cryptography;using System.Text;
namespace ArcCap {
public static class GameIdentity {
 public static readonly string[] Sources={"Steam","Epic","Battle.net","GOG","Ubisoft","EA","Xbox","Riot","Amazon","Rockstar","Legendary","Manual","HoYoPlay","Kuro"};
 public static string Key(uint steamId,string source,string nativeId,string root){
  source=String.IsNullOrEmpty(source)?"Steam":source;
  if(!Sources.Contains(source,StringComparer.Ordinal))throw new InvalidDataException("Unknown library source.");
  if(source=="Steam"){if(steamId==0)throw new InvalidDataException("Steam app identity required.");return "steam-"+steamId.ToString(System.Globalization.CultureInfo.InvariantCulture);}
  if(steamId!=0||String.IsNullOrWhiteSpace(nativeId)||nativeId.Length>2048||!Path.IsPathRooted(root??""))throw new InvalidDataException("Invalid non-Steam game identity.");
  string full=Path.GetFullPath(root??"").TrimEnd(Path.DirectorySeparatorChar);if(full.Length<=(Path.GetPathRoot(full)??"").TrimEnd(Path.DirectorySeparatorChar).Length)throw new InvalidDataException("A game installation cannot be a drive root.");
  using(var sha=SHA256.Create()){string value=source+"\n"+nativeId+"\n"+full.ToUpperInvariant();return source.ToLowerInvariant().Replace(".","")+"-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant().Substring(0,24);}
 }
 public static bool ValidKey(string key){return System.Text.RegularExpressions.Regex.IsMatch(key??"",@"\A(?:steam-[1-9][0-9]{0,9}|(?:epic|battlenet|gog|ubisoft|ea|xbox|riot|amazon|rockstar|legendary|manual|hoyoplay|kuro)-[a-f0-9]{24})\z");}
}
}
