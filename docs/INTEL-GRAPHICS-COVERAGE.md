# Intel graphics coverage and larger limits

## Scope

Arc Cache discovers active Intel graphics devices, including integrated and discrete adapters. B-series branding is not an eligibility rule. GPU branding suggests a starting point; the installed driver image, exact patch definition and loading checks determine what can actually be offered.

Intel distinguishes Xe-LP (including Iris Xe), Xe-HPG (Arc A-series), Xe-LPG integrated graphics, Xe2 variants and Xe3. Arc Pro A/B products also exist. These product families do not establish that a particular Windows DLL or cache layout is interchangeable. Sources: [Intel architecture guide](https://www.intel.com/content/www/us/en/docs/oneapi/optimization-guide-gpu/2025-2/intel-xe-gpu-architecture.html), [Arc Pro A-series](https://www.intel.com/content/www/us/en/products/details/discrete-gpus/arc/workstations/a-series.html), [Intel Xe3 processor graphics](https://edc.intel.com/content/www/us/en/design/platforms/core-processor-series-3-datasheet-volume-1-of-2/002/processor-graphics/).

## Current support and verification

| Driver path | Implemented | Not verified or unavailable |
| --- | --- | --- |
| Xe1 / reviewed xeh images, 9034 and 9150 | Separate original and 1 GiB checks, explicit game-trial workflow | Positive A-series hardware/game test; 2/4/8/16 GiB definitions |
| Xe2 / reviewed 9034 image | 1/2/4 GiB; experimental 8/16 GiB options | Measured capacity and real reload above 4 GiB |
| Newer Xe2 / candidate trial path | Original and 1 GiB checks and game trial | Larger independently qualified patches |
| Xe3 | Separate checks; reviewed 9034 constructor tests for 1/2/4 GiB in experimental tools; guided game trial at 1 GiB | Matching-hardware loading/game tests and 8/16 GiB reload patch |
| Other integrated / Pro packages | Active Intel adapter discovery; checks where a supported driver file is present | Exact image qualification; older Xe-L and legacy HD/UHD patch support |

This table describes code paths, not universal support for every GPU in a family. No matching hardware is available here for positive Xe1/Xe3 tests. Older `igd12um64xel.dll` is not the same as the Xe1 xeh definition.

## Larger cache limits

8/16 GiB are implemented only for the exact reviewed Xe2 32.0.101.9034 image in the unified preview. The patch changes the constructor limit, append budget and cache reload routine; its offsets cannot be reused for other images. Whole-file memory allocation remains. Large cache files can require approximately their size in additional RAM during loading. A configured ceiling does not immediately reserve that much memory.

Actual cache capacity and reload with real cache files larger than 4 GiB remain unverified. No Xe1, Xe3, older Xe-L or legacy HD/UHD 8/16 GiB patch is enabled. Unknown or changed images cannot inherit a reviewed definition from branding or a similar constant.
