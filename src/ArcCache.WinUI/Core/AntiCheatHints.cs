namespace ArcCache.Core;

// What the game list shows before a game is opened. The full safety scan in the worker still runs before any setup;
// this quick look only tells the user early which games are likely to refuse a driver copy.
public enum AntiCheatLevel { NotChecked, NoneFound, Optional, AlwaysOn }

public sealed record AntiCheatHint(AntiCheatLevel Level, string Name = "", string Marker = "", string? SeenWhen = null) {
    public static readonly AntiCheatHint NotChecked = new(AntiCheatLevel.NotChecked);
    // Seen running beats anything the files say.
    public static AntiCheatHint Seen(AntiCheatSighting sighting) => new(AntiCheatLevel.AlwaysOn, sighting.Name, "", sighting.When);
    public bool AlwaysOn => Level == AntiCheatLevel.AlwaysOn;
    string Short => Name switch { "Easy Anti-Cheat" => "EAC", _ => Name };
    public bool Optional => Level == AntiCheatLevel.Optional;
    public string Badge => Level switch {
        AntiCheatLevel.AlwaysOn when SeenWhen != null => "Anti-cheat runs",
        AntiCheatLevel.AlwaysOn => "Anti-cheat",
        AntiCheatLevel.Optional => $"Anti-cheat ({Short})",
        _ => ""
    };
    public string Explanation => Level switch {
        AntiCheatLevel.AlwaysOn when SeenWhen != null => $"{Name} was running while this game ran ({SeenWhen}). It sees the changed driver file even in single-player and can block the game or flag the account, so Arc Cache doesn't set this game up. Start the game without it, if it can, and Arc Cache checks again.",
        AntiCheatLevel.AlwaysOn => $"{Name} runs whenever this game starts. It can refuse the game's driver copy or flag the account, so don't set this game up.",
        AntiCheatLevel.Optional => $"{Name} files were found. Only use Arc Cache if the game can start without it, like Halo MCC's \"anti-cheat disabled\" mode. If {Name} starts in every mode (Le Mans Ultimate, for example), don't set this game up: it sees the changed driver file and can block the game or flag the account, even in single-player.",
        AntiCheatLevel.NoneFound => "No known anti-cheat files were found. That isn't a guarantee; Arc Cache checks again before setup.",
        _ => "Not checked yet."
    };
}

