using System.Text.Json;

namespace ArcCache.Core;

/// <summary>On-screen stats settings. AutoStart: open the overlay when a set-up game starts (Arc Cache must be running).
/// Corner: where on the game window it sits; the overlay is click-through, so it moves by corner, not by dragging.</summary>
public sealed record OverlaySettings(bool AutoStart=false,string Corner="top-right") {
    public static readonly IReadOnlyList<(string Tag,string Label)> Corners=[("top-right","Top right"),("top-left","Top left"),("bottom-right","Bottom right"),("bottom-left","Bottom left")];
    public static bool IsCorner(string? tag)=>Corners.Any(c=>c.Tag==tag);
    public static OverlaySettings Load(string path) {
        try{
            if(File.Exists(path)&&JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path)) is {} saved)
                return saved with{Corner=IsCorner(saved.Corner)?saved.Corner:"top-right"};
        }catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
        return new();
    }
    public void Save(string path) {
        if(!IsCorner(Corner))throw new ArgumentException("Unknown overlay corner.",nameof(Corner));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);
    }
}
