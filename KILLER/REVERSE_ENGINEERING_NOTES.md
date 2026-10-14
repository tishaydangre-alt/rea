# Reverse-Engineering Notes — Pattern-Level Only

## What was actually done (and not done)

This project was authored in a Linux sandbox with **no Unity, no Android SDK,
no Xcode, no REA MCP server connected, and no target game binary available**.
Accordingly:

- **No** APK/IPA of any commercial game was downloaded, dumped, decompiled,
  or executed. (Those binaries are copyrighted and usually DRM/ToS protected;
  obtaining them from unofficial sources would also be a supply-chain risk.)
- **No** IL2CPP metadata, `global-metadata.dat`, scene, prefab, animation,
  audio, or code from any existing game was inspected or reproduced.
- **No** claim is made that REA MCP or any third-party RE tool was run —
  it was not available in this environment.

What *was* used: the **functional brief itself** (genres, state names,
speeds/radii/timings supplied by the project owner in the request) and
general, non-copyrightable game-design patterns common to the
first-person stealth-horror genre. Game mechanics, tuning numbers, and
system architectures are not protected expression; everything expressive
in KILLER — names, text, house layout, characters, code, art, audio — is
original.

## Pattern → original implementation map

| Genre pattern (functional idea) | KILLER's original implementation |
|---|---|
| First-person controller with walk/run/crouch | `CharacterController`, 3.0 / 5.0 / 1.5 m/s, stamina, head-bob, Q/E lean, touch stick + look-drag |
| Antagonist patrols / investigates noise / chases | 5-state machine (Patrol, Suspicious, Search, Chase, Kill), BFS waypoint graph, 120° sight cone, noise events, 15 s memory, snap traps |
| Pursuer "learns" the player | Per-room visit heat-map biases patrol destinations; Day scaling raises speed/senses |
| Noise as a system | Single `EmitNoise(position, radius)` funnel for steps, doors, drawers, screams, radios, traps |
| Hide spots | Closets set a hidden state, zero the detection meter, break a chase into Search |
| Item gating | Drawer-hidden front-door key each run; basement hammer doubles as pry tool for the locked door |
| Day loop / escalating difficulty | Win increments Day; Hunter speed, sight, and patrol bias increase |
| Death/victory → interstitial; optional rewarded revive | Capped (~4/session, 60 s cooldown), fail-open, revive once per session |
| Local-only persistence | `PlayerPrefs` settings + versioned local JSON save; no backend |
| Online-only launch gate | No-data connectivity ping with Retry screen; game code transmits nothing |
| Atmosphere without assets | Procedural noise textures, TV static, synthesized drone/chase/stingers/footsteps (no audio files) |

## Separation checklist

- [x] Zero meshes, textures, animations, or audio copied from any game —
      primitives + procedural textures + synthesized clips only.
- [x] Zero code copied — one original C# file, all identifiers renamed
      (protagonist, antagonist, systems, save keys).
- [x] No third-party game name/trademark appears in code or in-game text.
- [x] Original IP: title, characters (Tishay Dangare; Vikram "The Shadow"
      Rao), house layout, writing, jokes, and posters are new.
- [x] In-game credits carry the originality disclaimer.
