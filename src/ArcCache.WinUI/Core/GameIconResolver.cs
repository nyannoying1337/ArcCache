using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;

namespace ArcCache.Core;

/// <summary>Extracts a bounded PNG thumbnail from a local game/launcher executable.</summary>
public static class GameIconResolver {
    const int MaxCachedIcons=256;
    public static string? CacheRootOverride {get;set;}
    public static string? FromExecutable(string? executable) {
        if(string.IsNullOrWhiteSpace(executable)||!Path.IsPathRooted(executable)||executable.StartsWith(@"\\",StringComparison.Ordinal)||!Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase))return null;
        try {
            string full=Path.GetFullPath(executable);var file=new FileInfo(full);if(!file.Exists||file.Length>2L*1024*1024*1024||(file.Attributes&FileAttributes.ReparsePoint)!=0||HasReparseAncestor(Path.GetDirectoryName(full)!))return null;
            string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant()+"\n"+file.Length+"\n"+file.LastWriteTimeUtc.Ticks))).ToLowerInvariant();
            string cacheRoot=CacheRootOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap");string directory=Path.Combine(cacheRoot,"icons");string output=Path.Combine(directory,key+".png");if(Directory.Exists(directory)){if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return null;if(File.Exists(output)){if((File.GetAttributes(output)&FileAttributes.ReparsePoint)!=0)return null;if(new FileInfo(output).Length<=256*1024)return output;}}
            using Icon? icon=Icon.ExtractAssociatedIcon(full);if(icon==null)return null;using Bitmap source=icon.ToBitmap();using var image=new Bitmap(128,128,System.Drawing.Imaging.PixelFormat.Format32bppArgb);using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.Transparent);graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;float scale=Math.Min(128f/source.Width,128f/source.Height);int width=Math.Max(1,(int)(source.Width*scale)),height=Math.Max(1,(int)(source.Height*scale));graphics.DrawImage(source,(128-width)/2,(128-height)/2,width,height);}
            Directory.CreateDirectory(directory);if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return null;
            string pending=output+"."+Guid.NewGuid().ToString("N")+".tmp";try{image.Save(pending,System.Drawing.Imaging.ImageFormat.Png);if(new FileInfo(pending).Length>256*1024){File.Delete(pending);return null;}File.Move(pending,output,true);}finally{if(File.Exists(pending))File.Delete(pending);}
            var cached=Directory.GetFiles(directory,"*.png").Select(p=>new FileInfo(p)).OrderByDescending(p=>p.LastWriteTimeUtc).ToArray();foreach(var stale in cached.Skip(MaxCachedIcons))try{if((stale.Attributes&FileAttributes.ReparsePoint)==0)stale.Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
            return output;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or System.Runtime.InteropServices.ExternalException){return null;}
    }
    static bool HasReparseAncestor(string directory){string? current=Path.GetFullPath(directory);while(!string.IsNullOrEmpty(current)){try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return true;}catch{return true;}string? parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;}return false;}
}
