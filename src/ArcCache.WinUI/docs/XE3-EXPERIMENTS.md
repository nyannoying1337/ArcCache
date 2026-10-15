# Xe3 device experiments

Use the unified preview. This workflow tests a private driver with Arc Cache's own D3D12 probe. It does not patch a game or replace the system driver.

1. Open Graphics → Try a compatibility check. Choose **Check the installed test file**, or **Choose a driver file** for read-only assessment. The report separates structural matches from reviewed images.
2. For the reviewed Xe3 32.0.101.9034 image, choose **512 MiB original control**, then **Create a test copy**. Read and confirm the experiment warning. Run normally, not as administrator.
3. With private routing enabled and Windows restarted, choose **Run the graphics check**. A matching Xe3 adapter must load the private Xe3 module for a pass. The probe creates one trivial compute pipeline without dispatching work.
4. If the original control passes, prepare and test 1, 2 or 4 GiB independently. Retain the experiment receipt and `device-test.txt`. Review personal paths before sharing results.
5. Stop using a bundle after a failure or driver update. Experiments live only under `%LOCALAPPDATA%/IntelArcCacheCap/driver-tests`; they are not attached to games and do not affect later game launches. The folder can be removed when the probe is closed.

Each bundle contains the original Xe3 backup/ACL, original dependency hashes, exact patched hash, private cohort, owned probe and a recovery/result receipt. Copies come from a currently active Intel package; every copied AMD64 DLL needs valid offline embedded trust. Catalog-only/unknown dependencies are refused. The package identity and all private hashes are rechecked before execution.

Limits apply only to the reviewed constructor image, SHA-256 `029E091F5B67A7418B39900E2AC4EAC9EF64767595E0138FF6569CC42FEB4911`. Unknown versions are reported as structural candidates without automatic patching. Xe2's 8/16 GiB reload code is not used for Xe3.

A pass proves observed private loading plus device/trivial-pipeline creation. It does not prove effective capacity, eviction, large-cache reload or game compatibility. A small shared Intel cache entry may be created. Windows enforcement stays enabled; no anti-cheat or game process is touched. The process times out after 60 seconds.
