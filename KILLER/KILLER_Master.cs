// ============================================================================
//  KILLER — Version 1.0.0 (final, never-updated release)
//  Created by Tishay Dangare
//
//  A completely original first-person stealth-horror game contained in ONE
//  C# file. No scene setup, no prefabs, no external assets are required:
//  the house, characters, UI, audio and save data are all built at runtime.
//
//  HOW TO USE
//  ----------
//  1. Create an empty Unity 2022.3 LTS (or Unity 6) 3D project.
//  2. Copy this file into the project's Assets folder.
//  3. Press Play — OR use the menu:  KILLER > Build Android APK
//                                    KILLER > Build iOS Xcode Project
//     An empty bootstrap scene is created automatically by those menus.
//
//  ADS (optional, for a store build)
//  ---------------------------------
//  Install "Unity Ads" from Package Manager, then add the scripting define
//  KILLER_USE_UNITY_ADS (Project Settings > Player > Scripting Defines).
//  Without that define the game uses a clearly-labelled simulated ad panel
//  so the complete flow can be tested in the Editor with no SDK installed.
//  Verify the IDs below in your own ad dashboard before publishing — IDs
//  are account-specific.
//
//  PRIVACY BY DESIGN
//  -----------------
//  * Saves/settings are local only (PlayerPrefs + one local JSON file).
//  * The only network calls are (a) a no-data connectivity ping and
//    (b) the ad SDK you choose to enable. No analytics, no cloud, no IAP.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if KILLER_USE_UNITY_ADS
using UnityEngine.Advertisements;
#endif

namespace KillerGame
{
    // ------------------------------------------------------------------ save
    [Serializable]
    public class KillerSettingsData
    {
        public float volume = 0.8f;
        public float sensitivity = 2.0f;
        public int quality = 1; // 0 low, 1 medium, 2 high
    }

    [Serializable]
    public class KillerStatsData
    {
        public int deaths = 0;
        public int escapes = 0;
        public float playTimeSeconds = 0f;
    }

    [Serializable]
    public class KillerSaveData
    {
        public string version = "1.0.0";
        public int day = 1;
        public int killsThisRun = 0;
        public int totalKills = 0;
        public bool hammerUnlocked = false;
        public KillerSettingsData settings = new KillerSettingsData();
        public KillerStatsData stats = new KillerStatsData();
    }

    // ------------------------------------------------------------------ main
    public class KILLER_Master : MonoBehaviour
    {
        public const string GameVersion = "1.0.0";
        public const string CreatorCredit = "Created by Tishay Dangare";

        // ------------------------------------------------------------ ad IDs
        // IDs supplied by the project owner. Confirm them in the dashboard of
        // the ad account that owns them before any public release.
#if UNITY_ANDROID
        private const string AdsGameId = "28a9e5cc5";
        private const string InterstitialId = "eb6raa1p7kzgy1ji";
        private const string RewardedId = "c47seg9uly0wnn6d";
#elif UNITY_IOS
        private const string AdsGameId = "28aa1f545";
        private const string InterstitialId = "8xyoxm786h3bz67d";
        private const string RewardedId = "w2clxqcqvnme68cd";
#else
        // Editor / standalone test values (simulated ads unless SDK enabled).
        private const string AdsGameId = "28a9e5cc5";
        private const string InterstitialId = "eb6raa1p7kzgy1ji";
        private const string RewardedId = "c47seg9uly0wnn6d";
#endif
        private const int MaxInterstitialsPerSession = 4; // target frequency
        private const float InterstitialCooldownSeconds = 60f;

        // ---------------------------------------------------------- tuning
        private const float WalkSpeed = 3.0f;
        private const float RunSpeed = 5.0f;
        private const float CrouchSpeed = 1.5f;
        private const float InteractDistance = 2.8f;
        private const float HunterKillDistance = 1.5f;
        private const float FloorHeight = 3.2f;

        private enum GameState { Boot, CheckingOnline, Splash, MainMenu, Playing, Paused, GameOver, Victory, NoInternet }
        private enum HunterState { Patrol, Suspicious, Search, Chase, Kill }
        private enum InteractKind { Door, Drawer, Closet, Lamp, Radio, PickupKey, PickupHammer, EscapeDoor, Note, Stair }

        private sealed class DoorData
        {
            public GameObject pivot; public GameObject panel; public Interactable inter;
            public float closedY; public float openAmount; public bool open; public bool locked; public string lockId;
        }
        private sealed class Interactable
        {
            public InteractKind kind; public GameObject root; public string label;
            public DoorData door; public bool used; public bool containsKey; public bool on;
            public Vector3 closedPos; public Light lampLight; public Renderer lampBulb; public AudioSource radioAudio;
            public string noteText; public Vector3 destination; public bool hasDestination;
        }
        private sealed class TargetData
        {
            public GameObject root; public GameObject body; public bool alive = true;
            public List<Vector3> path; public int pathIndex; public float waitTimer; public float speed = 1.1f;
            public bool fleeing; public float fleeTimer; public float fallAmount; public Vector3 home;
        }
        private sealed class Node
        {
            public Vector3 pos; public List<int> links = new List<int>();
        }
        private sealed class TrapData { public GameObject root; public bool armed = true; }

        // ------------------------------------------------------------- state
        private static KILLER_Master _instance;
        private GameState _state = GameState.Boot;
        private KillerSaveData _save;
        private string _savePath;
        private bool _onlineOk;
        private bool _worldBuilt;
        private bool _adInitialized;
        private bool _interstitialLoaded;
        private bool _rewardedLoaded;
        private bool _showingAd;
        private int _interstitialsShown;
        private float _lastInterstitialAt = -999f;
        private Action _afterAd;
        private Action _afterReward;
        private bool _reviveUsedThisSession;

        // player
        private GameObject _playerObj; private CharacterController _cc; private Camera _cam;
        private Transform _camT; private GameObject _flashlightObj;
        private float _yaw; private float _pitch; private float _camHeight = 1.62f;
        private bool _crouching; private bool _hidden; private Vector3 _hideReturnPos; private Interactable _hideCloset;
        private float _stamina = 100f; private bool _exhausted; private float _slowTimer;
        private float _stepTimer; private float _headBobPhase; private float _leanAmount;
        private int _targetsRemaining; private int _killsThisRun; private bool _hasFrontKey;
        private float _playTimer;

        // hunter — "Vikram 'The Shadow' Rao", a vigilante investigator.
        private GameObject _hunterObj; private Transform _hunterT; private Light _hunterLight;
        private HunterState _hunterState = HunterState.Patrol;
        private List<Vector3> _hunterPath; private int _hunterPathIndex;
        private Vector3 _hunterTargetPos; private Vector3 _lastKnownPlayerPos;
        private float _hunterStateTimer; private float _perceptionTimer; private float _memoryTimer;
        private float _hunterStepTimer; private float _trapTimer = 40f; private float _seenMeter;
        private readonly Dictionary<string, int> _roomHeat = new Dictionary<string, int>();
        private float _heatTimer;

        // world collections
        private readonly List<DoorData> _doors = new List<DoorData>();
        private readonly List<Interactable> _interactables = new List<Interactable>();
        private readonly List<TargetData> _targets = new List<TargetData>();
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<TrapData> _traps = new List<TrapData>();
        private readonly List<Vector3> _targetSpawns = new List<Vector3>();
        private readonly List<Vector3> _roomCentres = new List<Vector3>();
        private readonly Dictionary<GameObject, Interactable> _interactByObject = new Dictionary<GameObject, Interactable>();
        private Material _matWall, _matWall2, _matFloor, _matWood, _matDark, _matMetal, _matRed, _matWhite, _matBlack, _matSkin, _matShirt, _matGlow, _matStatic;
        private Texture2D _staticTexture;

        // audio
        private AudioSource _sfx, _music, _ambience, _hunterSfx;
        private AudioClip _clipClick, _clipStep, _clipHunterStep, _clipCreak, _clipStinger, _clipScream, _clipSwing, _clipDrone, _clipChase, _clipPickup, _clipTrap;

        // QTE
        private bool _qteActive; private float _qteTime; private float _qteDuration = 1.15f;
        private float _qteGreenStart, _qteGreenEnd; private TargetData _qteTarget;

        // UI
        private Font _font;
        private GameObject _splashPanel, _menuPanel, _hudPanel, _pausePanel, _overPanel, _winPanel, _offlinePanel, _adPanel, _settingsPanel, _creditsPanel, _scareOverlay, _touchPanel;
        private Text _splashSub, _offlineStatus, _hudObjective, _hudDay, _hudPrompt, _hudNoiseLabel, _subtitle, _menuStats, _overText, _winText, _adText, _qteLabel, _settingsLabel;
        private Image _staminaFill, _noiseFill, _qteMarker, _vignette;
        private Slider _volumeSlider, _sensSlider;
        private float _subtitleTimer, _scareTimer, _messageTimer;
        private Interactable _currentInteract;
        private string _monologueIndex;
        private int _lastMonologue = -1;
        private bool _attackQueued, _interactQueued, _touchRun, _touchCrouch;
        private int _lookTouchId = -1; private Vector2 _lookTouchLast;
        private int _moveTouchId = -1; private Vector2 _moveTouchStart, _moveTouchNow;

        private static readonly string[] KillLines =
        {
            "Bro really thought he could hide from me.",
            "Ancestor voices: 'That's one more for the family album.'",
            "Skill issue. Genuinely.",
            "He never even heard the floor creak. Rookie.",
            "The voices are clapping. That's new.",
            "One less witness. One more family legend."
        };
        private static readonly string[] SpottedLines =
        {
            "Okay. He found me. Less fun now.",
            "The Shadow is here — MOVE.",
            "Generations of hunters, and he picks tonight?"
        };

        // --------------------------------------------------------- bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("KILLER_Master");
            DontDestroyOnLoad(go);
            go.AddComponent<KILLER_Master>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            _savePath = Path.Combine(Application.persistentDataPath, "KILLER_save.json");
            LoadSave();
            BuildAudioClips();
            CreateAudioSources();
            BuildUi();
            BuildWorld();
            ApplySettings(false);
            ShowPanel(_splashPanel);
            _state = GameState.CheckingOnline;
            StartCoroutine(OnlineGate());
        }

        // ------------------------------------------------------ online gate
        // No user data is sent: this is a headers-only connectivity check.
        private IEnumerator OnlineGate()
        {
            _state = GameState.CheckingOnline;
            if (_offlineStatus != null) _offlineStatus.text = "Checking connection...";
            using (var req = UnityWebRequest.Get("https://www.google.com/generate_204"))
            {
                req.timeout = 5;
                yield return req.SendWebRequest();
                long code = req.responseCode;
                _onlineOk = req.result == UnityWebRequest.Result.Success && (code == 200 || code == 204);
            }
            if (_onlineOk)
            {
                InitializeAds();
                _state = GameState.Splash;
                yield return new WaitForSecondsRealtime(1.8f);
                ShowMainMenu();
            }
            else
            {
                _state = GameState.NoInternet;
                ShowPanel(_offlinePanel);
                if (_offlineStatus != null)
                    _offlineStatus.text = "No internet connection.\nKILLER is online-only. Check your connection and retry.";
            }
        }

        private void RetryOnline() { ShowPanel(_splashPanel); StartCoroutine(OnlineGate()); }

        // --------------------------------------------------------------- ads
        private void InitializeAds()
        {
#if KILLER_USE_UNITY_ADS
            try
            {
                EnsureAdsCallbacks();
                Advertisement.Initialize(AdsGameId, false, _adsCallbacks);
            }
            catch (Exception e) { Debug.LogWarning("[KILLER] Ads init failed: " + e.Message); }
#else
            _adInitialized = true; _interstitialLoaded = true; _rewardedLoaded = true;
            Debug.Log("[KILLER] Simulated ads active (define KILLER_USE_UNITY_ADS with the Unity Ads package for live ads). GameId=" + AdsGameId);
#endif
        }

        private void LoadAds()
        {
#if KILLER_USE_UNITY_ADS
            try { Advertisement.Load(InterstitialId, _adsCallbacks); Advertisement.Load(RewardedId, _adsCallbacks); }
            catch (Exception e) { Debug.LogWarning("[KILLER] Ad load failed: " + e.Message); }
#else
            _interstitialLoaded = true; _rewardedLoaded = true;
#endif
        }

        /// <summary>Shows an interstitial, then ALWAYS runs <paramref name="after"/>.</summary>
        private void ShowInterstitial(string reason, Action after)
        {
            _afterAd = after;
            if (_showingAd) { RunAfterAd(); return; }
            if (_interstitialsShown >= MaxInterstitialsPerSession) { RunAfterAd(); return; }
            if (Time.unscaledTime - _lastInterstitialAt < InterstitialCooldownSeconds) { RunAfterAd(); return; }
            _interstitialsShown++;
            _lastInterstitialAt = Time.unscaledTime;
            _showingAd = true;
            Debug.Log("[KILLER] Interstitial trigger: " + reason + " (" + _interstitialsShown + "/" + MaxInterstitialsPerSession + ")");
#if KILLER_USE_UNITY_ADS
            if (_adInitialized && _interstitialLoaded)
            {
                try { Advertisement.Show(InterstitialId, _adsCallbacks); return; }
                catch (Exception e) { Debug.LogWarning("[KILLER] Ad show failed: " + e.Message); }
            }
            _showingAd = false; RunAfterAd();
#else
            StartCoroutine(SimulatedAd(false));
#endif
        }

        private void ShowRewarded(Action onReward)
        {
            _afterReward = onReward;
            if (_showingAd) return;
            _showingAd = true;
#if KILLER_USE_UNITY_ADS
            if (_adInitialized && _rewardedLoaded)
            {
                try { Advertisement.Show(RewardedId, _adsCallbacks); return; }
                catch (Exception e) { Debug.LogWarning("[KILLER] Rewarded show failed: " + e.Message); }
            }
            _showingAd = false;
            // In a live build, never grant a reward for a failed load:
            Debug.LogWarning("[KILLER] Rewarded ad unavailable; no reward granted.");
#else
            StartCoroutine(SimulatedAd(true));
#endif
        }

