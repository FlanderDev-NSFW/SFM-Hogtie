# Hogtie 0.1.0

A hogtie device for **Secret Flasher Manaka**: she hangs face down in a floor-standing frame, back arched. A collar holds her
head up, her arms are cuffed behind her along the bar, and her ankles are pulled up to her hands at the post. The device is fitted to her
body: scaled, cuffs sized to her limbs, the arm cuffs and the ankle bar moved to where she reaches.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 1.0.0** or newer

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/Hogtie/`.

## How to use
Stand on open, level ground, facing where her head should go. Hold a ring key (e.g. `1`), page with `Z` / `X` to the restraint
page and click **Hogtie**. If there is no room (a wall, furniture, steps), a message says why. Hold `F` to struggle free.

## Settings
`BepInEx/config/FlanderDev.SFM.Hogtie.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Model.MetalShade | 0.15 | brightness of the device (0 = black, 1 = white) |
| Model.ClearanceCm | 0.4 | room inside the collar and cuffs (cm) |
| Pose.ArchDegrees | 35 | how far her back arches |
| View.CameraHeight | 0.2 | how far above her back the camera looks (m) |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- Being hogtied is not saved: after loading she is free.
- Clothes are not measured: wide skirts and boots can hide the cuffs or show through the device.
- The post stands between her thighs.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Model: see `LICENSE-MODELS.txt`.

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/Hogtie/Hogtie.csproj -c Release`.
- It compiles against RestraintHub: the [SFM-RestraintHub](https://github.com/FlanderDev-NSFW/SFM-RestraintHub) repository next to this one, `lib/RestraintHub/RestraintHub.dll`, or the hub installed in the game.
- The source of the mod is `src/Hogtie` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared
  [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit) library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
