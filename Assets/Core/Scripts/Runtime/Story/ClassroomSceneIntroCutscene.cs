using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomSceneIntroCutscene : MonoBehaviour
    {
        [SerializeField] private string cutscenePath = "Assets/Core/Art/Animations/Custom/classroomcutscene.mp4";
        [SerializeField] private bool playOncePerSceneLoad = true;
        [SerializeField] private bool lockPlayerControls = true;
        [SerializeField] private bool allowSkipWithEscape = true;
        [SerializeField] private KeyCode skipKey = KeyCode.Escape;
        [SerializeField, Min(0f)] private float prepareTimeoutSeconds = 12f;
        [SerializeField, Min(0f)] private float blackInDurationSeconds = 0.34f;
        [SerializeField, Min(0f)] private float blackOutDurationSeconds = 0.46f;
        [SerializeField] private Color fadeColor = Color.black;
        [SerializeField, Min(100)] private int overlaySortingOrder = 920;

        private Canvas overlayCanvas;
        private RawImage videoImage;
        private Image blackImage;
        private CanvasGroup blackGroup;
        private AudioSource videoAudioSource;
        private VideoPlayer videoPlayer;
        private RenderTexture targetTexture;
        private ClassroomPlayerControlLock controlLock;

        private bool hasPlayed;
        private bool isPlaying;
        private bool skipRequested;
        private bool videoCompleted;
        private bool lockAcquired;

        public bool HasPlayed => hasPlayed;

        private void Awake()
        {
            ResolveReferences();
            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = false;
            }

            if (lockAcquired)
            {
                controlLock?.Release();
                lockAcquired = false;
            }

            isPlaying = false;
        }

        private void OnDestroy()
        {
            if (targetTexture != null)
            {
                targetTexture.Release();
                Destroy(targetTexture);
                targetTexture = null;
            }
        }

        public IEnumerator PlayIntroSequenceRoutine()
        {
            if (playOncePerSceneLoad && hasPlayed)
            {
                yield break;
            }

            if (isPlaying)
            {
                yield break;
            }

            ResolveReferences();
            var resolvedPath = ResolveCutscenePath(cutscenePath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                Debug.LogWarning($"[ClassroomSceneIntroCutscene] Missing cutscene file: {cutscenePath}", this);
                hasPlayed = true;
                yield break;
            }

            if (overlayCanvas == null || videoImage == null || blackGroup == null || videoPlayer == null)
            {
                Debug.LogWarning("[ClassroomSceneIntroCutscene] Intro overlay is not ready. Skipping cutscene.", this);
                hasPlayed = true;
                yield break;
            }

            isPlaying = true;
            skipRequested = false;
            videoCompleted = false;
            overlayCanvas.enabled = true;
            videoImage.enabled = false;
            SetBlackAlpha(0f);

            if (lockPlayerControls)
            {
                controlLock?.Acquire(unlockCursor: false);
                lockAcquired = controlLock != null && controlLock.IsLocked;
            }

            var prepared = false;
            var prepareFailed = false;
            void HandlePrepared(VideoPlayer player)
            {
                prepared = true;
                EnsureRenderTarget(player);
            }

            void HandleError(VideoPlayer player, string message)
            {
                prepareFailed = true;
                Debug.LogWarning($"[ClassroomSceneIntroCutscene] Video error: {message}", this);
            }

            void HandleFinished(VideoPlayer player)
            {
                videoCompleted = true;
            }

            videoPlayer.prepareCompleted += HandlePrepared;
            videoPlayer.errorReceived += HandleError;
            videoPlayer.loopPointReached += HandleFinished;

            try
            {
                videoPlayer.Stop();
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = resolvedPath;
                videoPlayer.Prepare();

                var timeout = Mathf.Max(0.5f, prepareTimeoutSeconds);
                while (!prepared && !prepareFailed && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!prepared || prepareFailed)
                {
                    Debug.LogWarning("[ClassroomSceneIntroCutscene] Cutscene prepare failed or timed out.", this);
                    hasPlayed = true;
                    yield break;
                }

                videoImage.enabled = true;
                videoPlayer.Play();

                while (!videoCompleted && !skipRequested)
                {
                    if (allowSkipWithEscape && Input.GetKeyDown(skipKey))
                    {
                        skipRequested = true;
                    }

                    yield return null;
                }

                yield return FadeBlackRoutine(1f, blackInDurationSeconds);
                videoPlayer.Stop();
                videoImage.enabled = false;
                yield return FadeBlackRoutine(0f, blackOutDurationSeconds);

                hasPlayed = true;
            }
            finally
            {
                videoPlayer.prepareCompleted -= HandlePrepared;
                videoPlayer.errorReceived -= HandleError;
                videoPlayer.loopPointReached -= HandleFinished;

                if (overlayCanvas != null)
                {
                    overlayCanvas.enabled = false;
                }

                if (lockAcquired)
                {
                    controlLock?.Release();
                    lockAcquired = false;
                }

                isPlaying = false;
            }
        }

        private IEnumerator FadeBlackRoutine(float targetAlpha, float durationSeconds)
        {
            if (blackGroup == null)
            {
                yield break;
            }

            var startAlpha = blackGroup.alpha;
            if (durationSeconds <= Mathf.Epsilon)
            {
                SetBlackAlpha(targetAlpha);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / durationSeconds);
                var eased = Mathf.SmoothStep(0f, 1f, t);
                SetBlackAlpha(Mathf.Lerp(startAlpha, targetAlpha, eased));
                yield return null;
            }

            SetBlackAlpha(targetAlpha);
        }

        private void ResolveReferences()
        {
            if (controlLock == null)
            {
                controlLock = FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
            }

            EnsureUi();
            EnsureVideoPlayer();
        }

        private void EnsureUi()
        {
            if (overlayCanvas != null && videoImage != null && blackImage != null && blackGroup != null)
            {
                overlayCanvas.sortingOrder = overlaySortingOrder;
                return;
            }

            var canvasObject = new GameObject("ClassroomIntroCutsceneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = overlaySortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var rect = canvasObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var videoObject = new GameObject("CutsceneVideo", typeof(RectTransform), typeof(RawImage));
            videoObject.transform.SetParent(canvasObject.transform, false);
            videoImage = videoObject.GetComponent<RawImage>();
            videoImage.color = Color.white;
            videoImage.raycastTarget = false;

            var videoRect = videoObject.GetComponent<RectTransform>();
            videoRect.anchorMin = Vector2.zero;
            videoRect.anchorMax = Vector2.one;
            videoRect.offsetMin = Vector2.zero;
            videoRect.offsetMax = Vector2.zero;

            var blackObject = new GameObject("CutsceneBlackOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            blackObject.transform.SetParent(canvasObject.transform, false);
            blackImage = blackObject.GetComponent<Image>();
            blackImage.color = fadeColor;
            blackImage.raycastTarget = false;
            blackGroup = blackObject.GetComponent<CanvasGroup>();
            blackGroup.alpha = 0f;

            var blackRect = blackObject.GetComponent<RectTransform>();
            blackRect.anchorMin = Vector2.zero;
            blackRect.anchorMax = Vector2.one;
            blackRect.offsetMin = Vector2.zero;
            blackRect.offsetMax = Vector2.zero;

            overlayCanvas.enabled = false;
        }

        private void EnsureVideoPlayer()
        {
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.GetComponent<VideoPlayer>();
                if (videoPlayer == null)
                {
                    videoPlayer = gameObject.AddComponent<VideoPlayer>();
                }
            }

            if (videoAudioSource == null)
            {
                videoAudioSource = gameObject.GetComponent<AudioSource>();
                if (videoAudioSource == null)
                {
                    videoAudioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            videoAudioSource.playOnAwake = false;
            videoAudioSource.loop = false;
            videoAudioSource.spatialBlend = 0f;

            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = false;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetTargetAudioSource(0, videoAudioSource);
        }

        private void EnsureRenderTarget(VideoPlayer player)
        {
            if (player == null || videoImage == null)
            {
                return;
            }

            var width = ResolveVideoDimension(player.width, 1920);
            var height = ResolveVideoDimension(player.height, 1080);
            if (targetTexture == null || targetTexture.width != width || targetTexture.height != height)
            {
                if (targetTexture != null)
                {
                    targetTexture.Release();
                    Destroy(targetTexture);
                }

                targetTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "ClassroomIntroCutsceneRT"
                };
                targetTexture.Create();
            }

            player.targetTexture = targetTexture;
            videoImage.texture = targetTexture;
        }

        private static int ResolveVideoDimension(ulong value, int fallback)
        {
            if (value == 0ul || value > 8192ul)
            {
                return fallback;
            }

            return (int)value;
        }

        private void SetBlackAlpha(float alpha)
        {
            if (blackGroup == null || blackImage == null)
            {
                return;
            }

            blackGroup.alpha = Mathf.Clamp01(alpha);
            blackImage.color = fadeColor;
        }

        private static string ResolveCutscenePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (Path.IsPathRooted(trimmed))
            {
                return trimmed;
            }

            var normalized = trimmed.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, normalized));
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
        }
    }
}
