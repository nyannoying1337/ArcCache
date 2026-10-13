# Native validation probes

These probes validate constructor state, loader behavior and compatibility guards. They require Windows and x64 MSVC, plus local qualified original DLLs where specified. No proprietary test DLLs are distributed.

`CapConstructorProbe.cpp` executes only the reviewed constructor with a stubbed external helper. It verifies field values and preservation of unrelated state; it does not establish shader-cache capacity.

The proxy/debug-helper probes exercise isolated DLL-loading behavior and known private-routing conflicts. The SCSKiller recorder remains an external integration with unresolved private-driver compatibility. These probes are test evidence, not generic game-hook support.

[Build/test guide](../../../docs/DEVELOPMENT.md) · [Coverage](../../../docs/TESTING.md).
