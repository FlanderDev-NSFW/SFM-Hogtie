# Hogtie

> Developer notes (written in [sfm-modkit](https://github.com/FlanderDev-NSFW), where the mod is developed with
> an in-game test bridge; paths like `./tools/dev.ps1` and `docs/knowledge/` refer to that kit): how it works, settings in detail,
> diagnostics and the test history. The player readme (shipped in the release zip) is [`README.md`](../README.md).

A floor-standing hogtie device for SecretFlasherManaka (BepInEx 6 IL2CPP): she hangs face down, back arched, hips hanging low
but off the floor. The collar holds her neck, the upper arms go back and up through the upper-arm cuffs, the forearms lie along
the bar in the wrist cuffs, the ankle cuffs hold her ankles - knees down behind the post, feet up to her hands - and the post
stands between her thighs. Single-player/offline only.

**Needs RestraintHub** (2.0.0+): a placeable (`HogtieDevice`: placed from the panel, kept per map and in the save by the hub,
"F Hogtie" at it) whose in-use wearable is `Hogtie` (worn list, F routing, input blocking, conflicts, missions). Holds neck, arms,
legs and the spot. Being hogtied is not saved: a load or new game starts free.

**2.0.0 (RestraintHub 2):** placed in front of her (`FindSpot`: the 1.x floor and room checks with the hips point 1.1 m ahead);
its pose is her root's place (the hips' floor point, facing the way her head goes), so the device frame follows from her layout.
`ShowPlaced` builds an empty rig for her body under the hub's root; F locks her at that frame (no new checks) and hides the empty
rig while the fitted one is shown. Missions (`RtHub_Apply`) still hogtie her where she stands. The ring entry is gone.

**Status 2.0.0:** usable, L3 (see Verification). Model: "Hogite Device" by Kryiat, CC BY-NC-ND 4.0 (`LICENSE-MODELS`).

## How it works
- **The model** is the `hogtie-device` object of `Hogtie.blend` (one mesh of 24 loose parts, size shape keys per cuff), exported by
  `tools/export_model.py` into `assets/hogtie.htm` (embedded in the DLL):
  `blender -b --disable-autoexec Hogtie.blend --python src/Hogtie/tools/export_model.py -- <absolute path>/src/Hogtie/assets/hogtie.htm`.
  The parts are grouped (base, bar, collar, upper / wrist / ankle cuffs per side, their mounts, ankle bar); every cuff's size key
  doubles its ring about the point where it is held, so the runtime sets any size (`position = basis + key * delta`). The
  material is plain glossy black (the model's `a.png` is a normal map), drawn with a copy of the game's handcuff toon material
  (`Model.MetalShade`).
- **Both sides are drawn.** The model's winding is not consistent (the right-hand cuffs are mirrored copies wound inside out,
  signed volume < 0; Blender draws both sides). Drawn one-sided, the plate vanished seen from above and the rings showed their far
  half; with the winding flipped, the post and bar vanished instead. Each mesh now carries a second, reversed copy with turned
  normals (`HogtieRig.TwoSided`).
- **Her body** is measured from the bones at lock time (`HogtiePoser.Measure`): lengths, shoulder offset in her own axes, and the
  chest's front from the nipple points (`ChikubiPiasController`; first save: 20 cm in front of the spine, 47 % up it - without it
  the breasts went through the plate). Limb radii are estimated from bone lengths (docs/knowledge).
- **The fit** (`Logic/HogtiePose.cs`, tested): a grid search over the device scale (0.3-1.6), the ankle bar's height on the post,
  the shins' lean (the ankle cuffs turn on the ankle bar to it) and the two ways the thighs can meet the spine. The ankle cuffs
  sit at the ankles (`Pose.AnkleOnShin`); from the collar and the ankle cuffs the neck, knees and feet follow. The spine is
  arched: lower back (thigh joints -> chest bone) and chest (chest bone -> neck) meet at `Pose.ArchDegrees`, front down; the
  thigh joints sit where the thigh from the knee meets that arched spine from the neck. The fit that breaks the fewest rules
  wins: belly, chest and knees above the floor, thigh joints in front of the post, back under the bar; it prefers the hips hanging
  low (belly near the 3 cm floor clearance), the ankle cuffs near the model's 45 degrees and a scale near her spine / 0.41 m.
  The arms do not shape the fit (they follow, see below).
  First save (spine 0.41, long shins): scale 0.91, ankle bar +12.5 cm, shins lean 20 degrees (ankle cuffs turned -25), belly 4.6 cm
  up. "Amateur" (spine 0.21): the shins keep the model's 45 degrees.
  Before (0.1.0 first version) the ankle cuffs slid up to 0.30 shins up the leg and the spine was straight.
