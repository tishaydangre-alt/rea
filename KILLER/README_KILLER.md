# KILLER — Version 1.0.0

**Created by Tishay Dangare**

KILLER is an original first-person stealth-horror game for Android and iOS.
You play **Tishay Dangare**, 19, who has inherited a very bad family tradition.
Eliminate the five people hidden in the house, find the front-door key, and
escape before **Vikram "The Shadow" Rao** — the investigator who has hunted
your family for generations — catches you.

> Contains stylised, cartoonish violence, jump scares and dark humour.
> All characters are fictional. All art, audio, and code are original or
> procedurally generated at runtime.

## Deliverables in this folder

| File | What it is |
|---|---|
| `KILLER_Master.cs` | The entire game in one C# file (bootstrap, house, player, Hunter AI, targets, UI, audio, saves, ads, editor build menus). |
| `README_KILLER.md` | This file: build, controls, monetisation, privacy, troubleshooting. |
| `REVERSE_ENGINEERING_NOTES.md` | Pattern-level design notes and the legal-separation record. |
| `VERIFICATION.md` | Exactly what was and was not verified in the authoring environment. |

**No `.apk` or `.ipa` is checked in.** Those binaries can only be produced by
Unity (Android) and Unity + Xcode on a Mac with your own signing identity
(iOS). Anyone offering a pre-built binary from this source should be treated
with suspicion. The two editor menu items below produce them.

## Quick start (play in the Editor)

1. Install **Unity 2022.3 LTS** (or Unity 6) with Android Build Support and/or iOS Build Support.
2. Create a new **3D (Built-in Render Pipeline)** empty project.
3. Copy `KILLER_Master.cs` into the project's `Assets/` folder.
4. Press **Play**. No scene, prefab, or asset setup is needed — the game builds
   itself from an empty scene via `[RuntimeInitializeOnLoadMethod]`.

An internet connection is required to pass the launch check (by design).
In the Editor the check runs against a no-data connectivity endpoint.

## Build the single-file outputs

### Android → one `KILLER.apk`

Use **KILLER → Build Android APK** in the Unity menu bar. It automatically:

- creates the empty `Assets/KILLER_Bootstrap.unity` scene,
- sets package name `com.tishaydangare.killer`, version `1.0.0`,
- sets minimum API 24, IL2CPP, ARM64 + ARMv7,
- disables App Bundle output so you get a **single `.apk`**,
- writes `Builds/Android/KILLER.apk`.

### iOS → one `KILLER.ipa`

1. Use **KILLER → Build iOS Xcode Project** (same automatic setup;
   bundle ID `com.tishaydangare.killer`, minimum iOS 14.0, IL2CPP/ARM64).
2. Open `Builds/iOS` in **Xcode on a Mac**, select your signing team,
   then **Product → Archive → Distribute App** to export `KILLER.ipa`.

iOS signing, provisioning, and App Store submission require your own Apple
Developer account — no source file can do that for you.

## Controls

| Action | Desktop/Editor | Touch |
|---|---|---|
| Move | WASD / arrows | Left-side virtual stick (drag) |
| Look | Mouse | Drag right side of screen |
| Run | Left Shift | RUN toggle button |
| Crouch | C / Left Ctrl | CROUCH toggle button |
| Lean / peek | Q / E (hold) | Lean by tilting look; use cover |
| Interact / hide / stairs | F | USE button |
| Attack / strike in QTE | Left mouse / Space | ATTACK button |
| Flashlight | Button (touch) / always available via LIGHT | LIGHT button |
| Pause | Esc / P | II button |

## How to play

1. **Eliminate all 5 targets.** Sneak behind a target and attack, then tap
   attack again inside the timing zone. A perfect hit is quiet; a near miss is
   loud; a whiff makes them scream and flee — and screaming attracts The Shadow.
2. **Find the front-door key.** It is hidden in a different drawer each run.
3. **Escape through the front door** before The Shadow reaches you.
4. **Use the house:** hide in closets, toggle lamps, switch on radios as
   distractions, close doors (loudly), and find the basement hammer — it
   forces the locked first-floor door and makes finishes quicker and quieter.
5. **Mind the noise.** Running, slamming doors, traps, and screams all travel.
   The Hunter patrols, investigates, searches, and chases
   (Patrol → Suspicious → Search → Chase), remembers your last known position,
   sets snap traps — and each new Day he gets faster and patrols the rooms you
   visit most.

Death and victory screens, returning to the menu, and the optional
once-per-session **revive** are the ad moments (see below).

## Monetisation (ads only)

- Interstitials: on death-retry, death-to-menu, victory/next-day,
  victory-to-menu, and pause-to-menu — capped at **about 4 per session** with a
  60-second cooldown, and the game always continues if an ad fails to load.
- Rewarded: optional **revive once per session** from the death screen.
- **No IAP, no "remove ads", no banners, no paid version.**

The supplied IDs are wired into `KILLER_Master.cs`:

| | Android | iOS |
|---|---|---|
| Game ID | `28a9e5cc5` | `28aa1f545` |
| Interstitial unit | `eb6raa1p7kzgy1ji` | `8xyoxm786h3bz67d` |
| Rewarded unit | `c47seg9uly0wnn6d` | `w2clxqcqvnme68cd` |

**Before publishing, confirm these IDs in the dashboard of the ad account
that owns them.** Ad unit IDs are account-specific; do not ship IDs you do not
control.

By default the game runs with **clearly labelled simulated ads** so the whole
flow is testable with no SDK. For live ads: install the **Unity Ads** package
from Package Manager and add the scripting define **`KILLER_USE_UNITY_ADS`**
(Player Settings → Other Settings → Scripting Define Symbols). The same
single file then compiles against Unity Ads 4.x listeners; if the SDK or an
ad is unavailable, gameplay is never blocked (and a rewarded reward is never
granted for a failed load).

## Privacy & storage

- Settings (volume, sensitivity, quality) live in `PlayerPrefs`.
- Progress/stats live in one local JSON file:
  `Application.persistentDataPath/KILLER_save.json` (day, kills, unlocks, stats).
- The game code sends **no** gameplay, device, or personal data anywhere.
- Networking consists of: (1) a no-data connectivity ping for the online-only
  launch gate, and (2) the ad SDK you enable, which operates under that ad
  network's own privacy policy. Be transparent about (2) in your store listing.
- Version policy: **1.0.0 is the final version**; there is no update, cloud,
  analytics, or crash-reporting channel.

## Legal notice

KILLER is an original work created by Tishay Dangare. Any resemblance to
other games is coincidental. No third-party game assets, code, names, or
trademarks are included: the house is built from primitives, textures are
procedural noise, and every sound is synthesized at runtime.
See `REVERSE_ENGINEERING_NOTES.md` for the separation record.

## Troubleshooting

- **Stuck on "No internet connection"** — the launch gate needs connectivity; tap Retry.
- **No ads in the Editor** — expected: simulated ads are used until you install Unity Ads and add `KILLER_USE_UNITY_ADS`.
- **Can't find the key** — search every drawer; it is re-hidden each run.
- **Locked first-floor door** — get the hammer from the basement (south room).
- **Hunter seems to know your rooms** — he does: patrols bias toward your most-visited rooms each Day.
