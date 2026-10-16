using System.Text.Json;
namespace ArcCache.Core;
public sealed record SafetyPresentation(string Title,string Message,bool Warn) {
    public static bool RequiresPerGameAcknowledgement(JsonElement assessment,string source,int mib,bool trialMode,bool assessmentAvailable=true) =>
        trialMode || !assessmentAvailable || assessment.ValueKind!=JsonValueKind.Object || !assessment.GetProperty("Allowed").GetBoolean() ||
        ((source is "HoYoPlay" or "Kuro") && mib>512);
    public static SafetyPresentation From(JsonElement assessment,string source) {
        if(source is "HoYoPlay" or "Kuro")return new("Likely kernel anti-cheat",$"{source} games may use kernel anti-cheat. A modified Intel driver could block the game or risk your account. Only continue if you accept that risk; use Default for online play.",true);
        return From(assessment);
    }
    public static SafetyPresentation From(JsonElement assessment) {
        if(assessment.GetProperty("Allowed").GetBoolean())return new("","",false);
        string reason=assessment.GetProperty("Message").GetString()??"";
        bool antiCheat=assessment.GetProperty("Marker").ValueKind==JsonValueKind.String||reason.Contains("Valve Anti-Cheat",StringComparison.OrdinalIgnoreCase);
        bool multiplayer=(assessment.GetProperty("Multiplayer").GetString()??"").Contains("multiplayer/co-op",StringComparison.OrdinalIgnoreCase);
        return antiCheat
            ? new("This game uses anti-cheat","A modified driver may stop the game from starting or trigger an anti-cheat warning. Use the original driver for online play.",true)
            : multiplayer
                ? new("Online or co-op play may be affected","A modified driver may stop the game from starting or cause account penalties. Use the original driver for online play.",true)
                : new("Game safety is unknown","Arc Cache could not confirm whether this game uses anti-cheat or online play. A modified driver may stop it from starting or risk your account. Continue only if you accept that risk.",true);
    }
}
