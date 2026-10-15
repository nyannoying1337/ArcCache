namespace ArcCache.Core;

// Disk installation and runtime observation are independent states.
public sealed record SetupPresentation(string Installed, string Title, string Next, bool WaitingForControl) {
    public static string Limit(int mib) => mib >= 1024 ? $"{mib / 1024} GB" : $"{mib} MB";
    public static SetupPresentation From(GameSetupState? state, bool detected,bool routingReady=true,bool driverCompatible=true) {
        if(state?.CompatibilityRepairRequired==true)
            return new($"{Limit(state.Receipt!.MiB)} set · setup needs updating", "Set up this game again", "Close the game. Choose Use default settings, then set it up again. Changing the size alone will not fix the old setup.", false);
        if (state?.Error != null || state?.FolderExists == true && !state.Configured)
            return new("Setup needs attention", "Fix this setup", "Open Fix a setup or view details below. Use the saved setup record so Arc Cache can check which files belong to this game.", false);
        if (state?.Configured == true && state.Receipt is { } receipt) {
            string installed = $"{Limit(receipt.MiB)} set · setup files checked";
            if(!driverCompatible)return new($"{Limit(receipt.MiB)} saved · graphics driver needs checking","Check your updated graphics driver","Choose Check support on the Graphics page, or use default settings for this game. Your selected size is still saved.",false);
            if(!routingReady)return new($"{Limit(receipt.MiB)} saved · Windows setting is off","Enable game-specific settings","Open Graphics and choose Allow game-specific graphics files. Follow the instructions and restart Windows. Your selected size is still saved.",false);
            if (!detected) return new(installed, "Game update needs attention", "Use default settings, then start the updated game so Arc Cache can find it again.", false);
            bool control = receipt.MiB == 512 && receipt.Status != "original-control-loading-verified";
            if (control) return new(installed, "Check the game setup", "Start the game in DirectX 12 and keep Arc Cache open. It will check that the game can load its separate graphics files. Close the game when the check finishes.", true);
            if (receipt.MiB == 512) return new(installed, "Apply your cache size", "The first setup loaded successfully. Close the game, choose a size below, and apply it.", false);
            if(receipt.MiB>=8192)return new(installed,"Ready to try this setting","Start the game in DirectX 12. Very large caches can use lots of memory. Choose a smaller size or return to default settings here.",false);
            if(receipt.Trial)return new(installed,"Ready to try this game","Start the game in DirectX 12 through its usual launcher. The separate graphics test passed, but game stability and cache growth have not been measured. Choose Use default settings here to undo it.",false);
            return new(installed, "Ready to play", "Start the game in DirectX 12 through its usual launcher. Choose Check game setup below if you want to see which graphics files loaded.", false);
        }
        return detected
            ? new("Default settings · not set up", "Set up this game", "Arc Cache first makes a separate copy of the graphics files without changing them. It checks free space first. The cache size changes only after the copy passes a check.", false)
            : new("Default settings · not set up", "Start this game", "If the game offers a DirectX 12 option, choose it. Leave the game at the main menu and keep Arc Cache open so it can find the game files.", false);
    }
}
