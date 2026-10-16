namespace ArcCache.Core;

public sealed record DriverLimitCapability(int MiB, bool Available, string Evidence, bool Buildable = false, bool Tested = false);

public static class DriverLimitChoices
{
    public static bool IsAvailable(
        int mib,
        IReadOnlyDictionary<int, DriverLimitCapability> capabilities,
        bool gameUsesLimitedTestCopy)
    {
        return capabilities.TryGetValue(mib, out var capability)
            && capability.Available;
    }

    public static int Fallback(
        IReadOnlyDictionary<int, DriverLimitCapability> capabilities,
        bool gameUsesLimitedTestCopy)
    {
        foreach (int mib in new[] { 512, 1024, 2048, 4096, 8192, 16384 })
            if (IsAvailable(mib, capabilities, gameUsesLimitedTestCopy)) return mib;
        return 512;
    }
}
