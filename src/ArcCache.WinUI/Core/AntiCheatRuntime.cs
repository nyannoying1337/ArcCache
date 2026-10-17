using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ArcCache.Core;

/// <summary>An anti-cheat seen running while a game ran. WithDriverCopy: the game had its driver copy at that time.</summary>
public sealed record AntiCheatSighting(string Name, DateTime Utc, bool WithDriverCopy) {
    public string When => Utc.ToLocalTime().ToString("g");
}

// Files on disk only say an anti-cheat is installed; what matters is whether it runs while the game runs (Halo MCC can
// start without Easy Anti-Cheat, Le Mans Ultimate never does). These services are started on demand by the protected game
// and stop with it, so "running while this game runs" is the observation the setup decision is based on.
public static class AntiCheatRuntime {
    static readonly (string Process, string Name)[] Processes = [
        ("EasyAntiCheat_EOS", "Easy Anti-Cheat"), ("EasyAntiCheat", "Easy Anti-Cheat"),
        ("BEService", "BattlEye"), ("BEService_x64", "BattlEye"),
        ("EAAntiCheat.GameService", "EA Javelin anti-cheat"),
        ("SGuard64", "Anti-Cheat Expert (ACE)"), ("SGuardSvc64", "Anti-Cheat Expert (ACE)"),
        ("NeacClient", "NetEase anti-cheat")
    ];

    public static string? RunningName() {
        foreach (var entry in Processes) {
            var found = Process.GetProcessesByName(entry.Process);
            try { if (found.Length > 0) return entry.Name; } finally { foreach (var process in found) process.Dispose(); }
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    /// <summary>Whether any process runs from the game's folder. PROCESS_QUERY_LIMITED_INFORMATION is enough for the
    /// image path, and an anti-cheat that hides the process memory still allows it.</summary>
    public static bool AnyProcessUnder(string? root) {
        if (string.IsNullOrWhiteSpace(root)) return false;
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var name = new StringBuilder(4096);
        foreach (var process in Process.GetProcesses()) using (process) {
            IntPtr handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) continue;
            try {
                int size = name.Capacity; name.Length = 0;
                if (QueryFullProcessImageName(handle, 0, name, ref size) && name.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            } finally { CloseHandle(handle); }
        }
        return false;
    }
}

/// <summary>Per-game anti-cheat sightings, kept across restarts in anticheat-sightings.json.</summary>
public sealed class AntiCheatSightings(string path) {
    readonly object gate = new();
    Dictionary<string, AntiCheatSighting>? cache;

    Dictionary<string, AntiCheatSighting> Load() {
        if (cache != null) return cache;
        try { cache = File.Exists(path) && new FileInfo(path).Length < 1024 * 1024 ? JsonSerializer.Deserialize<Dictionary<string, AntiCheatSighting>>(File.ReadAllText(path)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { cache = null; }
        return cache ??= new(StringComparer.Ordinal);
    }
    void Save() {
        try { File.WriteAllText(path, JsonSerializer.Serialize(cache)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public AntiCheatSighting? Get(string key) { lock (gate) return Load().GetValueOrDefault(key); }
    public void Record(string key, AntiCheatSighting sighting) { lock (gate) { Load()[key] = sighting; Save(); } }
    public bool Clear(string key) { lock (gate) { bool removed = Load().Remove(key); if (removed) Save(); return removed; } }
}
