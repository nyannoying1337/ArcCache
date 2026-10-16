# Intel graphics coverage and larger limits

## Scope

Arc Cache discovers active Intel graphics devices, including integrated and discrete adapters. B-series branding is not an eligibility rule. GPU branding suggests a starting point; the installed driver image, exact patch definition and loading checks determine what can actually be offered.

Intel distinguishes Xe-LP (including Iris Xe), Xe-HPG (Arc A-series), Xe-LPG integrated graphics, Xe2 variants and Xe3. Arc Pro A/B products also exist. These product families do not establish that a particular Windows DLL or cache layout is interchangeable. Sources: [Intel architecture guide](https://www.intel.com/content/www/us/en/docs/oneapi/optimization-guide-gpu/2025-2/intel-xe-gpu-architecture.html), [Arc Pro A-series](https://www.intel.com/content/www/us/en/products/details/discrete-gpus/arc/workstations/a-series.html), [Intel Xe3 processor graphics](https://edc.intel.com/content/www/us/en/design/platforms/core-processor-series-3-datasheet-volume-1-of-2/002/processor-graphics/).

## Current support and verification

Arc Cache no longer pins sizes to one reviewed driver image. For the installed `igd12um64xe*.dll` it locates the per-game cache constructor (and, for 4 GB and larger, the cache reload routine) by byte patterns that must match **exactly once**, checks that the original limit is 512 MiB, and builds the patched copy. A size is offered only after it passes the isolated test on the player's PC: an unchanged 512 MiB control and then the size itself, each creating real pipelines, writing a shader cache and reloading it with that driver copy.

| Family | Driver file | Sizes the patch supports | Hardware results |
| --- | --- | --- | --- |
| Xe2 (Arc B580/B570; Lunar Lake 140V/130V) | `igd12um64xe2.dll` | 1, 2 GB (constructor), 4, 8, 16 GB (constructor + chunked reload) | **Arc B580, 32.0.101.9150:** control and all sizes pass; Palworld and Ready or Not run at 16 GB |
| Xe3 (Panther Lake Arc B370/B390) | `igd12um64xe3.dll` | 1–16 GB (same layout as Xe2; patterns match uniquely on 9150) | None yet; the on-PC test decides |
| Xe1 (Arc A-series) | `igd12um64xeh.dll` | **1 GB only** | None yet. The Xe1 constructor differs: the wide store used for 2 GB and up would corrupt it, so those sizes are locked |
| Xe-LPG, Xe-LP, older | – | none | Not supported |

The driver file is taken from the adapter's active driver package and confirmed by the test run (which reports the file that actually loaded), not from the GPU's marketing name. After an Intel driver update, **Check support** inspects the new files; existing game setups keep using their own copy until you change them.

## Larger cache limits

- 1 and 2 GB change only the constructor's limit (2 GB uses a wider store because the original instruction sign-extends).
- 4, 8 and 16 GB additionally route the cache reload through a small wrapper that reads in 256 MiB chunks, because the original reload path can't read files of that size in one call. The wrapper has correct unwind data and is exercised by the reload pass of the size test.
- A configured limit doesn't reserve memory or disk. The cache file grows as shaders are compiled. Loading a very large cache can need roughly its size in RAM.
- Measured effect (Palworld, 16 GB): a SCSKiller compile with the driver copy kept 760.8 MB of shaders (the stock driver stops at 536.9 MB), and the next game start read them with only 2.3 MB of new compiles.