        private IEnumerator SimulatedAd(bool rewarded)
        {
            if (_adPanel != null) { _adPanel.SetActive(true); _adPanel.transform.SetAsLastSibling(); }
            if (_adText != null) _adText.text = rewarded ? "REWARDED AD (simulated)\nYour reward is on its way..." : "ADVERTISEMENT (simulated)\nKILLER — Created by Tishay Dangare";
            yield return new WaitForSecondsRealtime(1.5f);
            _adPanel.SetActive(false);
            _showingAd = false;
            if (rewarded) { var cb = _afterReward; _afterReward = null; if (cb != null) cb(); }
            else RunAfterAd();
            RestorePanelForState();
        }

        private void RunAfterAd() { var cb = _afterAd; _afterAd = null; if (cb != null) cb(); }

#if KILLER_USE_UNITY_ADS
        private AdsCallbacks _adsCallbacks;
        private void EnsureAdsCallbacks() { if (_adsCallbacks == null) _adsCallbacks = new AdsCallbacks(this); }

        // Unity Ads listener object (keeps the SDK optional at compile time).
        private sealed class AdsCallbacks : IUnityAdsInitializationListener, IUnityAdsLoadListener, IUnityAdsShowListener
        {
            private readonly KILLER_Master _o;
            public AdsCallbacks(KILLER_Master o) { _o = o; }
            public void OnInitializationComplete() { _o._adInitialized = true; _o.LoadAds(); }
            public void OnInitializationFailed(UnityAdsInitializationError error, string message)
            { _o._adInitialized = false; Debug.LogWarning("[KILLER] Ads init failed: " + message); }
            public void OnUnityAdsAdLoaded(string placementId)
            {
                if (placementId == InterstitialId) _o._interstitialLoaded = true;
                if (placementId == RewardedId) _o._rewardedLoaded = true;
            }
            public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
            {
                Debug.LogWarning("[KILLER] Ad failed to load: " + placementId + " " + message);
                if (placementId == InterstitialId) _o._interstitialLoaded = false;
                if (placementId == RewardedId) _o._rewardedLoaded = false;
            }
            public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
            {
                Debug.LogWarning("[KILLER] Ad show failure: " + message);
                _o._showingAd = false;
                if (placementId == RewardedId) _o._afterReward = null; else _o.RunAfterAd();
                _o.RestorePanelForState();
            }
            public void OnUnityAdsShowStart(string placementId) { }
            public void OnUnityAdsShowClick(string placementId) { }
            public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState state)
            {
                _o._showingAd = false;
                if (placementId == InterstitialId) { _o._interstitialLoaded = false; _o.RunAfterAd(); }
                else if (placementId == RewardedId)
                {
                    _o._rewardedLoaded = false;
                    if (state == UnityAdsShowCompletionState.COMPLETED) { var cb = _o._afterReward; _o._afterReward = null; if (cb != null) cb(); }
                    else _o._afterReward = null;
                }
                _o.LoadAds();
                _o.RestorePanelForState();
            }
        }
