namespace ArcCache.Core;
public static class PlainLanguage {
    public static string Progress(string message) {
        if(message.StartsWith("Checking the source",StringComparison.OrdinalIgnoreCase))return "Checking the selected Intel graphics files";
        if(message.StartsWith("Saving backup",StringComparison.OrdinalIgnoreCase))return "Saving a safe backup";
        if(message.StartsWith("Copying ",StringComparison.OrdinalIgnoreCase))return "Making a separate test copy";
        if(message.StartsWith("Test files and backup checked",StringComparison.OrdinalIgnoreCase))return "Test copy is ready";
        if(message.StartsWith("Starting the separate graphics check",StringComparison.OrdinalIgnoreCase))return "Starting the graphics test";
        if(message.StartsWith("Waiting for the graphics check",StringComparison.OrdinalIgnoreCase))return "Running the graphics test";
        if(message.StartsWith("Graphics result saved",StringComparison.OrdinalIgnoreCase))return "Graphics test finished. Real game cache use is not measured.";
        return "Working…";
    }
    public static string Error(string message) {
        if(message.Contains("administrator",StringComparison.OrdinalIgnoreCase))return message.Contains("normally",StringComparison.OrdinalIgnoreCase)||message.Contains("without",StringComparison.OrdinalIgnoreCase)?"Open Arc Cache normally for this check. Administrator rights are not needed.":"For this Windows setting only, close Arc Cache and open it with Run as administrator. Confirm the change and restart Windows.";
        if(message.Contains("Enable game-specific settings",StringComparison.OrdinalIgnoreCase))return "Open Graphics, allow game-specific graphics files, restart Windows, then try again.";
        if(message.Contains("not enough free",StringComparison.OrdinalIgnoreCase)||message.Contains("free disk space",StringComparison.OrdinalIgnoreCase))return "There is not enough free disk space for this step. Free some space, then try again.";
        if(message.Contains("Close ",StringComparison.OrdinalIgnoreCase))return "Close the game, then try this step again.";
        if(message.Contains("risk acknowledgment",StringComparison.OrdinalIgnoreCase)||message.Contains("confirmation",StringComparison.OrdinalIgnoreCase))return "Read the warning and confirm before continuing.";
        if(message.Contains("hash",StringComparison.OrdinalIgnoreCase)||message.Contains("cohort",StringComparison.OrdinalIgnoreCase)||message.Contains("receipt",StringComparison.OrdinalIgnoreCase)||message.Contains("ACL",StringComparison.OrdinalIgnoreCase))return "Some setup files or their saved record could not be checked. Stop here and open the setup details before trying again.";
        if(message.Contains("restart",StringComparison.OrdinalIgnoreCase)||message.Contains("Reboot",StringComparison.OrdinalIgnoreCase))return "Restart Windows before trying this step again.";
        if(message.Contains("not supported",StringComparison.OrdinalIgnoreCase)||message.Contains("Unknown",StringComparison.OrdinalIgnoreCase)||message.Contains("Unsupported",StringComparison.OrdinalIgnoreCase)||message.Contains("Unqualified",StringComparison.OrdinalIgnoreCase))return "This driver or game version is not supported yet. Use default settings and check support on the Graphics page.";
        return "This step could not be completed. No further changes will be attempted. Open Recent actions for the full reason.";
    }
    public static string GameCheck(string report,string executable) {
        if(report.Contains("not running",StringComparison.OrdinalIgnoreCase))return "Start this game, then choose Check game setup again.";
        if(report.Contains(executable+".local\\",StringComparison.OrdinalIgnoreCase))return "The game loaded its own graphics files. Cache performance has not been measured.";
        if(report.Contains("Disk SHA-256",StringComparison.OrdinalIgnoreCase))return "The game loaded graphics files outside its Arc Cache setup. Open the details to check where they came from.";
        return "Arc Cache could not confirm which driver the game loaded. Open the details below.";
    }
}
