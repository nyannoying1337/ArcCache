# Intel graphics coverage and larger limits

## Scope

Arc Cache aims to cover Intel gaming graphics across desktop cards, laptop GPUs, workstation Arc Pro cards and integrated graphics. B-series is not an eligibility rule. GPU branding suggests a starting point; the installed driver image, exact patch definition and loading checks determine what can actually be offered.

Intel distinguishes Xe-LP (including Iris Xe), Xe-HPG (Arc A-series), Xe-LPG integrated graphics, Xe2 variants and Xe3. Arc Pro A/B products also exist. These product families do not establish that a particular Windows DLL or cache layout is interchangeable. Sources: [Intel architecture guide](https://www.intel.com/content/www/us/en/docs/oneapi/optimization-guide-gpu/2025-2/intel-xe-gpu-architecture.html), [Arc Pro A-series](https://www.intel.com/content/www/us/en/products/details/discrete-gpus/arc/workstations/a-series.html), [Intel Xe3 processor graphics](https://edc.intel.com/content/www/us/en/design/platforms/core-processor-series-3-datasheet-volume-1-of-2/002/processor-graphics/).

## Implemented versus planned

| Driver path | Implemented | Remaining |
| --- | --- | --- |
| Xe1 / reviewed xeh images, 9034 and 9150 | Separate original and 1 GiB checks, explicit game-trial workflow | Positive A-series hardware/game test; 2/4/8/16 GiB definitions |
| Xe2 / reviewed 9034 image | Standard 1/2/4 GiB; experimental 8/16 GiB | Measured capacity and real reload above 4 GiB |
| Newer Xe2 / candidate trial path | Original and 1 GiB checks and game trial | Larger independently qualified patches |
| Xe3 | Separate checks; reviewed 9034 constructor tests for 1/2/4 GiB in experimental tools; guided game trial at 1 GiB | Matching-hardware loading/game tests and 8/16 GiB reload patch |
| Other integrated / Pro packages | Active Intel adapter discovery; checks where a supported driver file is present | Exact image qualification; older Xe-L and legacy HD/UHD patch support |

This table describes code paths, not universal support for every GPU in a family. No matching hardware is available here for positive Xe1/Xe3 tests. Older `igd12um64xel.dll` is not the same as the Xe1 xeh definition.

## 8 and 16 GiB across families

The target is the same visible Default / 1 / 2 / 4 / 8 / 16 GiB choices on each eligible family, not a permanent Xe2-only restriction. The current larger patch is pinned to one exact Xe2 image. It widens both the per-file limit and a separate append budget, and changes a cache reload call to a chunked reader with correct Windows exception-unwind metadata. Copying its addresses to another image would modify unrelated code.

For each new image, independently trace the constructor, any inlined initialisations, append-budget consumers, allocation size, file-read length and parser bounds. Rebuild the reload wrapper against the image's own helper, check PE layout and unwind mapping, pin original/output hashes, and test zero/short reads, failures and boundaries around 4 GiB with a synthetic reader. Then require an unchanged-control and patched graphics check on matching hardware. Game loading and actual cache reload above 4 GiB are separate results to report.

## User confirmation above 4 GiB

Proposed wording: **8 and 16 GiB are highly experimental. Larger cache files use more disk space and can need several extra gigabytes of RAM while loading. Games may load slowly, hang or close. Close other apps, start with 8 GiB, and use Default if problems occur.** Require confirmation when applying either value. A 16 GiB limit is a ceiling, not an immediate 16 GiB reservation, and is not a guaranteed performance improvement.

Show unavailable sizes with their specific reason rather than hiding them. A warning acknowledges risk; it cannot replace a missing implementation. Never enable a size by changing only a UI guard.
