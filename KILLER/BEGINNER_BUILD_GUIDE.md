# Beginner Build Guide — No Coding Needed

You do **not** need to understand or edit the code. You only need to:

1. Install **Unity** (the game-making app).
2. Put **one file** (`KILLER_Master.cs`) into a Unity project.
3. Click the menu **KILLER → Build Android APK** (for Android).
4. For iPhone, repeat on a **Mac** and finish in **Xcode**.

> **Do the Android APK first.** It works on a normal Windows/Mac computer.
> iPhone is second, because Apple requires a Mac.

---

## PART A — Android APK (Windows or Mac)

### A1. What you need

- A computer with about **20 GB free space** and internet.
- About **30–60 minutes** the first time (downloads are large).
- An Android phone to test (optional but recommended).
- Cost: **Free** for building/testing an APK.

### A2. Install Unity Hub

1. Go to **unity.com/download** and download **Unity Hub**.
2. Install and open Unity Hub.
3. Create/sign in with a free Unity account if it asks.
4. If it asks for a licence, choose the free **Personal** licence.

### A3. Install the correct Unity version

1. In Unity Hub, click **Installs** (left side) → **Install Editor**.
2. Choose **2022.3 LTS** (if you see several, pick the 2022.3 LTS one).
3. On the modules/components screen, tick:
   - **Android Build Support**
   - inside it: **Android SDK & NDK Tools** and **OpenJDK**
   - (tick **iOS Build Support** too only if this is a Mac you will also use for iPhone)
4. Click **Install** and wait for it to finish.

> If these boxes are missed, you can add them later: Unity Hub → Installs →
> the three dots `…` next to the editor → **Add modules**.

### A4. Create the empty game project

1. In Unity Hub, click **Projects** → **New project**.
2. Choose the **3D** template (may be called "3D (Built-in Render Pipeline)" or just "3D").
3. Project name: `KILLER`
4. Location: somewhere easy, like your **Desktop**.
5. Click **Create project**. Unity will open (first open can take a few minutes).

### A5. Add the one game file

1. Download `KILLER_Master.cs` from this repository's `KILLER` folder
   (on GitHub: open the file → download/raw button).
2. On your computer, open the new Unity project folder: `KILLER/Assets/`
   (inside the project folder you just created — the folder literally named **Assets**).
3. Copy **`KILLER_Master.cs`** into that **Assets** folder.
4. Go back to Unity and wait until the spinning/loading indicator at the
   bottom-right stops. Unity is "reading" the file — this is normal.

### A6. Test the game inside Unity (2 minutes)

1. Make sure you are connected to the internet (the game checks this at start).
2. Click the **Play ▶ button** at the top-centre of Unity.
3. You should see: **KILLER** splash → main menu → click **NEW GAME**.
4. Move with **W, A, S, D keys**, look with the **mouse**, press **F** to open/search things.
5. Click the **Play ▶ button again** to stop.

> If the game starts and you can walk around: everything worked. 🎉
> If Unity shows red errors at the bottom (Console), do not try to fix code —
> note the first red line and ask for help with a screenshot of it.

### A7. Create the APK file

1. In Unity's top menu, click **KILLER → Build Android APK**.
2. Wait. The first build can take **10–30 minutes**. Do not close Unity.
3. When it finishes, your file is here:

   ```
   (your project folder)/Builds/Android/KILLER.apk
   ```

That is your finished Android game file. ✅

### A8. Put the APK on your Android phone

**Easy way (USB cable):**

1. Connect the phone to the computer with a cable.
2. Copy `KILLER.apk` to the phone (e.g., into **Downloads**).
3. On the phone, open **Files/Downloads**, tap `KILLER.apk`, tap **Install**.
4. If the phone warns about "unknown apps", allow installing from your
   Files app just this once (this is normal for APKs outside the Play Store).
5. Open **KILLER** from your apps and play.

**Or:** send the APK to yourself (WhatsApp/Drive/email), download it on the
phone, and tap it to install as above.

### A9. If you later want it on the Google Play Store

- You need a **Google Play Developer account (US$25 one-time)**.
- Important: Google Play normally wants an **`.aab` file**, not `.apk`.
  The APK above is perfect for testing and sharing directly with people.
  When you are ready for the Play Store, ask for the one menu change
  ("Build Android App Bundle (.aab)") before building — it is a small change.
- Google will also ask you to fill in an age-rating questionnaire and a
  privacy policy (required once real ads are switched on).

---

## PART B — iPhone IPA (Mac required)

Apple's rule: **iPhone apps can only be finished on a Mac computer.**
There is no way around this — Windows cannot make a real iPhone build.

### B1. Your options if you don't own a Mac