- **Placement** (`HogtiePlacer`): her head the way she faces, her hips over her feet; refused unless the floor under the plate and
  the collar is within 4 cm of her feet, and a box over the device is free of colliders (her own, NPCs and triggers aside). The
  device stands on the highest floor point under it.
- **The camera** circles a point `View.CameraHeight` above her back: the player's `CameraTarget` (under her body object, 1.21 m
  up standing; the game does not move it when crouching) is held there every Update and LateUpdate and put back on release.
- **The pose** is held in `LateUpdate`: hips placed and turned, lower back aimed at the chest bone, chest at the neck, head along
  the collar's axis, legs; then
  her drawn shoulder joints are read and each arm is fitted to them (`HogtiePose.FitArm`: the arm cuffs slide along the bar and
  turn to the limb, `HogtieRig.Move`), then the arms are aimed. Bones get their drawn lengths for the aim (the game changes local
  positions after LateUpdate - WallRestraint's finding; here the upper chest and collarbones matter too: without them her
  shoulders, elbows and hands were 5 cm off on the "Amateur" body).

- **The cuffs are fitted to her skin** (`HogtieFitter`, `Logic/CuffFit.cs`, tested): 40, 120 and 300 frames after the lock her
  body mesh is baked as drawn (onPreCull), cut at every cuff (the skin within the cuff's width whose nearest body part - by
  surface, not bone - is that limb) and an ellipse fitted around it; the cuff's size key is set to the ellipse's long half axis
  + `Model.ClearanceCm`, its mesh squashed to the short one, and it is moved across the limb onto the ellipse's centre. A cuff
  only grows across the fits. First save: collar 3.5 x 3.5, upper arms 3.7 x 3.4, wrists 2.7 x 2.0, ankles 3.6 x 2.8 cm (the
  bone-length estimates were 2.9 / 2.0 / 1.8 / 2.4 cm). All cuffs are turned fully onto their limb (the arm cuffs also sideways,
  the arms come together towards the bar). The ankle cuffs sit at the ankle (`Pose.AnkleOnShin` 0.08); 0.12 gave the same size
  (the cuff's upper edge then reaches the calf).
- **Occlusion:** the device's renderers do not take part in the level's occlusion culling (it hid the whole device from some
  camera spots behind her).

## Config (`BepInEx/config/FlanderDev.SFM.Hogtie.cfg`)
| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Hogtie | name in the panel, at the device (F) and in the worn list (the key name is kept from 1.x) |
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance an attempt frees her (failures add 0.02, like the game's handcuffs) |
| Model.ClearanceCm | 0.4 | room between limbs and collar / cuffs (cm; -1 .. 5, applied at the next lock) |
| Model.MetalShade | 0.15 | brightness of the device |
| Pose.AnkleOnShin | 0.08 | ankle cuff place, shin lengths above the ankle joint |
| Pose.ArchDegrees | 35 | how far her back arches (lower back against chest; 0 = straight) |
| View.CameraHeight | 0.2 | the camera circles this far above her back while she is hogtied (m) |
| Debug.Disable | 0 | bit 1 = do not pose the body (negative control); bit 2 = aim with the LateUpdate bone lengths; bit 8 = mark the skin points the cuff fit used (right upper arm and wrist) and hide the device |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call Hogtie.HogtieController LockHere | LockForced | Release | Bones | View <side> <along> <up> | ViewOff` (View: the game
camera at that point in device metres - x = her right, y = towards her feet, z = up - with a lamp);
`game get Hogtie.HogtieController::Status` (the fit and, while held, the restraint axes against the drawn bones - collar vs neck,
ankle cuffs vs shins, arm cuffs vs arms - each joint's distance from its target, and the cuff fit). Close looks at clipping:
the bridge's `game closeup bone:RightLowerArm~RightHand@0.85 --dist 0.2 --az 90 --el 10` (see SfmBridge's README).

## Verification
- L1: `./tools/dev.ps1 test Hogtie` - model groups, size keys, frame; the fit for both measured bodies (rules met within 3 cm,
  lengths kept, each limb through its cuff, arms following a moved shoulder).
- L3 0.1.0 (2026-10-27, Residence street at (-33.5, -114.5), night, `Model.MetalShade` 0.8 for the pictures):
  - First save: refused in the apartment beside a door ("something is in the way (door04_door3)") and facing the house
    ("AE_House_10_LOD0"); accepted on the street: scale 0.87, hips 0.197 m up, ankle bar +3.6 cm; joint misses <= 0.6 cm (hips,
    neck, head, knees, feet, shoulders, elbows, hands). Screenshots from the side, front, rear and three-quarter: collar on the neck,
    upper arms and wrists in their cuffs on the bar, shins through the ankle cuffs, post between her thighs, chest resting on the
    plate. Negative control `Debug.Disable = 1`: she stands beside the empty device.
  - Hold F (hub selected "Hogtie", not in an interaction area): "attempt: success after 0 failures", "release: free", device hidden,
    hub list empty.
  - "Amateur" save: scale 0.51, hips 0.15 m up, misses <= 0.8 cm after the shoulder fix (5 cm before). Her wide dress and big
    boots hide most of the device.
- L3 0.1.0 pose rework (2026-10-27, first save, same street): game camera now low behind her (screenshot). Ankle cuffs at the
  ankles, turned to the shins; back arched 35 degrees, belly 4.6 cm above the plate (side, left, rear, three-quarter screenshots).
  Axes against the drawn bones: collar 0.2 deg, ankle cuffs 0.5 / 0.8 deg. Ankle cuffs at 6 % of the shin looked loose (enlarged
  crop: a step all round); 5.5 % closes them with no leg through the band. Collar (7 % of the spine + 0.4 cm) sits snugly.
  On release the camera target is back at 1.21 m.
- L3 0.1.0 cuffs (2026-10-27, first save, Residence street at (-35.5, -114.5), `game closeup` views from several sides):
  - Ankle cuffs (round, from bone lengths) clipped on the calf side; moving the shin off the cuff's centre the wrong way made it
    worse (the bones' smallest turn leaves the shin's front facing her thighs). Wrist and upper-arm cuffs clipped (the arms
    converge 11 degrees sideways, the cuffs only turned up / down; and the wrist is wider than estimated).
  - With the cuffs turned fully onto the limbs (axes 0.0 deg) and fitted to the bake: no clipping from any side for the wrist,
    upper-arm and ankle cuffs and the collar; the round fit gapped on the flat sides of wrist and ankle, the ellipse closes
    them; some room remains on one side of the upper-arm and ankle cuffs (the limb tapers along them).
  - Markers at the fitted points (Debug.Disable 8) lay on her drawn skin (the upper arm's 3.5 cm is real); a few at the wrist's
    edge sat just past the drawn outline (at most ~0.5 cm too generous there).
  - From behind the whole device vanished (occlusion culling); fixed, visible from the same spot.
  - F still frees her; the camera target goes back to 1.21 m.
- Not yet tried in game: other places, daylight.
- L3 0.2.0 (2026-11-02, with RestraintHub 1.1.0 and the test missions - details in RestraintHub's `docs/RestraintHub.md`, L3 1.1.0): `CanPutOn` (placement with `check`: "something is in the way" indoors / at the intercom, ok on the street), put on by a mission (no struggling, 15 s): list "Hogtie [locked 0:11]", taken off by the timer.
- L3 2.0.0 (2026-11-07, RestraintHub 2.0.0 - details in its `docs/RestraintHub.md`, "L3 2.0.0 with the ported mods"): placing
  refused in the apartment ("something is in the way (bed01)") and once on the street (flowers), placed on the street (empty device
  with the game's circle, screenshot); F -> hogtied in it (fit logged as before, empty copy hidden, screenshot); F held -> free,
  device back; kept through save + restart + load; picked up from the panel. Missions not re-tested (unchanged path).

## Open points
- The arms: not tuned yet (they reach back through their cuffs from wherever her shoulders end up).
- The post passes through the crotch / lower buttocks a little (`hips against the post` is met at 4.8 cm in front of the thigh
  joints; her buttocks reach further back).
- Clothes are not measured (skirts, boots).