public static class AntiCheatHints {
    // Riot Vanguard refuses to start VALORANT, League of Legends and 2XKO while DevOverrideEnable is on, whatever Arc Cache does per game.
    public static bool VanguardInstalled(){try{using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vgk");return key!=null;}catch(Exception){return false;}}

    // Marker (file or folder name; "*" prefix/suffix allowed), display name, and whether it runs whenever the game starts.
    static readonly (string Marker, string Name, bool AlwaysOn)[] Markers = [
        ("EasyAntiCheat", "Easy Anti-Cheat", false), ("EasyAntiCheat_EOS", "Easy Anti-Cheat", false), ("start_protected_game.exe", "Easy Anti-Cheat", false),
        ("EasyAntiCheat_Setup.exe", "Easy Anti-Cheat", false), ("EasyAntiCheat_EOS_Setup.exe", "Easy Anti-Cheat", false),
        ("BattlEye", "BattlEye", false), ("BEService.exe", "BattlEye", false), ("BEService_x64.exe", "BattlEye", false), ("BELauncher.exe", "BattlEye", false),
        ("BEClient.dll", "BattlEye", false), ("BEClient_x64.dll", "BattlEye", false), ("*_BE.exe", "BattlEye", false),
        ("PunkBuster", "PunkBuster", false), ("PnkBstrA.exe", "PunkBuster", false), ("pbsvc.exe", "PunkBuster", false),
        ("vgk.sys", "Riot Vanguard", true), ("vgc.exe", "Riot Vanguard", true), ("VALORANT.exe", "Riot Vanguard", true), ("VALORANT-Win64-Shipping.exe", "Riot Vanguard", true),
        ("League of Legends.exe", "Riot Vanguard", true), ("LeagueClient.exe", "Riot Vanguard", true), ("LoR.exe", "Riot Vanguard", true),
        ("Lion-Win64-Shipping.exe", "Riot Vanguard", true), ("RiotClientServices.exe", "Riot Vanguard", true),
        ("EAAntiCheat.Installer.exe", "EA Javelin anti-cheat", true), ("EAAntiCheat.GameServiceLauncher.exe", "EA Javelin anti-cheat", true), ("EAAntiCheat.GameServiceLauncher.dll", "EA Javelin anti-cheat", true),
        ("AntiCheatExpert", "Anti-Cheat Expert (ACE)", true), ("AceAntibotClient", "Anti-Cheat Expert (ACE)", true), ("ACE-Base64.dll", "Anti-Cheat Expert (ACE)", true),
        ("ACE-BASE.sys", "Anti-Cheat Expert (ACE)", true), ("SGuard64.exe", "Anti-Cheat Expert (ACE)", true), ("TP3Helper.exe", "Anti-Cheat Expert (ACE)", true),
        ("TenProtect", "Anti-Cheat Expert (ACE)", true), ("TesSafe.sys", "Anti-Cheat Expert (ACE)", true),
        ("HoYoKProtect.sys", "HoYoverse anti-cheat", true), ("mhyprot.sys", "HoYoverse anti-cheat", true), ("mhyprot2.sys", "HoYoverse anti-cheat", true),
        ("mhyprot3.sys", "HoYoverse anti-cheat", true), ("mhypbase.dll", "HoYoverse anti-cheat", true), ("GenshinImpact.exe", "HoYoverse anti-cheat", true),
        ("YuanShen.exe", "HoYoverse anti-cheat", true), ("StarRail.exe", "HoYoverse anti-cheat", true), ("ZenlessZoneZero.exe", "HoYoverse anti-cheat", true), ("BH3.exe", "HoYoverse anti-cheat", true),
        ("NeacClient.exe", "NetEase anti-cheat", true), ("NeacSafe64.sys", "NetEase anti-cheat", true), ("NEP2.dll", "NetEase anti-cheat", true),
        ("GameGuard", "nProtect GameGuard", true), ("gameguard.des", "nProtect GameGuard", true), ("nProtect", "nProtect GameGuard", true),
        ("XIGNCODE", "XIGNCODE3", true), ("*.xem", "XIGNCODE3", true), ("NCGuard", "NCGuard", true), ("NCGuardSDK", "NCGuard", true), ("HShield", "HackShield", true),
        ("BlackCall.aes", "BlackCipher", true), ("BlackCall64.aes", "BlackCipher", true), ("BlackCat64.sys", "BlackCipher", true),
        ("DenuvoAC", "Denuvo Anti-Cheat", true), ("denuvo-anti-cheat.sys", "Denuvo Anti-Cheat", true), ("denuvo-anti-cheat-runtime.dll", "Denuvo Anti-Cheat", true),
        ("equ8_conf.json", "EQU8", true), ("randgrid.sys", "Kernel anti-cheat", true),
        (".build.info", "Battle.net (online-only checks)", true), (".product.db", "Battle.net (online-only checks)", true)
    ];

    static bool Matches(string name, string marker) =>
        marker.StartsWith('*') ? name.EndsWith(marker[1..], StringComparison.OrdinalIgnoreCase) : name.Equals(marker, StringComparison.OrdinalIgnoreCase);

    // Bounded walk: top folders of a game hold the launcher and anti-cheat files. An always-on marker wins over an optional one.
    public static AntiCheatHint Scan(string? root, int maxDepth = 4, int maxEntries = 20000) {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return AntiCheatHint.NotChecked;
        AntiCheatHint? optional = null; int seen = 0;
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        while (queue.Count > 0 && seen < maxEntries) {
            var (folder, depth) = queue.Dequeue();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(folder, "*", options); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (string entry in entries) {
                if (++seen > maxEntries) break;
                string name = Path.GetFileName(entry);
                foreach (var m in Markers) {
                    if (!Matches(name, m.Marker)) continue;
                    if (m.AlwaysOn) return new(AntiCheatLevel.AlwaysOn, m.Name, entry);
                    optional ??= new(AntiCheatLevel.Optional, m.Name, entry);
                }
                if (depth + 1 < maxDepth && Directory.Exists(entry)) queue.Enqueue((entry, depth + 1));
            }
        }
        return optional ?? new(AntiCheatLevel.NoneFound);
    }
}
