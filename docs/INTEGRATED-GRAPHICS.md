# Built-in and add-in Intel graphics

Arc Cache lists active Intel graphics chips, including graphics built into a CPU. Open **Graphics** and select the chip used by the game. Selecting a chip changes which installed package Arc Cache checks; it does not change Windows/game GPU preferences, enable a disabled chip, reinstall a driver or modify a game.

## Support levels

| Driver files actually used | Available workflow |
| --- | --- |
| Exact reviewed Xe2 32.0.101.9034 image and complete package | Detect the running DirectX 12 game, set up its unchanged copy, observe loading, then try a cache-size preset |
| Exact reviewed Xe3 32.0.101.9034 image | Separate graphics check in the preview version; per-game Xe3 setup remains unqualified |
| Older HD/UHD/Iris, XeH, different OEM package or unknown version | Detection/inspection where an active 64-bit driver is registered; patching remains blocked until a definition is reviewed |

The B580 model-name restriction is removed. Every dependency hash and the driver version still has to match. A different name, a selected built-in chip, or a similar instruction pattern cannot override those checks. A running game must expose one unambiguous supported Intel renderer from the selected package.

Architecture names alone do not grant support. For example, [Intel documents Xe2 processor graphics in Core Ultra 200V](https://cdrdv2-public.intel.com/829568/829568-004.pdf), while [Core Ultra Series 3 includes Xe3 integrated graphics](https://game.intel.com/us/stories/intel-drops-the-gaming-good-stuff-at-ces-2026/). Arc Cache still checks the installed bytes rather than guessing from a product name.

## Verification status

Automated policy tests cover selecting built-in versus add-in fixtures, missing-device fallback, model-independent acceptance of exact reviewed bytes, and refusal of changed dependencies/unknown versions. Actual integrated-GPU gameplay and positive Xe3 hardware tests remain unverified. The available physical regression configuration is B580; detection uses active SetupAPI devices rather than stale registry entries.