#endif

        // -------------------------------------------------------------- save
        private void LoadSave()
        {
            _save = new KillerSaveData();
            try
            {
                if (File.Exists(_savePath))
                {
                    var loaded = JsonUtility.FromJson<KillerSaveData>(File.ReadAllText(_savePath));
                    if (loaded != null && loaded.settings != null && loaded.stats != null) _save = loaded;
                }
            }
            catch (Exception e) { Debug.LogWarning("[KILLER] Save load failed, using defaults: " + e.Message); }
            // PlayerPrefs mirror for settings (fast access, local only).
            if (PlayerPrefs.HasKey("killer_volume")) _save.settings.volume = PlayerPrefs.GetFloat("killer_volume");
            if (PlayerPrefs.HasKey("killer_sens")) _save.settings.sensitivity = PlayerPrefs.GetFloat("killer_sens");
            if (PlayerPrefs.HasKey("killer_quality")) _save.settings.quality = PlayerPrefs.GetInt("killer_quality");
        }

        private void WriteSave()
        {
            try
            {
                _save.version = GameVersion;
                File.WriteAllText(_savePath, JsonUtility.ToJson(_save, true));
                PlayerPrefs.SetFloat("killer_volume", _save.settings.volume);
                PlayerPrefs.SetFloat("killer_sens", _save.settings.sensitivity);
                PlayerPrefs.SetInt("killer_quality", _save.settings.quality);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[KILLER] Save write failed: " + e.Message); }
        }

        private void ApplySettings(bool persist)
        {
            AudioListener.volume = Mathf.Clamp01(_save.settings.volume);
            int q = Mathf.Clamp(_save.settings.quality, 0, 2);
            if (QualitySettings.names.Length > 0)
                QualitySettings.SetQualityLevel(Mathf.Min(q, QualitySettings.names.Length - 1), true);
            if (_volumeSlider != null) _volumeSlider.value = _save.settings.volume;
            if (_sensSlider != null) _sensSlider.value = _save.settings.sensitivity;
            if (_settingsLabel != null) _settingsLabel.text = SettingsText();
            if (persist) WriteSave();
        }

        private string SettingsText()
        {
            string[] names = { "Low", "Medium", "High" };
            return "Volume " + Mathf.RoundToInt(_save.settings.volume * 100f) + "%   |   Look sensitivity " +
                   _save.settings.sensitivity.ToString("0.0") + "   |   Quality: " + names[Mathf.Clamp(_save.settings.quality, 0, 2)] +
                   "\nChanges are stored locally only.";
        }

        // ------------------------------------------------------------- audio
        private void CreateAudioSources()
        {
            _sfx = gameObject.AddComponent<AudioSource>(); _sfx.playOnAwake = false;
            _music = gameObject.AddComponent<AudioSource>(); _music.playOnAwake = false; _music.loop = true; _music.volume = 0f;
            _ambience = gameObject.AddComponent<AudioSource>(); _ambience.playOnAwake = false; _ambience.loop = true; _ambience.volume = 0.16f;
        }

        private AudioClip ToneClip(string name, float seconds, Func<double, double, float> sample)
        {
            const int rate = 22050;
            int n = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (double)rate, i / (double)n), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void BuildAudioClips()
        {
            // All audio is synthesized — no audio files are bundled or copied.
            _clipClick = ToneClip("click", 0.08f, (t, p) => (float)(Math.Sin(2 * Math.PI * 880 * t) * (1 - p) * 0.5));
            _clipPickup = ToneClip("pickup", 0.18f, (t, p) => (float)(Math.Sin(2 * Math.PI * (520 + 500 * p) * t) * (1 - p) * 0.5));
            _clipStep = ToneClip("step", 0.10f, (t, p) => (float)((HashNoise(t * 91.7) * 2 - 1) * (1 - p) * (1 - p) * 0.55));
            _clipHunterStep = ToneClip("hstep", 0.14f, (t, p) => (float)((HashNoise(t * 61.3) * 2 - 1) * (1 - p) * 0.8 + Math.Sin(2 * Math.PI * 70 * t) * (1 - p) * 0.3));
            _clipCreak = ToneClip("creak", 0.4f, (t, p) => (float)(Math.Sin(2 * Math.PI * (170 - 80 * p + 12 * Math.Sin(2 * Math.PI * 9 * t)) * t) * Math.Sin(Math.PI * p) * 0.35));
            _clipSwing = ToneClip("swing", 0.16f, (t, p) => (float)((HashNoise(t * 140) * 2 - 1) * Math.Sin(Math.PI * p) * 0.4));
            _clipScream = ToneClip("scream", 0.55f, (t, p) => (float)((Math.Sin(2 * Math.PI * (950 - 600 * p) * t) + 0.4 * Math.Sin(2 * Math.PI * 1900 * t)) * (1 - p) * 0.4));
            _clipTrap = ToneClip("trap", 0.25f, (t, p) => (float)((Math.Sin(2 * Math.PI * 1400 * t) * (1 - p) + (HashNoise(t * 200) * 2 - 1) * (1 - p)) * 0.4));
            _clipStinger = ToneClip("stinger", 0.7f, (t, p) => (float)((Math.Sin(2 * Math.PI * 110 * t) + Math.Sin(2 * Math.PI * 116.5 * t) + Math.Sin(2 * Math.PI * 440 * t)) * (1 - p) * 0.30));
            _clipDrone = ToneClip("drone", 4f, (t, p) => (float)((Math.Sin(2 * Math.PI * 55 * t) + 0.6 * Math.Sin(2 * Math.PI * 82.5 * t) + 0.15 * (HashNoise(t * 3.1) * 2 - 1)) * 0.22));
            _clipChase = ToneClip("chase", 2f, (t, p) =>
            {
                double beat = (t % 0.5) / 0.5;
                return (float)((Math.Sin(2 * Math.PI * 65 * t) * (1 - beat) + 0.3 * Math.Sin(2 * Math.PI * 220 * t) * (beat < 0.12 ? 1 : 0)) * 0.4);
            });
        }

        private static double HashNoise(double x)
        {
            double s = Math.Sin(x * 127.1 + 311.7) * 43758.5453;
            return s - Math.Floor(s);
        }

        private void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (_sfx != null && clip != null) _sfx.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        // ------------------------------------------------------------- world
        private void BuildWorld()
        {
            if (_worldBuilt) return;
            _worldBuilt = true;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.ambientLight = new Color(0.22f, 0.22f, 0.26f);

            var sunGo = new GameObject("MoonLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 0.25f; sun.color = new Color(0.6f, 0.7f, 1f);
            sunGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            BuildMaterials();
            BuildHouse();
            BuildWaypoints();
            CreatePlayer();
            CreateHunter();
            CreateTargets();

            if (_ambience != null && _clipDrone != null) { _ambience.clip = _clipDrone; _ambience.Play(); }
            if (_music != null && _clipChase != null) { _music.clip = _clipChase; _music.Play(); }
        }

        private Texture2D NoiseTexture(int size, Color baseCol, float variance, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var rnd = new System.Random(seed);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = 1f + (float)(rnd.NextDouble() * 2 - 1) * variance;
                px[i] = new Color(Mathf.Clamp01(baseCol.r * v), Mathf.Clamp01(baseCol.g * v), Mathf.Clamp01(baseCol.b * v));
            }
            tex.SetPixels(px); tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private Material MakeMat(string name, Color col, float variance, int seed, float metallic = 0f, float smoothness = 0.15f)
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.color = col;
            m.mainTexture = NoiseTexture(128, Color.white, variance, seed);
            m.metallic = metallic; m.smoothness = smoothness;
            return m;
        }

        private void BuildMaterials()
        {
            _matWall = MakeMat("wall", new Color(0.42f, 0.40f, 0.38f), 0.18f, 11);
            _matWall2 = MakeMat("wall2", new Color(0.30f, 0.28f, 0.33f), 0.18f, 23);
            _matFloor = MakeMat("floor", new Color(0.28f, 0.20f, 0.13f), 0.22f, 37);
            _matWood = MakeMat("wood", new Color(0.36f, 0.23f, 0.12f), 0.2f, 41);
            _matDark = MakeMat("dark", new Color(0.08f, 0.08f, 0.10f), 0.1f, 53);
            _matMetal = MakeMat("metal", new Color(0.45f, 0.47f, 0.50f), 0.1f, 67, 0.7f, 0.45f);
            _matRed = MakeMat("red", new Color(0.55f, 0.06f, 0.06f), 0.12f, 71);
            _matWhite = MakeMat("white", new Color(0.85f, 0.84f, 0.80f), 0.08f, 83);
            _matBlack = MakeMat("black", new Color(0.03f, 0.03f, 0.04f), 0.05f, 89);
            _matSkin = MakeMat("skin", new Color(0.72f, 0.52f, 0.38f), 0.08f, 97);
            _matShirt = MakeMat("shirt", new Color(0.15f, 0.30f, 0.55f), 0.12f, 103);
            _matGlow = MakeMat("glow", new Color(1f, 0.85f, 0.45f), 0.05f, 109);
            _matGlow.EnableKeyword("_EMISSION");
            _matGlow.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.3f) * 1.6f);

            _staticTexture = new Texture2D(128, 96, TextureFormat.RGB24, false);
            _matStatic = new Material(Shader.Find("Unlit/Texture")) { mainTexture = _staticTexture };
            RefreshStaticTexture();
        }

        private void RefreshStaticTexture()
        {
            if (_staticTexture == null) return;
            var rnd = new System.Random((int)(Time.time * 20f));
            int w = _staticTexture.width, h = _staticTexture.height;
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) { float g = (float)rnd.NextDouble(); px[i] = new Color(g, g, g); }
            _staticTexture.SetPixels(px); _staticTexture.Apply();
        }

        private GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent = null, int layer = 8)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            if (parent != null) go.transform.SetParent(parent, true);
            if (mat != null) go.GetComponent<Renderer>().material = mat;
            if (layer >= 0) go.layer = layer;
            return go;
        }

        // Wall along X with an optional door gap (gapCentre is an X position).
        private void WallX(float x1, float x2, float z, float baseY, float doorCentre, bool hasDoor, Material mat, bool makeDoor, bool locked, string lockId)
        {
            const float h = FloorHeight, thick = 0.3f, doorW = 1.15f, doorH = 2.15f;
            if (!hasDoor)
            {
                Box("Wall", new Vector3((x1 + x2) / 2f, baseY + h / 2f, z), new Vector3(Mathf.Abs(x2 - x1), h, thick), mat);
                return;
            }
            float a = Mathf.Min(x1, x2), b = Mathf.Max(x1, x2);
            float g1 = doorCentre - doorW / 2f, g2 = doorCentre + doorW / 2f;
            if (g1 > a) Box("Wall", new Vector3((a + g1) / 2f, baseY + h / 2f, z), new Vector3(g1 - a, h, thick), mat);
            if (b > g2) Box("Wall", new Vector3((g2 + b) / 2f, baseY + h / 2f, z), new Vector3(b - g2, h, thick), mat);
            Box("Lintel", new Vector3(doorCentre, baseY + doorH + (h - doorH) / 2f, z), new Vector3(doorW, h - doorH, thick), mat);
            if (makeDoor) CreateDoor(new Vector3(g1, baseY, z), doorW, doorH, 0f, locked, lockId, false);
        }

        private void WallZ(float z1, float z2, float x, float baseY, float doorCentre, bool hasDoor, Material mat, bool makeDoor, bool locked, string lockId)
        {
            const float h = FloorHeight, thick = 0.3f, doorW = 1.15f, doorH = 2.15f;
            if (!hasDoor)
            {
                Box("Wall", new Vector3(x, baseY + h / 2f, (z1 + z2) / 2f), new Vector3(thick, h, Mathf.Abs(z2 - z1)), mat);
                return;
            }
            float a = Mathf.Min(z1, z2), b = Mathf.Max(z1, z2);
            float g1 = doorCentre - doorW / 2f, g2 = doorCentre + doorW / 2f;
            if (g1 > a) Box("Wall", new Vector3(x, baseY + h / 2f, (a + g1) / 2f), new Vector3(thick, h, g1 - a), mat);
            if (b > g2) Box("Wall", new Vector3(x, baseY + h / 2f, (g2 + b) / 2f), new Vector3(thick, h, b - g2), mat);
            Box("Lintel", new Vector3(x, baseY + doorH + (h - doorH) / 2f, doorCentre), new Vector3(thick, h - doorH, doorW), mat);
            if (makeDoor) CreateDoor(new Vector3(x, baseY, g1), doorW, doorH, 90f, locked, lockId, true);
        }

        private void CreateDoor(Vector3 hingePos, float width, float height, float closedY, bool locked, string lockId, bool alongZ)
        {
            var pivot = new GameObject("DoorPivot");
            pivot.transform.position = hingePos;
            pivot.transform.rotation = Quaternion.Euler(0f, closedY, 0f);
            var panel = Box("Door", Vector3.zero, new Vector3(width, height, 0.12f), _matWood, pivot.transform, 0);
            panel.transform.localPosition = new Vector3(width / 2f, height / 2f, 0f);
            panel.transform.localRotation = Quaternion.identity;
            // Along-Z doors swing around the same pivot; panel offset is local X by design.
            var d = new DoorData { pivot = pivot, panel = panel, closedY = closedY, locked = locked, lockId = lockId };
            var inter = new Interactable { kind = InteractKind.Door, root = panel, label = locked ? "Locked door" : "Door", door = d };
            d.inter = inter;
            _doors.Add(d);
            _interactables.Add(inter);
            _interactByObject[panel] = inter;
        }

        private void BuildHouse()
        {
            // House footprint: X -13..13, Z -10..10. Floors: basement -3.2, ground 0, first +3.2.
            float[] bases = { -FloorHeight, 0f, FloorHeight };
            // Floor slabs (first-floor slab leaves a stairwell hole at X 7..13, Z 3..9).
            Box("BasementFloor", new Vector3(0, -FloorHeight - 0.15f, 0), new Vector3(26f, 0.3f, 20f), _matDark, null, 0);
            Box("GroundFloor", new Vector3(0, -0.15f, 0), new Vector3(26f, 0.3f, 20f), _matFloor, null, 0);
            Box("FirstFloorA", new Vector3(-3f, FloorHeight - 0.15f, 0), new Vector3(20f, 0.3f, 20f), _matFloor, null, 0);
            Box("FirstFloorB", new Vector3(10f, FloorHeight - 0.15f, -3.5f), new Vector3(6f, 0.3f, 13f), _matFloor, null, 0);
            Box("Ceiling", new Vector3(0, FloorHeight * 2f + 0.15f, 0), new Vector3(26f, 0.3f, 20f), _matDark, null, 0);

            for (int f = 0; f < bases.Length; f++)
            {
                float y = bases[f];
                Material wallMat = f == 1 ? _matWall : _matWall2;
                bool isFirst = f == 2, isGround = f == 1;
                // Exterior (front door gap only on the ground floor, south wall).
                WallX(-13f, 13f, -10f, y, 0f, isGround, wallMat, false, false, null);
                WallX(-13f, 13f, 10f, y, 0f, false, wallMat, false, false, null);
                WallZ(-10f, 10f, -13f, y, 0f, false, wallMat, false, false, null);
                WallZ(-10f, 10f, 13f, y, 0f, false, wallMat, false, false, null);
                // Interior partitions with closable doors.
                WallZ(-10f, 2f, -4f, y, -4f, true, wallMat, true, false, null);
                WallZ(-10f, 3f, 5f, y, -2f, true, wallMat, true, isFirst, isFirst ? "hammer" : null);
                WallX(-13f, 5f, 2f, y, -8f, true, wallMat, true, false, null);
                WallX(5f, 7f, 3f, y, 0f, false, wallMat, false, false, null); // keep stairwell open
                if (f == 0)
                {
                    // Basement storage door starts unlocked so the hammer/pry tool
                    // inside can be reached; it is what unlocks the first-floor door.
                    WallX(-13f, 13f, 0f, y, -8f, true, wallMat, true, false, null);
                }
            }

            // Front escape door (physical, at the south gap).
            CreateDoor(new Vector3(-0.575f, 0f, -10f), 1.15f, 2.15f, 0f, false, null, false);
            var escapeInter = _interactables[_interactables.Count - 1];
            escapeInter.kind = InteractKind.EscapeDoor; escapeInter.label = "Front door — escape";

            // Stair ramps (rotated slabs the CharacterController can climb).
            CreateRamp(new Vector3(10f, FloorHeight / 2f - 0.1f, 6f), new Vector3(0.3f, 7.2f, 3.4f), -63f);
            CreateRamp(new Vector3(-10f, -FloorHeight / 2f - 0.1f, 6f), new Vector3(0.3f, 7.2f, 3.4f), 63f);
            Box("AtticSign", new Vector3(9.8f, FloorHeight + 2.6f, 2.8f), new Vector3(2.2f, 0.5f, 0.1f), _matDark, null, 0);

            // Room centres (used for Hunter "learning", spawns, labels).
            _roomCentres.AddRange(new[]
            {
                new Vector3(-8.5f, 0, -6f), new Vector3(9f, 0, -6f), new Vector3(-8.5f, 0, 6f),
                new Vector3(0f, 0, 0f), new Vector3(9f, 0, 6.5f),
                new Vector3(-8.5f, FloorHeight, -6f), new Vector3(9f, FloorHeight, -6f),
                new Vector3(-8.5f, FloorHeight, 6f), new Vector3(0f, FloorHeight, 0f),
                new Vector3(-8f, -FloorHeight, -5f), new Vector3(8f, -FloorHeight, 5f)
            });
            _targetSpawns.AddRange(new[]
            {
                new Vector3(-8.5f, 0, -6f), new Vector3(9f, 0, -6f), new Vector3(-8.5f, FloorHeight, -6f),
                new Vector3(9f, FloorHeight, -6f), new Vector3(-8f, -FloorHeight, -5f)
            });

            BuildFurniture();
        }

        private void CreateRamp(Vector3 pos, Vector3 size, float zTilt)
        {
            var go = Box("Stairs", pos, size, _matWood, null, 0);
            go.transform.rotation = Quaternion.Euler(0f, 0f, zTilt);
            // Steps visual: small boxes along the ramp are omitted for perf; the ramp is walkable.
        }

        private void BuildFurniture()
        {
            // Living room: sofa, TV with static, poster.
            Box("Sofa", new Vector3(-8.5f, 0.35f, -7.5f), new Vector3(3f, 0.7f, 1f), _matRed);
            Box("TVStand", new Vector3(-8.5f, 0.3f, -9.4f), new Vector3(2.4f, 0.6f, 0.5f), _matWood);
            var tv = Box("TV", new Vector3(-8.5f, 1.25f, -9.45f), new Vector3(1.9f, 1.1f, 0.08f), _matBlack, null, 0);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "TVScreen"; screen.transform.position = new Vector3(-8.5f, 1.25f, -9.39f);
            screen.transform.localScale = new Vector3(1.75f, 0.95f, 1f);
            screen.GetComponent<Renderer>().material = _matStatic; screen.layer = 0;
            AddPoster(new Vector3(-11f, 1.8f, -9.82f), "WHAT THE\nSIGMA?", 0f);
            AddPoster(new Vector3(12.82f, 1.8f, 0f), "HOUSE RULES:\n1. No running.\n2. He is listening.", -90f);
            AddPoster(new Vector3(-8.5f, FloorHeight + 1.8f, 9.82f), "THE FAMILY\nALBUM", 180f);

            // Tables, beds, cabinets per floor.
            AddTable(new Vector3(9f, 0, -6f)); AddTable(new Vector3(-8.5f, FloorHeight, 6f));
            AddBed(new Vector3(-10.5f, FloorHeight, -7.5f)); AddBed(new Vector3(10.5f, FloorHeight, -7.5f)); AddBed(new Vector3(-10.5f, 0, 6.5f));

            // Drawers (one randomly hides the front-door key each run).
            AddDrawer(new Vector3(-12f, FloorHeight, -3f));
            AddDrawer(new Vector3(12f, FloorHeight, 4f));
            AddDrawer(new Vector3(12f, 0, -9f));
            AddDrawer(new Vector3(-12f, -FloorHeight, 8f));

            // Hideable closets.
            AddCloset(new Vector3(-12.2f, 0, 0.5f));
            AddCloset(new Vector3(12.2f, FloorHeight, -1f));
            AddCloset(new Vector3(-12.2f, -FloorHeight, -8f));

            // Lamps (few real lights, for mobile performance).
            AddLamp(new Vector3(-8.5f, 0, -4f)); AddLamp(new Vector3(9f, FloorHeight, -4f)); AddLamp(new Vector3(0f, 0, 8f));

            // Radios (noise distractions).
            AddRadio(new Vector3(9f, 0, 8.5f)); AddRadio(new Vector3(-8.5f, FloorHeight, -8.5f));

            // Weapon pickup: hammer in the basement (faster, quieter finishes).
            var hammer = Box("HammerPickup", new Vector3(8f, -FloorHeight + 0.6f, -8f), new Vector3(0.5f, 0.18f, 0.18f), _matMetal, null, 0);
            RegisterInteract(hammer, InteractKind.PickupHammer, "Take hammer (quieter finishes)");

            // Ancestor note (lore / dark humour, entirely original text).
            var note = Box("Note", new Vector3(0f, 0.55f, 8.6f), new Vector3(0.4f, 0.05f, 0.3f), _matWhite, null, 0);
            var noteInter = RegisterInteract(note, InteractKind.Note, "Read the family note");
            noteInter.noteText = "Family note: 'Tishay — every generation leaves this house quieter than they found it. Don't disappoint the voices. — Grandpa'";

            // Stair pads: the visible ramps are decorative; pads guarantee reliable
            // floor changes on mobile controllers (no physics tunnelling on stairs).
            AddStairPad(new Vector3(7.4f, 0.06f, 6f), new Vector3(11.6f, FloorHeight + 0.06f, 2.2f), "Take the stairs (up)");
            AddStairPad(new Vector3(11.6f, FloorHeight + 0.06f, 2.2f), new Vector3(7.4f, 0.06f, 6f), "Take the stairs (down)");
            AddStairPad(new Vector3(-7.4f, 0.06f, 6f), new Vector3(-11.6f, -FloorHeight + 0.06f, 2.2f), "Take the stairs (basement)");
            AddStairPad(new Vector3(-11.6f, -FloorHeight + 0.06f, 2.2f), new Vector3(-7.4f, 0.06f, 6f), "Take the stairs (up)");
        }

        private void AddStairPad(Vector3 pos, Vector3 destination, string label)
        {
            var pad = Box("StairPad", pos, new Vector3(1.5f, 0.1f, 1.5f), _matMetal, null, 0);
            var inter = RegisterInteract(pad, InteractKind.Stair, label);
            inter.destination = destination; inter.hasDestination = true;
        }

        private Interactable RegisterInteract(GameObject obj, InteractKind kind, string label)
        {
            var inter = new Interactable { kind = kind, root = obj, label = label, closedPos = obj.transform.position };
            _interactables.Add(inter);
            _interactByObject[obj] = inter;
            return inter;
        }

        private void AddPoster(Vector3 pos, string text, float rotY)
        {
            var bg = Box("Poster", pos, new Vector3(1.4f, 0.9f, 0.05f), _matWhite, null, 0);
            bg.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            var canvasGo = new GameObject("PosterText");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.transform.position = pos + bg.transform.forward * 0.04f;
            canvasGo.transform.rotation = bg.transform.rotation;
            canvasGo.transform.localScale = Vector3.one * 0.005f;
            var rt = canvasGo.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(280f, 180f);
            var t = MakeText(canvasGo.transform, text, 30, TextAnchor.MiddleCenter, Color.black);
            Stretch(t.rectTransform);
        }

        private void AddTable(Vector3 pos)
        {
            Box("TableTop", pos + new Vector3(0, 0.75f, 0), new Vector3(2.2f, 0.1f, 1.2f), _matWood);
            Box("TableLeg", pos + new Vector3(0, 0.35f, 0), new Vector3(1.8f, 0.7f, 0.8f), _matWood);
        }

        private void AddBed(Vector3 pos)
        {
            Box("Bed", pos + new Vector3(0, 0.35f, 0), new Vector3(2f, 0.5f, 3f), _matWhite);
            Box("Pillow", pos + new Vector3(0, 0.7f, -1.2f), new Vector3(1.2f, 0.25f, 0.7f), _matWhite, null, 0);
            Box("Blanket", pos + new Vector3(0, 0.62f, 0.4f), new Vector3(2.02f, 0.15f, 1.8f), _matRed, null, 0);
        }

        private void AddDrawer(Vector3 pos)
        {
            var bodyObj = Box("DrawerUnit", pos + new Vector3(0, 0.6f, 0), new Vector3(1.1f, 1.2f, 0.6f), _matWood);
            var drawer = Box("Drawer", pos + new Vector3(0, 0.85f, 0.25f), new Vector3(0.9f, 0.35f, 0.5f), _matDark, null, 0);
            var inter = RegisterInteract(drawer, InteractKind.Drawer, "Open drawer");
            inter.closedPos = drawer.transform.position;
            _interactByObject[bodyObj] = inter;
        }

        private void AddCloset(Vector3 pos)
        {
            float y = pos.y;
            Box("ClosetL", pos + new Vector3(-0.75f, y + 1.2f, 0), new Vector3(0.15f, 2.4f, 1.1f), _matWood);
            Box("ClosetR", pos + new Vector3(0.75f, y + 1.2f, 0), new Vector3(0.15f, 2.4f, 1.1f), _matWood);
            Box("ClosetTop", pos + new Vector3(0, y + 2.45f, 0), new Vector3(1.6f, 0.15f, 1.1f), _matWood);
            Box("ClosetBack", pos + new Vector3(0, y + 1.2f, -0.5f), new Vector3(1.6f, 2.4f, 0.12f), _matWood);
            var doorObj = Box("ClosetDoor", pos + new Vector3(0, y + 1.2f, 0.52f), new Vector3(1.3f, 2.3f, 0.08f), _matWood, null, 0);
            RegisterInteract(doorObj, InteractKind.Closet, "Hide in closet");
        }

        private void AddLamp(Vector3 pos)
        {
            Box("LampPole", pos + new Vector3(0, 0.8f, 0), new Vector3(0.12f, 1.6f, 0.12f), _matMetal, null, 0);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "LampBulb"; bulb.transform.position = pos + new Vector3(0, 1.7f, 0);
            bulb.transform.localScale = Vector3.one * 0.28f; bulb.layer = 0;
            var bulbMat = new Material(_matGlow); bulb.GetComponent<Renderer>().material = bulbMat;
            var lightGo = new GameObject("LampLight");
            lightGo.transform.position = pos + new Vector3(0, 1.7f, 0);
            var li = lightGo.AddComponent<Light>();
            li.type = LightType.Point; li.range = 9f; li.intensity = 1.1f; li.color = new Color(1f, 0.85f, 0.6f);
            var inter = RegisterInteract(bulb, InteractKind.Lamp, "Toggle lamp");
            inter.lampLight = li; inter.lampBulb = bulb.GetComponent<Renderer>(); inter.on = true;
        }

        private void AddRadio(Vector3 pos)
        {
            var radio = Box("Radio", pos + new Vector3(0, 0.55f, 0), new Vector3(0.7f, 0.4f, 0.3f), _matBlack, null, 0);
            Box("RadioBase", pos + new Vector3(0, 0.25f, 0), new Vector3(1f, 0.5f, 0.6f), _matWood);
            var src = radio.AddComponent<AudioSource>();
            src.clip = _clipDrone; src.loop = true; src.spatialBlend = 1f; src.maxDistance = 18f; src.volume = 0.5f; src.pitch = 1.7f;
            var inter = RegisterInteract(radio, InteractKind.Radio, "Toggle radio (distraction)");
            inter.radioAudio = src;
        }

        // ---------------------------------------------------------- waypoints
        private void BuildWaypoints()
        {
            float[] bases = { -FloorHeight, 0f, FloorHeight };
            var indexByKey = new Dictionary<string, int>();
            foreach (float y in bases)
            {
                for (float x = -12f; x <= 12.01f; x += 4f)
                {
                    for (float z = -8f; z <= 8.01f; z += 4f)
                    {
                        var p = new Vector3(x, y, z);
                        if (Physics.CheckSphere(p + Vector3.up * 0.6f, 0.45f, 1 << 8)) continue;
                        if (y > 0.1f && x > 6f && z > 2f) continue; // stairwell hole
                        indexByKey[Key(p)] = _nodes.Count;
                        _nodes.Add(new Node { pos = p });
                    }
                }
            }
            // Same-floor links.
            for (int i = 0; i < _nodes.Count; i++)
            {
                for (int j = 0; j < _nodes.Count; j++)
                {
                    if (i == j) continue;
                    var a = _nodes[i].pos; var b = _nodes[j].pos;
                    if (Mathf.Abs(a.y - b.y) > 0.1f) continue;
                    if (Vector3.Distance(a, b) > 4.6f) continue;
                    if (Physics.Linecast(a + Vector3.up * 1f, b + Vector3.up * 1f, 1 << 8)) continue;
                    _nodes[i].links.Add(j);
                }
            }
            // Stair links (chains over the ramps).
            LinkChain(new[]
            {
                new Vector3(7.2f, 0f, 6f), new Vector3(9f, 1.1f, 6f), new Vector3(10.5f, 2.2f, 6f), new Vector3(11.5f, FloorHeight, 6f)
            }, indexByKey);
            LinkChain(new[]
            {
                new Vector3(-7.2f, 0f, 6f), new Vector3(-9f, -1.1f, 6f), new Vector3(-10.5f, -2.2f, 6f), new Vector3(-11.5f, -FloorHeight, 6f)
            }, indexByKey);
        }

        private static string Key(Vector3 p) { return Mathf.RoundToInt(p.x) + "," + Mathf.RoundToInt(p.y * 10f) + "," + Mathf.RoundToInt(p.z); }

        private void LinkChain(Vector3[] points, Dictionary<string, int> indexByKey)
        {
            int prev = -1;
            foreach (var p in points)
            {
                int idx;
                if (!indexByKey.TryGetValue(Key(p), out idx))
                {
                    idx = _nodes.Count;
                    _nodes.Add(new Node { pos = p });
                    indexByKey[Key(p)] = idx;
                    // Connect to nearest same-area nodes.
                    for (int j = 0; j < _nodes.Count - 1; j++)
                    {
                        if (Vector3.Distance(_nodes[j].pos, p) < 5.5f && Mathf.Abs(_nodes[j].pos.y - p.y) < 1.4f)
                        { _nodes[idx].links.Add(j); _nodes[j].links.Add(idx); }
                    }
                }
                if (prev >= 0) { _nodes[prev].links.Add(idx); _nodes[idx].links.Add(prev); }
                prev = idx;
            }
        }

        private int NearestNode(Vector3 pos)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float d = Vector3.Distance(_nodes[i].pos, pos);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            int start = NearestNode(from), goal = NearestNode(to);
            var result = new List<Vector3>();
            if (start < 0 || goal < 0) { result.Add(to); return result; }
            var prev = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start); visited.Add(start); prev[start] = -1;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == goal) break;
                foreach (int nxt in _nodes[cur].links)
                    if (!visited.Contains(nxt)) { visited.Add(nxt); prev[nxt] = cur; queue.Enqueue(nxt); }
            }
            if (!visited.Contains(goal)) { result.Add(to); return result; }
            var stack = new List<int>();
            int c = goal;
            while (c != -1) { stack.Add(c); c = prev.ContainsKey(c) ? prev[c] : -1; }
            stack.Reverse();
            foreach (int i in stack) result.Add(_nodes[i].pos);
            result.Add(to);
            return result;
        }

        // ----------------------------------------------------------- actors
        private void CreatePlayer()
        {
            _playerObj = new GameObject("TishayDangare_Player");
            _cc = _playerObj.AddComponent<CharacterController>();
            _cc.height = 1.8f; _cc.radius = 0.35f; _cc.center = new Vector3(0, 0.9f, 0); _cc.stepOffset = 0.55f; _cc.slopeLimit = 58f;
            _playerObj.transform.position = new Vector3(0f, 0.05f, 7f);
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(_playerObj.transform, false);
            camGo.transform.localPosition = new Vector3(0, _camHeight, 0);
            _cam = camGo.AddComponent<Camera>();
            _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 70f; _cam.fieldOfView = 72f;
            _camT = camGo.transform;
            camGo.AddComponent<AudioListener>();
            _flashlightObj = new GameObject("Flashlight");
            _flashlightObj.transform.SetParent(camGo.transform, false);
            _flashlightObj.transform.localPosition = new Vector3(0.18f, -0.12f, 0.05f);
            var fl = _flashlightObj.AddComponent<Light>();
            fl.type = LightType.Spot; fl.range = 16f; fl.spotAngle = 52f; fl.intensity = 1.4f; fl.color = new Color(1f, 0.95f, 0.85f);
        }

        private void CreateHunter()
        {
            _hunterObj = new GameObject("Vikram_TheShadowRao_Hunter");
            _hunterT = _hunterObj.transform;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "HunterBody"; body.transform.SetParent(_hunterT, false);
            body.transform.localPosition = new Vector3(0, 1f, 0); body.transform.localScale = new Vector3(1.05f, 1.05f, 1.05f);
            body.GetComponent<Renderer>().material = _matBlack;
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "HunterHead"; head.transform.SetParent(_hunterT, false);
            head.transform.localPosition = new Vector3(0, 2.02f, 0); head.transform.localScale = Vector3.one * 0.42f;
            head.GetComponent<Renderer>().material = _matWhite;
            for (int i = -1; i <= 1; i += 2)
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye"; eye.transform.SetParent(_hunterT, false);
                eye.transform.localPosition = new Vector3(0.09f * i, 2.06f, 0.18f); eye.transform.localScale = Vector3.one * 0.07f;
                var em = new Material(_matGlow); em.SetColor("_EmissionColor", Color.red * 2.2f);
                eye.GetComponent<Renderer>().material = em;
            }
            var lightGo = new GameObject("HunterTorch");
            lightGo.transform.SetParent(_hunterT, false); lightGo.transform.localPosition = new Vector3(0.3f, 1.6f, 0.1f);
            _hunterLight = lightGo.AddComponent<Light>();
            _hunterLight.type = LightType.Spot; _hunterLight.range = 15f; _hunterLight.spotAngle = 46f; _hunterLight.intensity = 1.6f;
            _hunterSfx = _hunterObj.AddComponent<AudioSource>();
            _hunterSfx.spatialBlend = 1f; _hunterSfx.maxDistance = 25f; _hunterSfx.playOnAwake = false;
            _hunterObj.transform.position = new Vector3(-9f, FloorHeight, -6f);
            _hunterState = HunterState.Patrol;
            _hunterTargetPos = _hunterObj.transform.position;
        }

        private void CreateTargets()
        {
            string[] names = { "Flatmate Arjun", "Neighbour Meera", "Tutor Kabir", "Guest Sana", "Caretaker Dev" };
            for (int i = 0; i < _targetSpawns.Count; i++)
            {
                var root = new GameObject("Target_" + names[i]);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body"; body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0, 0.95f, 0);
                body.GetComponent<Renderer>().material = i % 2 == 0 ? _matShirt : _matRed;
                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head"; head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0, 1.92f, 0); head.transform.localScale = Vector3.one * 0.4f;
                head.GetComponent<Renderer>().material = _matSkin;
                root.transform.position = _targetSpawns[i] + Vector3.up * 0.05f;
                _targets.Add(new TargetData { root = root, body = body, alive = true, home = root.transform.position });
            }
        }

        // ---------------------------------------------------------------- UI
        private Font GetFont()
        {
            if (_font != null) return _font;
            try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; }
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
            }
            return _font;
        }

        private GameObject MakePanel(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = colour;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return go;
        }

        private void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private Text MakeText(Transform parent, string text, int size, TextAnchor anchor, Color colour)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = GetFont(); t.text = text; t.fontSize = size; t.alignment = anchor; t.color = colour;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(12, 8); rt.offsetMax = new Vector2(-12, -8);
            return t;
        }

        private Text MakeAnchoredText(Transform parent, string text, int size, TextAnchor anchor, Color colour,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var t = MakeText(parent, text, size, anchor, colour);
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return t;
        }

        private Button MakeButton(Transform parent, string label, Vector2 centre, Vector2 size, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0.14f, 0.02f, 0.03f, 0.95f);
            var btn = go.AddComponent<Button>();
            var cb = btn.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.4f, 1.1f, 1.1f); btn.colors = cb;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre; rt.sizeDelta = size;
            var t = MakeText(go.transform, label, 24, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.85f));
            Stretch(t.rectTransform);
            btn.onClick.AddListener(() => { PlaySfx(_clipClick, 0.7f); if (onClick != null) onClick(); });
            return btn;
        }

        private Button MakeHudButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Action onClick)
        {
            var go = new GameObject("Touch_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.45f);
            var btn = go.AddComponent<Button>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(78f, 62f);
            var t = MakeText(go.transform, label, 16, TextAnchor.MiddleCenter, Color.white);
            Stretch(t.rectTransform);
            btn.onClick.AddListener(() => { if (onClick != null) onClick(); });
            return btn;
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("KILLER_Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasGo.AddComponent<GraphicRaycaster>();

            var es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            Transform root = canvasGo.transform;
            Color bg = new Color(0.01f, 0.01f, 0.02f, 0.97f);

            // Splash
            _splashPanel = MakePanel(root, "Splash", bg);
            var splashTitle = MakeAnchoredText(_splashPanel.transform, "KILLER", 92, TextAnchor.MiddleCenter, new Color(0.85f, 0.05f, 0.08f),
                new Vector2(0, 0.45f), new Vector2(1, 0.85f), Vector2.zero, Vector2.zero);
            splashTitle.fontStyle = FontStyle.Bold;
            _splashSub = MakeAnchoredText(_splashPanel.transform, CreatorCredit + "\nVersion " + GameVersion, 26, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0, 0.25f), new Vector2(1, 0.45f), Vector2.zero, Vector2.zero);

            // Offline
            _offlinePanel = MakePanel(root, "Offline", bg);
            MakeAnchoredText(_offlinePanel.transform, "NO INTERNET CONNECTION", 44, TextAnchor.MiddleCenter, new Color(1f, 0.4f, 0.4f),
                new Vector2(0, 0.55f), new Vector2(1, 0.8f), Vector2.zero, Vector2.zero);
            _offlineStatus = MakeAnchoredText(_offlinePanel.transform, "", 22, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0, 0.35f), new Vector2(1, 0.56f), Vector2.zero, Vector2.zero);
            MakeButton(_offlinePanel.transform, "RETRY", new Vector2(0, -110f), new Vector2(300f, 64f), RetryOnline);

            // Main menu
            _menuPanel = MakePanel(root, "MainMenu", new Color(0.01f, 0.01f, 0.02f, 0.88f));
            var title = MakeAnchoredText(_menuPanel.transform, "KILLER", 84, TextAnchor.MiddleCenter, new Color(0.85f, 0.05f, 0.08f),
                new Vector2(0, 0.68f), new Vector2(1, 0.97f), Vector2.zero, Vector2.zero);
            title.fontStyle = FontStyle.Bold;
            MakeAnchoredText(_menuPanel.transform, CreatorCredit + "  •  Version " + GameVersion + "\nYou are Tishay Dangare. Finish the family ritual. Don't let The Shadow catch you.",
                19, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.9f), new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.68f), Vector2.zero, Vector2.zero);
            MakeButton(_menuPanel.transform, "NEW GAME", new Vector2(0, 55f), new Vector2(340f, 62f), () => StartRun(1, true));
            MakeButton(_menuPanel.transform, "CONTINUE  (Day " + Mathf.Max(1, _save.day) + ")", new Vector2(0, -20f), new Vector2(340f, 62f), () => StartRun(Mathf.Max(1, _save.day), false));
            MakeButton(_menuPanel.transform, "SETTINGS", new Vector2(0, -95f), new Vector2(340f, 62f), () => ShowPanel(_settingsPanel));
            MakeButton(_menuPanel.transform, "CREDITS", new Vector2(0, -170f), new Vector2(340f, 62f), () => ShowPanel(_creditsPanel));
            MakeButton(_menuPanel.transform, "QUIT", new Vector2(0, -245f), new Vector2(340f, 62f), () => Application.Quit());
            _menuStats = MakeAnchoredText(_menuPanel.transform, "", 17, TextAnchor.MiddleCenter, new Color(0.6f, 0.62f, 0.7f),
                new Vector2(0.1f, 0.01f), new Vector2(0.9f, 0.10f), Vector2.zero, Vector2.zero);

            // Settings
            _settingsPanel = MakePanel(root, "Settings", bg);
            MakeAnchoredText(_settingsPanel.transform, "SETTINGS", 46, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0, 0.72f), new Vector2(1, 0.92f), Vector2.zero, Vector2.zero);
            _settingsLabel = MakeAnchoredText(_settingsPanel.transform, "", 20, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.1f, 0.55f), new Vector2(0.9f, 0.72f), Vector2.zero, Vector2.zero);
            _volumeSlider = MakeSlider(_settingsPanel.transform, new Vector2(0, 55f), 0f, 1f, _save.settings.volume,
                v => { _save.settings.volume = v; ApplySettings(true); });
            _sensSlider = MakeSlider(_settingsPanel.transform, new Vector2(0, -15f), 0.4f, 5f, _save.settings.sensitivity,
                v => { _save.settings.sensitivity = v; ApplySettings(true); });
            MakeButton(_settingsPanel.transform, "CYCLE QUALITY", new Vector2(0, -95f), new Vector2(320f, 58f),
                () => { _save.settings.quality = (_save.settings.quality + 1) % 3; ApplySettings(true); });
            MakeButton(_settingsPanel.transform, "BACK", new Vector2(0, -180f), new Vector2(320f, 58f),
                () => { if (_state == GameState.Paused) ShowPanel(_pausePanel); else ShowMainMenu(); });

            // Credits
            _creditsPanel = MakePanel(root, "Credits", bg);
            MakeAnchoredText(_creditsPanel.transform,
                "KILLER — Version " + GameVersion + "\n" + CreatorCredit + "\n\n" +
                "Protagonist: Tishay Dangare, 19, Indian, male.\n" +
                "Antagonist: Vikram \"The Shadow\" Rao — the investigator who has hunted the family for generations.\n\n" +
                "All art, audio and code in this game are original or procedurally generated.\n" +
                "KILLER is an original work. Any resemblance to other games is coincidental.\n" +
                "Contains stylised, cartoonish violence and dark humour. Fictional characters only.",
                20, TextAnchor.MiddleCenter, Color.white, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.9f), Vector2.zero, Vector2.zero);
            MakeButton(_creditsPanel.transform, "BACK", new Vector2(0, -250f), new Vector2(300f, 58f), ShowMainMenu);

            // HUD
            _hudPanel = MakePanel(root, "HUD", new Color(0, 0, 0, 0));
            _hudPanel.GetComponent<Image>().raycastTarget = false;
            _hudDay = MakeAnchoredText(_hudPanel.transform, "", 20, TextAnchor.UpperLeft, Color.white,
                new Vector2(0, 0.88f), new Vector2(0.4f, 1f), new Vector2(14, -8), Vector2.zero);
            _hudObjective = MakeAnchoredText(_hudPanel.transform, "", 20, TextAnchor.UpperRight, new Color(1f, 0.8f, 0.8f),
                new Vector2(0.55f, 0.88f), new Vector2(1, 1f), Vector2.zero, new Vector2(-14, -8));
            _hudPrompt = MakeAnchoredText(_hudPanel.transform, "", 22, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.2f, 0.14f), new Vector2(0.8f, 0.24f), Vector2.zero, Vector2.zero);
            _subtitle = MakeAnchoredText(_hudPanel.transform, "", 21, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.65f),
                new Vector2(0.12f, 0.26f), new Vector2(0.88f, 0.36f), Vector2.zero, Vector2.zero);
            MakeAnchoredText(_hudPanel.transform, "STAMINA", 13, TextAnchor.LowerLeft, Color.white,
                new Vector2(0, 0.10f), new Vector2(0.25f, 0.15f), new Vector2(14, 0), Vector2.zero);
            _staminaFill = MakeBar(_hudPanel.transform, new Vector2(0.015f, 0.055f), new Vector2(0.24f, 0.095f), new Color(0.3f, 0.85f, 0.4f));
            MakeAnchoredText(_hudPanel.transform, "HUNTER ALERT", 13, TextAnchor.LowerLeft, new Color(1f, 0.65f, 0.65f),
                new Vector2(0, 0.035f), new Vector2(0.25f, 0.06f), new Vector2(14, 0), Vector2.zero);
            _noiseFill = MakeBar(_hudPanel.transform, new Vector2(0.015f, 0.005f), new Vector2(0.24f, 0.03f), new Color(0.9f, 0.15f, 0.15f));
            // QTE bar
            var qteBg = MakePanel(_hudPanel.transform, "QTE", new Color(0, 0, 0, 0.65f));
            var qteRt = qteBg.GetComponent<RectTransform>();
            qteRt.anchorMin = new Vector2(0.32f, 0.38f); qteRt.anchorMax = new Vector2(0.68f, 0.44f); qteRt.offsetMin = Vector2.zero; qteRt.offsetMax = Vector2.zero;
            _qteLabel = MakeText(qteBg.transform, "TAP ATTACK IN THE RED ZONE!", 19, TextAnchor.MiddleCenter, Color.white);
            Stretch(_qteLabel.rectTransform);
            _qteMarker = MakePanel(qteBg.transform, "QTEMarker", Color.white).GetComponent<Image>();
            var mRt = _qteMarker.rectTransform;
            mRt.anchorMin = new Vector2(0, 0); mRt.anchorMax = new Vector2(0.02f, 1); mRt.offsetMin = Vector2.zero; mRt.offsetMax = Vector2.zero;
            qteBg.SetActive(false); _hudPanel.transform.Find("QTE").gameObject.SetActive(false);
            _qteLabel.transform.parent.gameObject.SetActive(false);

            // Vignette (procedural-feel overlay; also used for the scare flash base)
            var vigGo = new GameObject("Vignette");
            vigGo.transform.SetParent(_hudPanel.transform, false);
            _vignette = vigGo.AddComponent<Image>(); _vignette.color = new Color(0, 0, 0, 0.34f); _vignette.raycastTarget = false;
            Stretch(vigGo.GetComponent<RectTransform>());

            // Touch controls (shown automatically on touch devices)
            _touchPanel = MakePanel(_hudPanel.transform, "Touch", new Color(0, 0, 0, 0));
            _touchPanel.GetComponent<Image>().raycastTarget = false;
            MakeHudButton(_touchPanel.transform, "USE", new Vector2(1, 0), new Vector2(-92, 188), () => { _interactQueued = true; });
            MakeHudButton(_touchPanel.transform, "ATTACK", new Vector2(1, 0), new Vector2(-92, 108), () => { _attackQueued = true; });
            MakeHudButton(_touchPanel.transform, "RUN", new Vector2(1, 0), new Vector2(-182, 108), () => { _touchRun = !_touchRun; });
            MakeHudButton(_touchPanel.transform, "CROUCH", new Vector2(1, 0), new Vector2(-182, 188), () => { _touchCrouch = !_touchCrouch; });
            MakeHudButton(_touchPanel.transform, "II", new Vector2(1, 1), new Vector2(-56, -52), TogglePause);
            MakeHudButton(_touchPanel.transform, "LIGHT", new Vector2(1, 0), new Vector2(-92, 268), () => { if (_flashlightObj != null) _flashlightObj.SetActive(!_flashlightObj.activeSelf); });
            _touchPanel.SetActive(Input.touchSupported || Application.isMobilePlatform);

            // Pause
            _pausePanel = MakePanel(root, "Pause", new Color(0, 0, 0, 0.82f));
            MakeAnchoredText(_pausePanel.transform, "PAUSED", 52, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0, 0.62f), new Vector2(1, 0.85f), Vector2.zero, Vector2.zero);
            MakeButton(_pausePanel.transform, "RESUME", new Vector2(0, 70f), new Vector2(320f, 60f), TogglePause);
            MakeButton(_pausePanel.transform, "SETTINGS", new Vector2(0, -5f), new Vector2(320f, 60f), () => ShowPanel(_settingsPanel));
            MakeButton(_pausePanel.transform, "MAIN MENU", new Vector2(0, -80f), new Vector2(320f, 60f),
                () => { Time.timeScale = 1f; ShowInterstitial("pause-to-menu", ShowMainMenu); });

            // Game over
            _overPanel = MakePanel(root, "GameOver", new Color(0.06f, 0, 0, 0.94f));
            MakeAnchoredText(_overPanel.transform, "YOU DIED", 68, TextAnchor.MiddleCenter, new Color(0.9f, 0.08f, 0.1f),
                new Vector2(0, 0.60f), new Vector2(1, 0.86f), Vector2.zero, Vector2.zero);
            _overText = MakeAnchoredText(_overPanel.transform, "", 21, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.60f), Vector2.zero, Vector2.zero);
            MakeButton(_overPanel.transform, "WATCH AD TO REVIVE (once per session)", new Vector2(0, 45f), new Vector2(460f, 60f), TryRevive);
            MakeButton(_overPanel.transform, "RETRY", new Vector2(0, -30f), new Vector2(320f, 60f),
                () => ShowInterstitial("death-retry", () => StartRun(Mathf.Max(1, _save.day), false)));
            MakeButton(_overPanel.transform, "MAIN MENU", new Vector2(0, -105f), new Vector2(320f, 60f),
                () => ShowInterstitial("death-to-menu", ShowMainMenu));

            // Victory
            _winPanel = MakePanel(root, "Victory", new Color(0, 0.05f, 0.02f, 0.94f));
            MakeAnchoredText(_winPanel.transform, "ESCAPED", 64, TextAnchor.MiddleCenter, new Color(0.45f, 1f, 0.55f),
                new Vector2(0, 0.60f), new Vector2(1, 0.86f), Vector2.zero, Vector2.zero);
            _winText = MakeAnchoredText(_winPanel.transform, "", 21, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.1f, 0.42f), new Vector2(0.9f, 0.60f), Vector2.zero, Vector2.zero);
            MakeButton(_winPanel.transform, "NEXT DAY", new Vector2(0, 10f), new Vector2(320f, 60f),
                () => ShowInterstitial("victory-next-day", () => StartRun(_save.day, false)));
            MakeButton(_winPanel.transform, "MAIN MENU", new Vector2(0, -65f), new Vector2(320f, 60f),
                () => ShowInterstitial("victory-to-menu", ShowMainMenu));

            // Simulated / SDK ad overlay
            _adPanel = MakePanel(root, "Ad", new Color(0, 0, 0, 0.96f));
            _adPanel.GetComponent<RectTransform>().SetAsLastSibling();
            _adText = MakeAnchoredText(_adPanel.transform, "", 30, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.7f), Vector2.zero, Vector2.zero);

            // Jump-scare overlay: a stark white face shape drawn with UI text art.
            _scareOverlay = MakePanel(root, "Scare", new Color(0, 0, 0, 0.88f));
            MakeAnchoredText(_scareOverlay.transform, "◉        ◉\n\n        △\n   ‾‾‾‾‾‾‾", 72, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.2f, 0.25f), new Vector2(0.8f, 0.75f), Vector2.zero, Vector2.zero);
            _scareOverlay.SetActive(false);

            ShowPanel(_splashPanel);
        }

        private Slider MakeSlider(Transform parent, Vector2 centre, float min, float max, float value, Action<float> onChange)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre; rt.sizeDelta = new Vector2(420f, 34f);
            var slider = go.AddComponent<Slider>();
            var selfImg = go.AddComponent<Image>(); selfImg.color = new Color(0, 0, 0, 0.01f);
            var bg = new GameObject("BG"); bg.transform.SetParent(go.transform, false);
            var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.25f, 0.25f, 0.3f);
            Stretch(bg.GetComponent<RectTransform>());
            var fillArea = new GameObject("Fill"); fillArea.transform.SetParent(go.transform, false);
            Stretch(fillArea.GetComponent<RectTransform>());
            var fill = new GameObject("FillImg"); fill.transform.SetParent(fillArea.transform, false);
            var fillImg = fill.AddComponent<Image>(); fillImg.color = new Color(0.8f, 0.15f, 0.2f);
            Stretch(fill.GetComponent<RectTransform>());
            var handleArea = new GameObject("HandleArea"); handleArea.transform.SetParent(go.transform, false);
            Stretch(handleArea.GetComponent<RectTransform>());
            var handle = new GameObject("Handle"); handle.transform.SetParent(handleArea.transform, false);
            var hImg = handle.AddComponent<Image>(); hImg.color = Color.white;
            var hRt = handle.GetComponent<RectTransform>(); hRt.sizeDelta = new Vector2(26f, 26f);
            slider.fillRect = fillImg.rectTransform; slider.handleRect = hRt; slider.targetGraphic = hImg;
            slider.minValue = min; slider.maxValue = max; slider.value = value;
            slider.onValueChanged.AddListener(v => { if (onChange != null) onChange(v); });
            return slider;
        }

        private Image MakeBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color colour)
        {
            var bgGo = new GameObject("BarBG");
            bgGo.transform.SetParent(parent, false);
            var bgImg = bgGo.AddComponent<Image>(); bgImg.color = new Color(0, 0, 0, 0.55f); bgImg.raycastTarget = false;
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = anchorMin; bgRt.anchorMax = anchorMax; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var fillGo = new GameObject("BarFill");
            fillGo.transform.SetParent(bgGo.transform, false);
            var fill = fillGo.AddComponent<Image>(); fill.color = colour; fill.raycastTarget = false;
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 1f;
            Stretch(fillGo.GetComponent<RectTransform>());
            return fill;
        }

        private void ShowPanel(GameObject panel)
        {
            GameObject[] all = { _splashPanel, _menuPanel, _hudPanel, _pausePanel, _overPanel, _winPanel, _offlinePanel, _settingsPanel, _creditsPanel };
            foreach (var p in all) if (p != null) p.SetActive(p == panel);
            if (_adPanel != null && !_showingAd) _adPanel.SetActive(false);
            if (panel == _hudPanel) LockCursor(true); else LockCursor(false);
        }

        private void RestorePanelForState()
        {
            switch (_state)
            {
                case GameState.MainMenu: ShowPanel(_menuPanel); break;
                case GameState.Playing: ShowPanel(_hudPanel); break;
                case GameState.Paused: ShowPanel(_pausePanel); break;
                case GameState.GameOver: ShowPanel(_overPanel); break;
                case GameState.Victory: ShowPanel(_winPanel); break;
                case GameState.NoInternet: ShowPanel(_offlinePanel); break;
                default: ShowPanel(_splashPanel); break;
            }
        }

        private void LockCursor(bool locked)
        {
            if (Application.isMobilePlatform) return;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void ShowMainMenu()
        {
            _state = GameState.MainMenu;
            Time.timeScale = 1f;
            if (_menuStats != null)
                _menuStats.text = "Day " + Mathf.Max(1, _save.day) + "  •  Total eliminations: " + _save.totalKills +
                                  "  •  Deaths: " + _save.stats.deaths + "  •  Escapes: " + _save.stats.escapes +
                                  "  •  Saved locally only";
            if (_menuPanel != null)
            {
                // Refresh the Continue label is intentionally static (shows saved day at boot);
                // Continue always loads the current saved day at click time.
            }
            ShowPanel(_menuPanel);
        }

        private void ShowSubtitle(string text, float seconds = 3f)
        {
            if (_subtitle != null) _subtitle.text = text;
            _subtitleTimer = seconds;
        }

        // ----------------------------------------------------------- run flow
        private void StartRun(int day, bool freshGame)
        {
            if (freshGame)
            {
                _save.day = 1; _save.totalKills = 0; _save.hammerUnlocked = false;
                day = 1;
            }
            _save.day = Mathf.Max(1, day);
            _killsThisRun = 0; _hasFrontKey = false; _playTimer = 0f;
            EndQte();
            _hidden = false; _exhausted = false; _stamina = 100f;
            Time.timeScale = 1f;

            // Reset doors / drawers / pickups / traps.
            foreach (var d in _doors) { d.open = false; d.openAmount = 0f; }
            foreach (var inter in _interactables)
            {
                inter.used = false; inter.containsKey = false;
                if (inter.kind == InteractKind.Drawer && inter.root != null) inter.root.transform.position = inter.closedPos;
                if ((inter.kind == InteractKind.PickupHammer) && inter.root != null) inter.root.SetActive(!_save.hammerUnlocked);
                if (inter.kind == InteractKind.Lamp) SetLamp(inter, true);
                if (inter.kind == InteractKind.Radio && inter.radioAudio != null) { inter.radioAudio.Stop(); inter.on = false; }
            }
            // Hide the front-door key in a random drawer.
            var drawers = _interactables.FindAll(i => i.kind == InteractKind.Drawer);
            if (drawers.Count > 0) drawers[UnityEngine.Random.Range(0, drawers.Count)].containsKey = true;
            foreach (var t in _traps) if (t.root != null) Destroy(t.root);
            _traps.Clear();

            // Player to spawn.
            _cc.enabled = false;
            _playerObj.transform.position = new Vector3(0f, 0.05f, 7f);
            _playerObj.transform.rotation = Quaternion.identity;
            _yaw = 180f; _pitch = 0f;
            _cc.enabled = true;

            // Hunter far away, difficulty scales with the day.
            _hunterObj.transform.position = new Vector3(-9f, FloorHeight, -6f);
            _hunterState = HunterState.Patrol; _hunterPath = null; _seenMeter = 0f; _memoryTimer = 0f; _trapTimer = 40f;

            // Targets alive again.
            foreach (var t in _targets)
            {
                t.alive = true; t.root.SetActive(true);
                t.root.transform.position = t.home; t.root.transform.rotation = Quaternion.identity;
                t.body.transform.localRotation = Quaternion.identity;
                t.path = null; t.waitTimer = UnityEngine.Random.Range(0f, 2f); t.fleeing = false; t.fallAmount = 0f;
            }
            _targetsRemaining = _targets.Count;

            _state = GameState.Playing;
            ShowPanel(_hudPanel);
            UpdateHud();
            ShowSubtitle(_save.day == 1
                ? "Day 1. The voices are awake. Eliminate everyone, take the front-door key, and get out."
                : "Day " + _save.day + ". The Shadow has been studying you. He is faster now.", 4.5f);
            WriteSave();
        }

        private void UpdateHud()
        {
            if (_hudDay != null) _hudDay.text = "DAY " + _save.day + (_hidden ? "\n[HIDDEN]" : "") + (_crouching ? "\n[crouched]" : "");
            if (_hudObjective != null)
                _hudObjective.text = "Targets left: " + _targetsRemaining + " / " + _targets.Count +
                                     (_hasFrontKey ? "\nFront-door key: YES — escape!" : "\nFront-door key: not found");
            if (_staminaFill != null) _staminaFill.fillAmount = _stamina / 100f;
            if (_noiseFill != null) _noiseFill.fillAmount = Mathf.Clamp01(_seenMeter);
        }

        private void Die()
        {
            if (_state != GameState.Playing) return;
            EndQte();
            _state = GameState.GameOver;
            _save.stats.deaths++;
            _save.stats.playTimeSeconds += _playTimer;
            WriteSave();
            PlaySfx(_clipStinger, 1f); PlaySfx(_clipScream, 0.8f);
            TriggerScare(0.6f);
            if (_overText != null)
                _overText.text = "The Shadow caught you on Day " + _save.day + ".\nEliminations this run: " + _killsThisRun +
                                 (_reviveUsedThisSession ? "\n(Revive already used this session.)" : "\nYou may revive once per session with a rewarded ad.");
            ShowPanel(_overPanel);
        }

        private void Win()
        {
            if (_state != GameState.Playing) return;
            _state = GameState.Victory;
            _save.stats.escapes++;
            _save.stats.playTimeSeconds += _playTimer;
            _save.day = Mathf.Max(1, _save.day) + 1;
            WriteSave();
            if (_winText != null)
                _winText.text = "You eliminated all targets and escaped.\nThe voices are satisfied... for tonight.\nNext: Day " + _save.day + " — The Shadow learns your favourite rooms.";
            ShowPanel(_winPanel);
        }

        private void TryRevive()
        {
            if (_reviveUsedThisSession)
            {
                if (_overText != null) _overText.text = "Revive already used this session. Retry or return to the menu.";
                return;
            }
            ShowRewarded(() =>
            {
                _reviveUsedThisSession = true;
                _cc.enabled = false;
                _playerObj.transform.position = new Vector3(0f, 0.05f, 7f);
                _cc.enabled = true;
                _hunterObj.transform.position = new Vector3(-9f, FloorHeight, -6f);
                _hunterState = HunterState.Patrol; _hunterPath = null; _seenMeter = 0f;
                _state = GameState.Playing;
                ShowPanel(_hudPanel);
                ShowSubtitle("Second chance. Don't waste it.", 3f);
            });
        }

        private void TogglePause()
        {
            if (_state == GameState.Playing)
            {
                _state = GameState.Paused; Time.timeScale = 0f; ShowPanel(_pausePanel);
            }
            else if (_state == GameState.Paused)
            {
                _state = GameState.Playing; Time.timeScale = 1f; ShowPanel(_hudPanel);
            }
        }

        private void TriggerScare(float seconds)
        {
            if (_scareOverlay != null) _scareOverlay.SetActive(true);
            _scareTimer = seconds;
            PlaySfx(_clipStinger, 1f);
        }

        // ------------------------------------------------------------ update
        private void Update()
        {
            float dt = Time.deltaTime;

            if (_subtitleTimer > 0f) { _subtitleTimer -= dt; if (_subtitleTimer <= 0f && _subtitle != null) _subtitle.text = ""; }
            if (_scareTimer > 0f) { _scareTimer -= dt; if (_scareTimer <= 0f && _scareOverlay != null) _scareOverlay.SetActive(false); }
            if (Time.frameCount % 6 == 0) RefreshStaticTexture();
            UpdateDoors(dt);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                if (_state == GameState.Playing || _state == GameState.Paused) TogglePause();
            }

            if (_state != GameState.Playing)
            {
                if (_music != null) _music.volume = Mathf.Lerp(_music.volume, 0f, dt * 3f);
                return;
            }

            _playTimer += dt;
            HandleTouchInput();
            UpdatePlayer(dt);
            UpdateQte(dt);
            UpdateHunter(dt);
            UpdateTargets(dt);
            UpdateTraps(dt);
            UpdateInteractPrompt();
            UpdateHud();

            // Hunter background audio + chase music intensity.
            bool chase = _hunterState == HunterState.Chase;
            if (_music != null) _music.volume = Mathf.Lerp(_music.volume, chase ? 0.5f : 0.10f, dt * 2f);
            if (_vignette != null)
                _vignette.color = new Color(0, 0, 0, chase ? 0.45f : 0.30f + 0.05f * Mathf.Sin(Time.time * 1.7f));

            // Radio distractions periodically attract the Hunter.
            foreach (var inter in _interactables)
                if (inter.kind == InteractKind.Radio && inter.on && inter.radioAudio != null && inter.radioAudio.isPlaying)
                    EmitNoise(inter.root.transform.position, 11f, true);

            // Day stats are written on death/victory; also flush every 30 s.
            if (_playTimer > 30f && Mathf.FloorToInt(_playTimer) % 30 == 0 && Time.frameCount % 300 == 0)
            { _save.stats.playTimeSeconds += 0; }
        }

        private void UpdatePlayer(float dt)
        {
            if (_cc == null) return;

            // Look: mouse or right-side touch drag.
            float lookX = 0f, lookY = 0f;
            if (!Application.isMobilePlatform)
            {
                lookX = Input.GetAxis("Mouse X");
                lookY = Input.GetAxis("Mouse Y");
            }
            else if (_lookTouchId >= 0)
            {
                // handled in HandleTouchInput via _touchLookDelta
                lookX = _touchLookDelta.x; lookY = _touchLookDelta.y;
                _touchLookDelta = Vector2.zero;
            }
            float sens = _save.settings.sensitivity;
            _yaw += lookX * sens;
            _pitch = Mathf.Clamp(_pitch - lookY * sens, -78f, 78f);
            _playerObj.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // Lean (Q / E on keyboard; E is also interact — lean uses Q/E only when holding Alt-free,
            // interact is F or the USE button to avoid conflict on desktop).
            float leanTarget = 0f;
            if (Input.GetKey(KeyCode.Q)) leanTarget = -0.38f;
            if (Input.GetKey(KeyCode.E)) leanTarget = 0.38f;
            _leanAmount = Mathf.Lerp(_leanAmount, leanTarget, dt * 8f);

            if (_hidden)
            {
                _camT.localPosition = new Vector3(_leanAmount, _camHeight - 0.55f, 0.15f);
                _camT.localRotation = Quaternion.Euler(_pitch * 0.4f, 0f, -_leanAmount * 18f);
                if (_interactQueued || Input.GetKeyDown(KeyCode.F)) { _interactQueued = false; ExitHide(); }
                return;
            }

            _crouching = _touchCrouch || Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.LeftControl);
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");
            if (_moveTouchId >= 0)
            {
                Vector2 mv = (_moveTouchNow - _moveTouchStart) / 60f;
                inputX = Mathf.Clamp(mv.x, -1f, 1f);
                inputZ = Mathf.Clamp(-mv.y, -1f, 1f);
            }
            bool wantsRun = _touchRun || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool moving = Mathf.Abs(inputX) > 0.05f || Mathf.Abs(inputZ) > 0.05f;
            bool running = wantsRun && moving && !_crouching && !_exhausted;
            if (running)
            {
                _stamina -= 26f * dt;
                if (_stamina <= 0f) { _stamina = 0f; _exhausted = true; }
            }
            else
            {
                _stamina += (_crouching ? 20f : 15f) * dt;
                if (_stamina > 35f) _exhausted = false;
                if (_stamina > 100f) _stamina = 100f;
            }
            float speed = _crouching ? CrouchSpeed : (running ? RunSpeed : WalkSpeed);
            if (_slowTimer > 0f) { _slowTimer -= dt; speed *= 0.45f; }

            Vector3 wish = (_playerObj.transform.right * inputX + _playerObj.transform.forward * inputZ);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            Vector3 vel = wish * speed;
            vel.y = -9.8f;
            _cc.Move(vel * dt);

            // Camera height, head-bob.
            float targetH = _crouching ? 1.05f : _camHeight;
            if (moving) _headBobPhase += dt * (running ? 11f : 7f);
            float bob = moving ? Mathf.Sin(_headBobPhase) * (running ? 0.055f : 0.035f) : 0f;
            _camT.localPosition = new Vector3(_leanAmount, targetH + bob, 0f);
            _camT.localRotation = Quaternion.Euler(_pitch, 0f, -_leanAmount * 16f);
            _cc.height = _crouching ? 1.15f : 1.8f;
            _cc.center = new Vector3(0, _cc.height / 2f, 0);

            // Footstep noise.
            if (moving && _cc.isGrounded)
            {
                _stepTimer -= dt * (running ? 1.9f : 1f);
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 0.42f;
                    PlaySfx(_clipStep, _crouching ? 0.15f : 0.5f);
                    float radius = _crouching ? 0f : (running ? 9f : 4.5f);
                    if (radius > 0f) EmitNoise(_playerObj.transform.position, radius, false);
                }
                // Hunter room-heat learning.
                _heatTimer -= dt;
                if (_heatTimer <= 0f) { _heatTimer = 5f; string room = RoomName(_playerObj.transform.position); _roomHeat[room] = (_roomHeat.ContainsKey(room) ? _roomHeat[room] : 0) + 1; }
            }

            // Interact / attack inputs.
            if (_interactQueued || Input.GetKeyDown(KeyCode.F)) { _interactQueued = false; TryInteract(); }
            if (_attackQueued || Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                _attackQueued = false;
                if (_qteActive) ResolveQte();
                else TryStartAttack();
            }
            if (Input.GetKeyDown(KeyCode.F) && false) { /* F handled above */ }
        }

        private Vector2 _touchLookDelta;
        private void HandleTouchInput()
        {
            _touchLookDelta = Vector2.zero;
            if (!Input.touchSupported && !Application.isMobilePlatform) return;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                bool leftSide = t.position.x < Screen.width * 0.45f && t.position.y < Screen.height * 0.55f;
                if (t.phase == TouchPhase.Began)
                {
                    if (leftSide && _moveTouchId < 0) { _moveTouchId = t.fingerId; _moveTouchStart = t.position; _moveTouchNow = t.position; }
                    else if (_lookTouchId < 0) { _lookTouchId = t.fingerId; _lookTouchLast = t.position; }
                }
                else if (t.phase == TouchPhase.Moved)
                {
                    if (t.fingerId == _moveTouchId) _moveTouchNow = t.position;
                    if (t.fingerId == _lookTouchId)
                    {
                        Vector2 delta = t.position - _lookTouchLast;
                        _lookTouchDelta += new Vector2(delta.x * 0.06f, -delta.y * 0.06f);
                        _lookTouchLast = t.position;
                    }
                }
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    if (t.fingerId == _moveTouchId) _moveTouchId = -1;
                    if (t.fingerId == _lookTouchId) _lookTouchId = -1;
                }
            }
        }

        private string RoomName(Vector3 pos)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < _roomCentres.Count; i++)
            {
                float d = Vector3.Distance(_roomCentres[i], new Vector3(pos.x, _roomCentres[i].y, pos.z)) + Mathf.Abs(_roomCentres[i].y - pos.y) * 3f;
                if (d < bestD) { bestD = d; best = i; }
            }
            return "Room" + best;
        }

        // -------------------------------------------------------------- noise
        /// <summary>Every loud action in the game funnels through here.</summary>
        private void EmitNoise(Vector3 pos, float radius, bool continuous)
        {
            if (_hunterObj == null || _hidden) return;
            float d = Vector3.Distance(pos, _hunterObj.transform.position);
            if (d <= radius)
            {
                _lastKnownPlayerPos = pos;
                if (_hunterState == HunterState.Patrol || _hunterState == HunterState.Search || _hunterState == HunterState.Suspicious)
                {
                    SetHunterState(HunterState.Suspicious, pos);
                }
            }
        }

        private void SetHunterState(HunterState next, Vector3 targetPos)
        {
            if (_hunterState == HunterState.Chase && next == HunterState.Chase) { _hunterTargetPos = targetPos; return; }
            bool enteringChase = next == HunterState.Chase && _hunterState != HunterState.Chase;
            _hunterState = next;
            _hunterTargetPos = targetPos;
            _hunterPath = FindPath(_hunterObj.transform.position, targetPos);
            _hunterPathIndex = 0;
            _hunterStateTimer = 0f;
            if (next == HunterState.Chase)
            {
                _memoryTimer = 15f;
                if (enteringChase)
                {
                    float d = Vector3.Distance(_playerObj.transform.position, _hunterObj.transform.position);
                    if (d < 7f) TriggerScare(0.3f);
                    int idx = UnityEngine.Random.Range(0, SpottedLines.Length);
                    ShowSubtitle("Tishay (thoughts): " + SpottedLines[idx], 2.5f);
                }
            }
        }

        // ------------------------------------------------------------- hunter
        private void UpdateHunter(float dt)
        {
            if (_hunterObj == null) return;
            _hunterStateTimer += dt;
            _perceptionTimer -= dt;

            float difficulty = 1f + 0.08f * (_save.day - 1);
            float speed;
            switch (_hunterState)
            {
                case HunterState.Patrol: speed = 1.5f * difficulty; break;
                case HunterState.Suspicious: speed = 2.5f * difficulty; break;
                case HunterState.Search: speed = 3.0f * difficulty; break;
                case HunterState.Chase: speed = 4.45f * difficulty; break;
                default: speed = 0f; break;
            }

            // Perception (checked 6x/sec for mobile perf).
            if (_perceptionTimer <= 0f)
            {
                _perceptionTimer = 0.16f;
                if (CanSeePlayer())
                {
                    _seenMeter = Mathf.Min(1f, _seenMeter + 0.45f);
                    _lastKnownPlayerPos = _playerObj.transform.position;
                    if (_seenMeter >= 0.55f) SetHunterState(HunterState.Chase, _playerObj.transform.position);
                    else if (_hunterState == HunterState.Patrol) SetHunterState(HunterState.Suspicious, _playerObj.transform.position);
                }
                else _seenMeter = Mathf.Max(0f, _seenMeter - 0.10f);
            }

            switch (_hunterState)
            {
                case HunterState.Patrol:
                    if (_hunterPath == null || _hunterPathIndex >= _hunterPath.Count)
                    {
                        Vector3 dest;
                        // Learning heuristic: 40% of patrols bias to the player's most-visited area.
                        if (_roomHeat.Count > 0 && UnityEngine.Random.value < 0.4f)
                        {
                            string hot = null; int best = -1;
                            foreach (var kv in _roomHeat) if (kv.Value > best) { best = kv.Value; hot = kv.Key; }
                            int idx = hot != null ? int.Parse(hot.Replace("Room", "")) : 0;
                            idx = Mathf.Clamp(idx, 0, _roomCentres.Count - 1);
                            dest = _roomCentres[idx];
                        }
                        else dest = _nodes.Count > 0 ? _nodes[UnityEngine.Random.Range(0, _nodes.Count)].pos : _hunterObj.transform.position;
                        _hunterPath = FindPath(_hunterObj.transform.position, dest);
                        _hunterPathIndex = 0;
                    }
                    // Occasionally sets a snap trap while patrolling.
                    _trapTimer -= dt;
                    if (_trapTimer <= 0f) { _trapTimer = 45f; PlaceTrap(); }
                    break;
                case HunterState.Suspicious:
                    if (ReachedPathEnd()) SetHunterState(HunterState.Search, _lastKnownPlayerPos);
                    break;
                case HunterState.Search:
                    if (ReachedPathEnd())
                    {
                        if (_hunterStateTimer > 6f) { SetHunterState(HunterState.Patrol, _hunterObj.transform.position); }
                        else
                        {
                            Vector3 near = _lastKnownPlayerPos + new Vector3(UnityEngine.Random.Range(-5f, 5f), 0, UnityEngine.Random.Range(-5f, 5f));
                            _hunterPath = FindPath(_hunterObj.transform.position, near); _hunterPathIndex = 0;
                        }
                    }
                    break;
                case HunterState.Chase:
                    _memoryTimer -= dt;
                    // Repath to the player twice a second.
                    if (_hunterStateTimer > 0.5f)
                    {
                        _hunterStateTimer = 0f;
                        Vector3 goal = CanSeePlayerQuick() ? _playerObj.transform.position : _lastKnownPlayerPos;
                        _hunterPath = FindPath(_hunterObj.transform.position, goal); _hunterPathIndex = 0;
                    }
                    if (!CanSeePlayerQuick() && _memoryTimer <= 0f) SetHunterState(HunterState.Search, _lastKnownPlayerPos);
                    if (Vector3.Distance(_hunterObj.transform.position, _playerObj.transform.position) < HunterKillDistance && !_hidden)
                    {
                        SetHunterState(HunterState.Kill, _playerObj.transform.position);
                        Die();
                        return;
                    }
                    break;
            }

            MoveAlongPath(dt, speed, true);
            // Hunter opens doors he reaches (with a creak the player can hear).
            foreach (var d in _doors)
            {
                if (!d.open && d.pivot != null && Vector3.Distance(d.pivot.transform.position, _hunterObj.transform.position) < 1.7f)
                {
                    d.open = true;
                    if (Vector3.Distance(d.pivot.transform.position, _playerObj.transform.position) < 9f) PlaySfx(_clipCreak, 0.6f);
                }
            }
            // Hunter footsteps.
            if (_hunterPath != null && _hunterPathIndex < _hunterPath.Count)
            {
                _hunterStepTimer -= dt;
                if (_hunterStepTimer <= 0f)
                {
                    _hunterStepTimer = _hunterState == HunterState.Chase ? 0.3f : 0.48f;
                    if (_hunterSfx != null) _hunterSfx.PlayOneShot(_clipHunterStep, 0.8f);
                }
            }
            // Point the torch where he walks.
            if (_hunterLight != null && _hunterPath != null && _hunterPathIndex < _hunterPath.Count)
                _hunterLight.transform.LookAt(_hunterPath[_hunterPathIndex] + Vector3.up * 1.2f);
        }

        private bool ReachedPathEnd() { return _hunterPath == null || _hunterPathIndex >= _hunterPath.Count; }

        private bool CanSeePlayerQuick()
        {
            if (_hidden || _playerObj == null) return false;
            Vector3 eye = _hunterObj.transform.position + Vector3.up * 1.9f;
            Vector3 target = _playerObj.transform.position + Vector3.up * 1.3f;
            float dist = Vector3.Distance(eye, target);
            if (dist > 11f) return false;
            return !Physics.Linecast(eye, target, 1 << 8);
        }

        private bool CanSeePlayer()
        {
            if (_hidden || _playerObj == null || _hunterObj == null) return false;
            Vector3 eye = _hunterObj.transform.position + Vector3.up * 1.9f;
            Vector3 target = _playerObj.transform.position + Vector3.up * 1.3f;
            Vector3 toPlayer = target - eye;
            float dist = toPlayer.magnitude;
            float range = (_crouching ? 7f : 10f) + 0.4f * (_save.day - 1);
            if (dist > range) return false;
            Vector3 forward = _hunterObj.transform.forward;
            float angle = Vector3.Angle(new Vector3(forward.x, 0, forward.z), new Vector3(toPlayer.x, 0, toPlayer.z));
            if (angle > 60f && dist > 2.2f) return false; // 120-degree cone, plus close-range awareness
            if (Physics.Linecast(eye, target, 1 << 8)) return false;
            return true;
        }

        private void MoveAlongPath(float dt, float speed, bool isHunter)
        {
            Transform t = _hunterT;
            if (_hunterPath == null || _hunterPathIndex >= _hunterPath.Count || speed <= 0f) return;
            Vector3 goal = _hunterPath[_hunterPathIndex];
            Vector3 pos = t.position;
            Vector3 flatGoal = new Vector3(goal.x, pos.y, goal.z);
            // Floor changes happen along stair nodes, whose Y differs: ease Y toward the node Y.
            float targetY = goal.y;
            Vector3 next = Vector3.MoveTowards(pos, new Vector3(goal.x, targetY, goal.z), speed * dt);
            // Keep movement mostly planar but allow stair Y interpolation when close horizontally.
            if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(goal.x, 0, goal.z)) < 1.2f)
                pos = next;
            else
                pos = Vector3.MoveTowards(pos, flatGoal, speed * dt);
            if (Vector3.Distance(pos, new Vector3(goal.x, pos.y, goal.z)) < 0.45f && Mathf.Abs(pos.y - targetY) < 0.6f)
            { pos.y = targetY; _hunterPathIndex++; }
            Vector3 dir = (new Vector3(goal.x, pos.y, goal.z) - t.position);
            t.position = pos;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
                t.rotation = Quaternion.Slerp(t.rotation, look, dt * 6f);
            }
        }

        private void PlaceTrap()
        {
            if (_traps.Count >= 3) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SnapTrap";
            go.transform.position = _hunterObj.transform.position + Vector3.up * 0.05f;
            go.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);
            go.GetComponent<Renderer>().material = _matRed;
            go.layer = 0;
            _traps.Add(new TrapData { root = go, armed = true });
        }

        private void UpdateTraps(float dt)
        {
            for (int i = _traps.Count - 1; i >= 0; i--)
            {
                var trap = _traps[i];
                if (!trap.armed || trap.root == null) continue;
                if (Vector3.Distance(trap.root.transform.position, _playerObj.transform.position) < 0.75f)
                {
                    trap.armed = false;
                    PlaySfx(_clipTrap, 1f);
                    _slowTimer = 3f;
                    EmitNoise(_playerObj.transform.position, 13f, false);
                    ShowSubtitle("SNAP! A trap. The Shadow knows you're near.", 2.5f);
                    Destroy(trap.root);
                    _traps.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------ targets
        private void UpdateTargets(float dt)
        {
            foreach (var t in _targets)
            {
                if (!t.alive)
                {
                    if (t.fallAmount < 1f && t.body != null)
                    {
                        t.fallAmount = Mathf.Min(1f, t.fallAmount + dt * 2.2f);
                        t.body.transform.localRotation = Quaternion.Euler(88f * t.fallAmount, 0, 0);
                        t.root.transform.position += Vector3.down * dt * 0.15f;
                    }
                    continue;
                }
                if (t.fleeing)
                {
                    t.fleeTimer -= dt;
                    if (t.fleeTimer <= 0f) t.fleeing = false;
                }
                if (t.path == null || t.pathIndex >= t.path.Count)
                {
                    t.waitTimer -= dt;
                    if (t.waitTimer <= 0f)
                    {
                        Vector3 dest = _nodes.Count > 0 ? _nodes[UnityEngine.Random.Range(0, _nodes.Count)].pos : t.home;
                        // Keep targets mostly on their own floor.
                        int guard = 0;
                        while (Mathf.Abs(dest.y - t.root.transform.position.y) > 0.5f && guard++ < 8 && _nodes.Count > 0)
                            dest = _nodes[UnityEngine.Random.Range(0, _nodes.Count)].pos;
                        t.path = FindPath(t.root.transform.position, dest);
                        t.pathIndex = 0;
                        t.waitTimer = UnityEngine.Random.Range(1f, 4f);
                    }
                    continue;
                }
                float sp = t.fleeing ? 3.4f : t.speed;
                Vector3 goal = t.path[t.pathIndex];
                Vector3 pos = t.root.transform.position;
                Vector3 next = Vector3.MoveTowards(pos, new Vector3(goal.x, goal.y + 0.05f, goal.z), sp * dt);
                Vector3 dir = next - pos;
                t.root.transform.position = next;
                if (dir.sqrMagnitude > 0.0001f)
                    t.root.transform.rotation = Quaternion.Slerp(t.root.transform.rotation, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z)), dt * 5f);
                if (Vector3.Distance(next, new Vector3(goal.x, next.y, goal.z)) < 0.4f) t.pathIndex++;
            }
        }

        // --------------------------------------------------------- interaction
        private void UpdateInteractPrompt()
        {
            _currentInteract = null;
            if (_hidden) { if (_hudPrompt != null) _hudPrompt.text = "[F / USE] Leave hiding spot"; return; }
            RaycastHit hit;
            Vector3 origin = _camT.position;
            if (Physics.Raycast(origin, _camT.forward, out hit, InteractDistance))
            {
                Interactable inter;
                if (_interactByObject.TryGetValue(hit.collider.gameObject, out inter)) _currentInteract = inter;
            }
            if (_currentInteract == null)
            {
                // Proximity fallback (small pickups can be hard to centre on touch).
                float best = 1.6f;
                foreach (var inter in _interactables)
                {
                    if (inter.root == null || !inter.root.activeInHierarchy) continue;
                    float d = Vector3.Distance(inter.root.transform.position, _playerObj.transform.position);
                    if (d < best) { best = d; _currentInteract = inter; }
                }
            }
            if (_hudPrompt != null)
            {
                if (_qteActive) _hudPrompt.text = "";
                else if (_currentInteract != null) _hudPrompt.text = "[F / USE] " + PromptFor(_currentInteract);
                else
                {
                    var near = NearestLivingTarget(2.4f);
                    _hudPrompt.text = near != null ? "[CLICK / ATTACK] Strike from stealth" : "";
                }
            }
        }

        private string PromptFor(Interactable inter)
        {
            switch (inter.kind)
            {
                case InteractKind.Door: return inter.door != null && inter.door.locked ? "Locked — needs the basement hammer (pry tool)" : (inter.door != null && inter.door.open ? "Close door" : "Open door");
                case InteractKind.Drawer: return "Search drawer";
                case InteractKind.Closet: return "Hide in closet";
                case InteractKind.EscapeDoor: return _targetsRemaining > 0 ? "Front door — eliminate all targets first" : (_hasFrontKey ? "ESCAPE!" : "Front door — locked, find the key");
                default: return inter.label;
            }
        }

        private void TryInteract()
        {
            if (_hidden) { ExitHide(); return; }
            if (_currentInteract == null) return;
            var inter = _currentInteract;
            switch (inter.kind)
            {
                case InteractKind.Door:
                    {
                        var d = inter.door;
                        if (d == null) break;
                        if (d.locked)
                        {
                            if (HasPryTool())
                            {
                                d.locked = false; d.open = true; PlaySfx(_clipCreak, 0.9f);
                                ShowSubtitle("Hammer as a pry bar. Door forced open.", 2.5f);
                                EmitNoise(_playerObj.transform.position, 8f, false);
                            }
                            else ShowSubtitle("Locked tight. Something in the basement could force this...", 3f);
                            break;
                        }
                        d.open = !d.open;
                        PlaySfx(_clipCreak, 0.8f);
                        EmitNoise(_playerObj.transform.position, d.open ? 5f : 8f, false);
                        break;
                    }
                case InteractKind.EscapeDoor:
                    if (_targetsRemaining > 0) ShowSubtitle("Not yet. " + _targetsRemaining + " target(s) still breathing in this house.", 3f);
                    else if (!_hasFrontKey) ShowSubtitle("Locked. The front-door key is hidden in a drawer somewhere.", 3f);
                    else Win();
                    break;
                case InteractKind.Drawer:
                    {
                        inter.root.transform.position = inter.closedPos + _playerObj.transform.forward * 0.0f + new Vector3(0, 0, 0.28f);
                        PlaySfx(_clipCreak, 0.5f);
                        EmitNoise(_playerObj.transform.position, 4f, false);
                        if (inter.containsKey && !_hasFrontKey)
                        {
                            inter.containsKey = false; _hasFrontKey = true;
                            PlaySfx(_clipPickup, 1f);
                            ShowSubtitle("Found the FRONT-DOOR KEY. Finish the list, then run.", 3.5f);
                        }
                        else ShowSubtitle(UnityEngine.Random.value < 0.3f ? "Drawer: old socks and secrets. No key." : "Nothing useful. The voices sigh.", 2f);
                        break;
                    }
                case InteractKind.Closet: EnterHide(inter); break;
                case InteractKind.Lamp: SetLamp(inter, !inter.on); PlaySfx(_clipClick, 0.7f); break;
                case InteractKind.Radio:
                    inter.on = !inter.on;
                    if (inter.radioAudio != null) { if (inter.on) inter.radioAudio.Play(); else inter.radioAudio.Stop(); }
                    ShowSubtitle(inter.on ? "Radio on. Somewhere, The Shadow turns his head." : "Radio off.", 2.5f);
                    break;
                case InteractKind.PickupHammer:
                    inter.root.SetActive(false); inter.used = true; _save.hammerUnlocked = true; WriteSave();
                    PlaySfx(_clipPickup, 1f);
                    ShowSubtitle("Hammer acquired. Finishes are quicker — and quieter — now.", 3f);
                    break;
                case InteractKind.PickupKey:
                    inter.root.SetActive(false); _hasFrontKey = true; PlaySfx(_clipPickup, 1f);
                    ShowSubtitle("Front-door key acquired.", 3f);
                    break;
                case InteractKind.Note:
                    ShowSubtitle(inter.noteText, 6f);
                    break;
                case InteractKind.Stair:
                    if (inter.hasDestination)
                    {
                        _cc.enabled = false;
                        _playerObj.transform.position = inter.destination + Vector3.up * 0.05f;
                        _cc.enabled = true;
                        PlaySfx(_clipStep, 0.8f);
                        EmitNoise(inter.destination, 4f, false);
                    }
                    break;
            }
            UpdateHud();
        }

        private bool HasPryTool()
        {
            // The hammer doubles as the pry tool — one tool, two uses, no extra inventory slots.
            return _save.hammerUnlocked;
        }

        private void SetLamp(Interactable inter, bool on)
        {
            inter.on = on;
            if (inter.lampLight != null) inter.lampLight.enabled = on;
            if (inter.lampBulb != null)
                inter.lampBulb.material.SetColor("_EmissionColor", on ? new Color(1f, 0.75f, 0.3f) * 1.6f : Color.black);
        }

        private void EnterHide(Interactable closet)
        {
            _hidden = true; _hideCloset = closet;
            _hideReturnPos = _playerObj.transform.position;
            _cc.enabled = false;
            Vector3 inside = closet.root.transform.position + new Vector3(0, -1.1f, -0.1f);
            // Closet door object sits at the front face; step slightly inside the unit.
            _playerObj.transform.position = new Vector3(closet.root.transform.position.x, _playerObj.transform.position.y, closet.root.transform.position.z - 0.15f);
            _cc.enabled = true;
            ShowSubtitle("Hidden. Hold still. Breathe quieter than the house.", 3f);
            _seenMeter = 0f;
            if (_hunterState == HunterState.Chase) SetHunterState(HunterState.Search, _lastKnownPlayerPos);
        }

        private void ExitHide()
        {
            _hidden = false;
            _cc.enabled = false;
            _playerObj.transform.position = _hideReturnPos;
            _cc.enabled = true;
            PlaySfx(_clipCreak, 0.4f);
        }

        private void UpdateDoors(float dt)
        {
            foreach (var d in _doors)
            {
                if (d.pivot == null) continue;
                float target = d.open ? 1f : 0f;
                d.openAmount = Mathf.MoveTowards(d.openAmount, target, dt * 2.6f);
                d.pivot.transform.rotation = Quaternion.Euler(0f, d.closedY + d.openAmount * 102f, 0f);
            }
        }

        // -------------------------------------------------------------- combat
        private TargetData NearestLivingTarget(float maxDist)
        {
            TargetData best = null; float bestD = maxDist;
            foreach (var t in _targets)
            {
                if (!t.alive) continue;
                float d = Vector3.Distance(t.root.transform.position, _playerObj.transform.position);
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        private void TryStartAttack()
        {
            if (_hidden || _qteActive) return;
            var t = NearestLivingTarget(2.5f);
            PlaySfx(_clipSwing, 0.8f);
            if (t == null) { EmitNoise(_playerObj.transform.position, 3f, false); return; }
            // Stealth check: striking from behind (or while crouched) opens the timing window.
            Vector3 toPlayer = (_playerObj.transform.position - t.root.transform.position).normalized;
            float behind = Vector3.Dot(t.root.transform.forward, toPlayer); // < 0 means player is behind
            bool stealthy = behind < 0.1f || _crouching;
            _qteTarget = t; _qteActive = true; _qteTime = 0f;
            _qteDuration = _save.hammerUnlocked ? 1.35f : 1.15f;
            _qteGreenStart = UnityEngine.Random.Range(0.45f, 0.6f);
            _qteGreenEnd = _qteGreenStart + (stealthy ? 0.22f : 0.13f);
            var qte = _hudPanel.transform.Find("QTE");
            if (qte != null) qte.gameObject.SetActive(true);
            ShowSubtitle(stealthy ? "Quietly now... strike in the zone!" : "He almost saw you — strike in the zone!", 1.2f);
        }

        private void UpdateQte(float dt)
        {
            if (!_qteActive) return;
            _qteTime += dt;
            float p = Mathf.Clamp01(_qteTime / _qteDuration);
            if (_qteMarker != null)
            {
                var rt = _qteMarker.rectTransform;
                rt.anchorMin = new Vector2(p, 0); rt.anchorMax = new Vector2(p + 0.02f, 1);
            }
            if (_qteTime >= _qteDuration) FailQte(false);
        }

        private void ResolveQte()
        {
            if (!_qteActive || _qteTarget == null) return;
            float p = _qteTime / _qteDuration;
            if (p >= _qteGreenStart && p <= _qteGreenEnd) CompleteKill(_qteTarget, true);
            else if (p >= _qteGreenStart - 0.18f && p <= _qteGreenEnd + 0.18f) CompleteKill(_qteTarget, false);
            else FailQte(true);
            EndQte();
        }

        private void FailQte(bool whiffed)
        {
            if (_qteTarget != null)
            {
                _qteTarget.fleeing = true; _qteTarget.fleeTimer = 4f; _qteTarget.path = null;
                PlaySfx(_clipScream, 0.9f);
                EmitNoise(_playerObj.transform.position, 15f, false); // a scream carries
                ShowSubtitle("Missed! That scream just told The Shadow exactly where you are.", 3f);
            }
            EndQte();
        }

        private void EndQte()
        {
            _qteActive = false; _qteTarget = null;
            var qte = _hudPanel.transform.Find("QTE");
            if (qte != null) qte.gameObject.SetActive(false);
        }

        private void CompleteKill(TargetData t, bool silent)
        {
            if (t == null || !t.alive) return;
            t.alive = false;
            _targetsRemaining = Mathf.Max(0, _targetsRemaining - 1);
            _killsThisRun++; _save.totalKills++;
            WriteSave();
            // Stylised, cartoonish feedback: red cubes, no realistic gore.
            for (int i = 0; i < 10; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = t.root.transform.position + Vector3.up * 1.2f;
                cube.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.08f, 0.2f);
                cube.GetComponent<Renderer>().material = _matRed;
                cube.layer = 0;
                var rb = cube.AddComponent<Rigidbody>();
                rb.velocity = new Vector3(UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(1f, 3.5f), UnityEngine.Random.Range(-2f, 2f));
                Destroy(cube, 2.2f);
            }
            if (!silent)
            {
                PlaySfx(_clipScream, 1f);
                EmitNoise(t.root.transform.position, 15f, false);
                ShowSubtitle("Messy. Loud. The Shadow heard that.", 3f);
            }
            else
            {
                int idx;
                do { idx = UnityEngine.Random.Range(0, KillLines.Length); } while (idx == _lastMonologue && KillLines.Length > 1);
                _lastMonologue = idx;
                ShowSubtitle("Tishay (thoughts): " + KillLines[idx], 3.2f);
            }
            if (_targetsRemaining <= 0)
                ShowSubtitle("That's everyone. " + (_hasFrontKey ? "FRONT DOOR. GO." : "Now find the front-door key (a drawer, somewhere)."), 4f);
            UpdateHud();
        }

        // ------------------------------------------------------------- editor
#if UNITY_EDITOR
        [MenuItem("KILLER/Build Android APK")]
        public static void BuildAndroidMenu()
        {
            EnsureBootstrapScene();
            PlayerSettings.companyName = "Tishay Dangare";
            PlayerSettings.productName = "KILLER";
            PlayerSettings.bundleVersion = GameVersion;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.tishaydangare.killer");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.buildAppBundle = false; // single .apk
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/KILLER_Bootstrap.unity" }, "Builds/Android/KILLER.apk", BuildTarget.Android, BuildOptions.None);
            Debug.Log("[KILLER] Android build: " + report.summary.result + " -> Builds/Android/KILLER.apk");
        }

        [MenuItem("KILLER/Build iOS Xcode Project")]
        public static void BuildIosMenu()
        {
            EnsureBootstrapScene();
            PlayerSettings.companyName = "Tishay Dangare";
            PlayerSettings.productName = "KILLER";
            PlayerSettings.bundleVersion = GameVersion;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.tishaydangare.killer");
            PlayerSettings.iOS.targetOSVersionString = "14.0";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/KILLER_Bootstrap.unity" }, "Builds/iOS", BuildTarget.iOS, BuildOptions.None);
            Debug.Log("[KILLER] iOS Xcode project: " + report.summary.result + " -> Builds/iOS (open in Xcode, sign, Archive, export the .ipa)");
        }

        private static void EnsureBootstrapScene()
        {
            // The game builds itself from an empty scene via RuntimeInitializeOnLoadMethod.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder("Assets")) { /* Assets always exists */ }
            EditorSceneManager.SaveScene(scene, "Assets/KILLER_Bootstrap.unity");
        }
#endif
    }
}
