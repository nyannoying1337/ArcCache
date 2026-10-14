namespace ArcCache.Core;

// Disk installation and runtime observation are independent states.
public sealed record SetupPresentation(string Installed, string Title, string Next, bool WaitingForControl) {
    public static string Limit(int mib) => mib >= 1024 ? $"{mib / 1024} GiB" : $"{mib} MiB";
    public static SetupPresentation From(GameSetupState? state, bool detected,bool routingReady=true,bool driverCompatible=true) {
        if(state?.CompatibilityRepairRequired==true)
            return new($"{Limit(state.Receipt!.MiB)} set · setup needs updating", "Set up this game again", "Close the game. Choose Use default settings, then set it up again. Changing the size alone will not fix the old setup.", false);
        if (state?.Error != null || state?.FolderExists == true && !state.Configured)
            return new("Setup needs attention", "Fix this setup", "Open Fix a setup or view details below. Use the saved setup record so Arc Cache can check which files belong to this game.", false);
        if (state?.Configured == true && state.Receipt is { } receipt) {
            string installed = $"{Limit(receipt.MiB)} set · setup files checked";
            if(!driverCompatible)return new($"{Limit(receipt.MiB)} saved · graphics driver needs checking","Check your updated graphics driver","Choose Check support on the Graphics page, or use default settings for this game. Your selected size is still saved.",false);
            if(!routingReady)return new($"{Limit(receipt.MiB)} saved · Windows setting is off","Enable game-specific settings","Open Settings, enable game-specific settings and restart Windows. Your selected size is still saved.",false);
            if (!detected) return new(installed, "Game update needs attention", "Use default settings, then start the updated game so Arc Cache can find it again.", false);
            bool control = receipt.MiB == 512 && receipt.Status != "original-control-loading-verified";
            if (control) return new(installed, "Step 2 of 3 · Check the first setup", "Start the game in DirectX 12 mode and keep Arc Cache open. It will check the new setup. Close the game after the check finishes.", true);
            if (receipt.MiB == 512) return new(installed, "Step 3 of 3 · Choose your cache size", "The first setup loaded successfully. Close the game, choose a size below, and apply it.", false);
            if(receipt.MiB>=8192)return new(installed,"Ready to try this setting","Start the game in DirectX 12 mode. Large caches can use lots of memory. Use this app to choose a smaller size or return to default settings.",false);
            return new(installed, "Ready to play", "Play through Steam in DirectX 12 mode. Use Check game setup below if you want to verify which files loaded.", false);
        }
        return detected
            ? new("Default settings · not set up", "Step 1 of 3 · Set up this game", "Arc Cache starts with an unchanged driver copy (about 608 MiB). Your chosen size is applied after that copy is checked.", false)
            : new("Default settings · not set up", "Step 1 of 3 · Start this game", "Start the game in DirectX 12 mode and leave it at the menu. Arc Cache will find the files it needs.", false);
    }
}
