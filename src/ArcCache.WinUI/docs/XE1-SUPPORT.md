# Xe1 / Intel Arc A-series test patch

Intel describes the Arc A-series / Alchemist architecture as [Xe-HPG](https://www.intel.com/content/www/us/en/developer/articles/technical/introduction-to-the-xe-hpg-architecture.html). Arc Cache labels this generation **Xe1**. The patch in this build targets `igd12um64xeh.dll`; this file association is based on local driver analysis, not a universal mapping for every Intel GPU.

## Available now

The app can inspect the exact Xe1 images below, make an unchanged control and a 1 GiB test copy, run the graphics check with the requested module name, and offer the 1 GiB game-trial guide after both checks pass. Arc A-series adapter names preselect the Xe1 file; that suggestion does not grant patch approval. File hashes and runtime checks still decide eligibility. No positive Arc A-series hardware or game result is available.

| Version | Original SHA-256 | 1 GiB output SHA-256 | Constructor file offset / RVA |
| --- | --- | --- | --- |
| 32.0.101.9034 | `FDA1637450E828E5D00F445B127861A599C08CC39FB55172A0107ABBF1A7758E` | `135D6BFE2EB13BAB24DE231E97AF1CB5563AADA769E84107AAC5162DA8662D97` | `0x130F940` / `0x1310540` |
| 32.0.101.9150 | `D42F3E08F5A2A0CA73D16C6219496CD333427B7CD79B443F4407C2182A8934D1` | `8B4B1216F636AA120CC09736DDC084811A4561D0FFF7042B5C05FB8AE021C9D0` | `0x1368C20` / `0x1369820` |

## What was checked

The 113-byte out-of-line constructor was decoded independently for each image, including its PE exception-table function boundary and executable-section mapping. It stores the default 512 MiB value at object offset `0x10` and a separate 4 GiB value at offset `0x08`. Xe1 places later instructions differently from the existing Xe2/Xe3 constructor.

The patch replaces only the four-byte immediate for that default limit with 1 GiB. The entire original image SHA-256, full constructor context, mapping and resulting output SHA-256 must match the reviewed definition. All other bytes remain identical. Unchanged controls are byte-for-byte originals. Unknown Xe1 images and 2/4/8/16 GiB requests remain blocked.

An isolated native CPU harness executed the patched constructor from each image, stubbing only its string-member helper. It verified the 1 GiB field, unchanged 4 GiB field, return pointer and initial flags. This is a constructor test, not a GPU or cache-capacity test.

## Remaining verification

No Arc A-series GPU is available on the development machine. Positive Xe1 driver loading, game behaviour and effective cache capacity remain unverified. The binary contains other inlined 512 MiB initialisations; these are deliberately left unchanged because their paths and effects have not been qualified. This constructor patch does not establish that every cache path now uses 1 GiB. Community testing should report the selected chip, driver version, separate original/patched results, game loading and cache observations independently.

The native helper accepts Xe1 explicitly and verifies the loaded file came from its own test directory. A B580 cannot count as a positive Xe1 hardware test: if its unchanged Xe1 copy does not load, the app stops without making a patched copy. Matching source/dependency checks, explicit risk consent, per-game original-copy loading, owned-file rollback, and Windows integrity enforcement remain in place. Older `igd12um64xel.dll` / Xe-L is not included by this definition.

Xe1, Xe2 and Xe3 test results are kept separate. Passing a test on one generation does not qualify another generation or an unknown driver image.
