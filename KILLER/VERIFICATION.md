# Verification Record

Environment available during authoring: Linux sandbox with Node/Python only —
**no Unity Editor, no .NET/Mono compiler, no Android SDK, no Xcode/Mac**.
That constrains what could honestly be verified:

## Verified here

- `KILLER_Master.cs` parses as valid C# (tree-sitter C# grammar,
  `has_error == False`), with balanced braces/parens/brackets.
- No third-party game name or studio name appears anywhere in the script.
- Ad IDs, package/bundle IDs (`com.tishaydangare.killer`), version `1.0.0`,
  min API 24 / iOS 14.0, IL2CPP settings are present in the editor build menus.
- Gameplay-critical wiring was desk-checked: key is hidden in exactly one
  random drawer per run; the escape door requires (targets == 0 && key);
  the hammer needed for the locked first-floor door is reachable in the
  basement without crossing that locked door; stair pads connect
  basement/ground/first floor both ways; Hunter BFS uses one waypoint graph
  across all floors.

## NOT verified here (requires Unity)

- Runtime compilation against a specific Unity version (2022.3 LTS / Unity 6)
  and against Unity Ads 4.x with `KILLER_USE_UNITY_ADS` defined.
- PlayMode behaviour: CharacterController stair/ramp feel, waypoint link
  quality in every doorway, Hunter kill-distance fairness, QTE timing feel,
  UI layout on notched/odd aspect ratios, and mobile frame rate.
- Device builds: producing `KILLER.apk` / `KILLER.ipa`, signing, and store
  submission (needs Android SDK / a Mac + your Apple Developer account).
- Live ad fill/behaviour with the supplied IDs (needs the owning ad account).

## Recommended 15-minute Unity smoke test

1. New 3D (Built-in) project → drop in `KILLER_Master.cs` → Play.
2. With internet on: splash → menu → New Game. Confirm HUD, movement,
   a drawer search, a closet hide, one QTE elimination, Hunter chase.
3. Die once → death screen → Retry (simulated interstitial) → confirm restart.
4. Toggle airplane mode at launch → confirm the Retry gate appears.
5. Build via **KILLER → Build Android APK**; install on a device and repeat 2–3
   with touch controls. Archive the iOS project from a Mac the same way.
