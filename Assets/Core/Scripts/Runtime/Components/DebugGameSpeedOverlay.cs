using UnityEngine;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Lightweight runtime time-scale overlay for debugging mission flow speed.
    /// Spawned automatically and persisted through scene reloads via DontDestroyOnLoad.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugGameSpeedOverlay : MonoBehaviour
    {
        private const string OverlayObjectName = "DebugGameSpeedOverlay";
        private const string TimeScalePrefKey = "debug.gameplay.time_scale";
        private const float DefaultTimeScale = 1f;
        private const float DefaultFixedDeltaTime = 0.02f;
        private const int WindowId = 0x7A1E;

        private static DebugGameSpeedOverlay s_Instance;

        [SerializeField] private bool visible = true;
        [SerializeField] private KeyCode toggleOverlayKey = KeyCode.F8;
        [SerializeField] private KeyCode resetSpeedKey = KeyCode.F7;
        [SerializeField, Min(0.05f)] private float minTimeScale = 0.2f;
        [SerializeField, Min(0.1f)] private float maxTimeScale = 4f;

        private float baseFixedDeltaTime = DefaultFixedDeltaTime;
        private float currentTimeScale = DefaultTimeScale;
        private Rect windowRect = new Rect(18f, 18f, 286f, 132f);
        private GUIStyle labelStyle;
        private GUIStyle hintStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                return;
            }

            if (s_Instance != null)
            {
                return;
            }

            var existing = FindFirstObjectByType<DebugGameSpeedOverlay>(FindObjectsInactive.Include);
            if (existing != null)
            {
                s_Instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var overlayObject = new GameObject(OverlayObjectName);
            s_Instance = overlayObject.AddComponent<DebugGameSpeedOverlay>();
            DontDestroyOnLoad(overlayObject);
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            baseFixedDeltaTime = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : DefaultFixedDeltaTime;
            if (maxTimeScale < minTimeScale)
            {
                maxTimeScale = minTimeScale;
            }

            currentTimeScale = Mathf.Clamp(
                PlayerPrefs.GetFloat(TimeScalePrefKey, DefaultTimeScale),
                minTimeScale,
                maxTimeScale);
            ApplyTimeScale(currentTimeScale, savePreference: false);
        }

        private void OnDestroy()
        {
            if (s_Instance != this)
            {
                return;
            }

            s_Instance = null;
            ApplyTimeScale(DefaultTimeScale, savePreference: false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleOverlayKey))
            {
                visible = !visible;
            }

            if (Input.GetKeyDown(resetSpeedKey))
            {
                ApplyTimeScale(DefaultTimeScale, savePreference: true);
            }

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                ApplyTimeScale(currentTimeScale + 0.1f, savePreference: true);
            }

            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                ApplyTimeScale(currentTimeScale - 0.1f, savePreference: true);
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            EnsureStyles();
            windowRect = GUI.Window(WindowId, windowRect, DrawWindowContents, "Debug Speed");
        }

        private void DrawWindowContents(int _)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Time Scale  x{currentTimeScale:0.00}", labelStyle);
            var nextScale = GUILayout.HorizontalSlider(currentTimeScale, minTimeScale, maxTimeScale);
            if (!Mathf.Approximately(nextScale, currentTimeScale))
            {
                ApplyTimeScale(nextScale, savePreference: true);
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x", GUILayout.Height(24f)))
            {
                ApplyTimeScale(0.5f, savePreference: true);
            }

            if (GUILayout.Button("1x", GUILayout.Height(24f)))
            {
                ApplyTimeScale(DefaultTimeScale, savePreference: true);
            }

            if (GUILayout.Button("2x", GUILayout.Height(24f)))
            {
                ApplyTimeScale(2f, savePreference: true);
            }

            if (GUILayout.Button("3x", GUILayout.Height(24f)))
            {
                ApplyTimeScale(3f, savePreference: true);
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label($"Hotkeys: {toggleOverlayKey} toggle  |  {resetSpeedKey} reset  |  +/- step", hintStyle);
            GUI.DragWindow(new Rect(0f, 0f, 9999f, 20f));
        }

        private void ApplyTimeScale(float value, bool savePreference)
        {
            currentTimeScale = Mathf.Clamp(value, minTimeScale, maxTimeScale);
            Time.timeScale = currentTimeScale;
            Time.fixedDeltaTime = Mathf.Max(0.0001f, baseFixedDeltaTime * currentTimeScale);

            if (!savePreference)
            {
                return;
            }

            PlayerPrefs.SetFloat(TimeScalePrefKey, currentTimeScale);
            PlayerPrefs.Save();
        }

        private void EnsureStyles()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.95f, 0.95f, 1f) }
                };
            }

            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 10,
                    wordWrap = true,
                    normal = { textColor = new Color(0.86f, 0.86f, 0.9f, 1f) }
                };
            }
        }
    }
}
