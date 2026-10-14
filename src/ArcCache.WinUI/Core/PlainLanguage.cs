namespace ArcCache.Core;
public static class PlainLanguage {
    public static string Error(string message) {
        if(message.Contains("administrator",StringComparison.OrdinalIgnoreCase))return message.Contains("normally",StringComparison.OrdinalIgnoreCase)||message.Contains("without",StringComparison.OrdinalIgnoreCase)?"Open Arc Cache normally for this check. Administrator rights are not needed.":"For this Windows setting only, close Arc Cache and open it with Run as administrator. Confirm the change and restart Windows.";
        if(message.Contains("Close ",StringComparison.OrdinalIgnoreCase))return "Close the game, then try this step again.";
        if(message.Contains("risk acknowledgment",StringComparison.OrdinalIgnoreCase)||message.Contains("confirmation",StringComparison.OrdinalIgnoreCase))return "Read the warning and confirm before continuing.";
        if(message.Contains("hash",StringComparison.OrdinalIgnoreCase)||message.Contains("cohort",StringComparison.OrdinalIgnoreCase)||message.Contains("receipt",StringComparison.OrdinalIgnoreCase)||message.Contains("ACL",StringComparison.OrdinalIgnoreCase))return "Some setup files or their saved record could not be checked. Stop here and open the setup details before trying again.";
        if(message.Contains("restart",StringComparison.OrdinalIgnoreCase)||message.Contains("Reboot",StringComparison.OrdinalIgnoreCase))return "Restart Windows before trying this step again.";
        if(message.Contains("not supported",StringComparison.OrdinalIgnoreCase)||message.Contains("Unknown",StringComparison.OrdinalIgnoreCase)||message.Contains("Unsupported",StringComparison.OrdinalIgnoreCase)||message.Contains("Unqualified",StringComparison.OrdinalIgnoreCase))return "This driver or game version is not supported yet. Use default settings and check support on the Graphics page.";
        return "This step could not be completed. No further changes will be attempted. Open Recent actions for the full reason.";
    }
    public static string GameCheck(string report,string executable) {
        if(report.Contains("not running",StringComparison.OrdinalIgnoreCase))return "Start this game, then choose Check game setup again.";
        if(report.Contains(executable+".local\\",StringComparison.OrdinalIgnoreCase))return "The game loaded its own driver copy. Cache performance has not been measured.";
        if(report.Contains("Disk SHA-256",StringComparison.OrdinalIgnoreCase))return "The game loaded a driver outside its custom setup. Open the details to check its location.";
        return "Arc Cache could not confirm which driver the game loaded. Open the details below.";
    }
}