| Option | Rough cost | Notes |
|---|---|---|
| Borrow a friend's Mac | Free | Easiest if possible. You need a few hours. |
| Rent a Mac online (e.g., MacinCloud, MacStadium) | Paid, hourly/monthly | A real Mac in your browser. Works for this. |
| Unity Build Automation / CI with macOS | Paid | More technical — not recommended as a first step. |
| Hire a freelancer to build it | Paid | Only share the `KILLER` folder; ask for the IPA + Xcode project back. Be careful with strangers and your accounts — never share your Apple password. |

### B2. What you need on the Mac

- The same **Unity 2022.3 LTS**, but with **iOS Build Support** ticked
  (Part A3 — add it via Unity Hub → Installs → `…` → Add modules if missing).
- **Xcode**: free from the Mac **App Store** (large download). Open it once
  and let it install its extra components.
- An **iPhone** to test.
- For a shareable IPA/TestFlight: **Apple Developer Program — US$99/year**.
  (With only a free Apple ID, Xcode can put the game on *your own* iPhone
  for a few days at a time, but you cannot properly distribute an IPA.)

### B3. Steps on the Mac

1. Copy your whole Unity project (the `KILLER` folder from Part A, including
   `Assets/KILLER_Master.cs`) to the Mac.
2. Open it in Unity Hub → **Open** → select the project folder.
3. Wait for Unity to finish loading, stay connected to the internet,
   and quickly press **Play ▶** once to confirm the menu appears. Stop it.
4. Click **KILLER → Build iOS Xcode Project**.
5. Wait for it to finish. It creates:

   ```
   (your project folder)/Builds/iOS/
   ```

   This is **not yet an IPA** — it is an Xcode project.
6. Open the **`.xcodeproj`** file inside `Builds/iOS` with **Xcode**.
7. In Xcode: click the project name (top-left) → **Signing & Capabilities** →
   tick **Automatically manage signing** → choose your **Team**
   (your Apple Developer account).
8. Bundle ID should already be: `com.tishaydangare.killer`
9. Choose at the top of Xcode:
   - your connected iPhone, then press **Run ▶** to test on your phone, **or**
   - **Any iOS Device**, then menu **Product → Archive**.
10. When the Archive finishes, click **Distribute App** →
    **TestFlight & App Store** (recommended) or **Export** to save an `.ipa` file.
11. **TestFlight is the easiest way to share it**: testers install Apple's
    free TestFlight app and get your game — no manual IPA handling.

> You do **not** install an IPA by tapping it like an Android APK.
> iPhone installs go through Xcode, TestFlight, or the App Store only.

### B4. If you later want it on the App Store

- Apple Developer Program (**US$99/year**) is required.
- You upload the build from Xcode (**Distribute App → App Store Connect**),
  then fill in the store page (name, screenshots, age rating, privacy answers)
  in **App Store Connect** in your browser, and submit for Apple's review.
- A horror game with violence will get an age rating (likely 17+) — answer
  the rating questions honestly.

---

## About the ads (read this before trying to earn money)

- Your first APK will show **test/simulated ads** ("ADVERTISEMENT (simulated)").
  That is correct for testing — no setup needed.
- Real money-earning ads need an account with the ad company and the
  `KILLER_USE_UNITY_ADS` switch turned on in Unity
  (install "Unity Ads" from Window → Package Manager, then
  Project Settings → Player → Scripting Define Symbols → add `KILLER_USE_UNITY_ADS`).
- The ad IDs must belong to **your** ad account. Do not publish the game with
  someone else's ad IDs — you would earn nothing and may get banned by the ad network.
- Once real ads are on, Google/Apple will require a **privacy policy page**
  (a simple free webpage) because ad networks collect ad-related data.

## Common problems (plain-English fixes)

| Problem | Fix |
|---|---|
| No **KILLER** menu at the top of Unity | The file is not in the project's **Assets** folder, or Unity is still loading. Wait, check A5. |
| Unity says Android SDK/NDK missing | Unity Hub → Installs → `…` → Add modules → tick Android SDK & NDK + OpenJDK (A3). |
| Game stops at "No internet connection" | Connect to Wi-Fi/data and tap **RETRY**. This is by design. |
| Phone refuses to install the APK | Allow "Install via USB / unknown apps" for your Files app once (A8). |
| Build seems stuck | First builds are slow (10–30 min). As long as the progress bar moves occasionally, wait. |
| Xcode says no signing team | B3 step 7: select your Team under Signing & Capabilities. Paid Apple Developer account is needed for TestFlight/App Store. |
| Red errors in Unity Console | Copy/screenshot the **first** red error line and ask for help — don't edit the code yourself. |

## The 1-minute summary

```
Android:  Unity Hub → install Unity 2022.3 LTS (+ Android modules)
          → New 3D project → copy KILLER_Master.cs into Assets
          → Play to test → KILLER menu → Build Android APK
          → copy Builds/Android/KILLER.apk to phone → tap → Install

iPhone:   Same project on a Mac (+ iOS module + Xcode)
          → KILLER menu → Build iOS Xcode Project
          → open in Xcode → pick your signing Team
          → Product → Archive → Distribute App (TestFlight) → IPA/App Store
```
