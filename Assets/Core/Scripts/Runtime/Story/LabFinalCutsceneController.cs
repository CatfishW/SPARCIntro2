using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabFinalCutsceneController : MonoBehaviour
    {
        [SerializeField] private string finalCutsceneVideoPath = "Assets/Core/Art/Animations/timelines/cutscene_rocket.mp4";
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private PlayableDirector playableDirector;
        [SerializeField, Min(0.1f)] private float videoPrepareTimeoutSeconds = 12f;
        [SerializeField, Min(0.1f)] private float preBlackHoldSeconds = 1.3f;
        [SerializeField, Min(0.1f)] private float fadeToBlackSeconds = 1.15f;
        [SerializeField, Min(0.1f)] private float blackHoldSeconds = 1.2f;
        [SerializeField] private Color fadeColor = Color.black;
        [SerializeField] private bool endGameAfterBlack = true;

        private Canvas overlayCanvas;
        private RawImage videoImage;
        private Image blackImage;
        private CanvasGroup blackGroup;
        private VideoPlayer videoPlayer;
        private AudioSource videoAudioSource;
        private RenderTexture videoTexture;
        private Coroutine activeRoutine;
        private bool completed;
        private bool videoCompleted;

        public event Action Completed;

        public bool IsPlaying => activeRoutine != null;

        private void Awake()
        {
            EnsureOverlay();
            SetBlackAlpha(0f);
            overlayCanvas.enabled = false;
        }

        private void OnDisable()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = false;
            }

            controlLock?.ForceReleaseAll();
        }

        private void OnDestroy()
        {
            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
                videoTexture = null;
            }
        }

        public void ResetSequence()
        {
            completed = false;
            SetBlackAlpha(0f);
            if (videoImage != null)
            {
                videoImage.enabled = false;
            }

            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = false;
            }
        }

        public void PlayEndingSequence()
        {
            if (activeRoutine != null || completed)
            {
                return;
            }

            activeRoutine = StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            ResolveRuntimeReferences();
            controlLock?.Acquire(unlockCursor: false);
            EnsureOverlay();
            EnsureVideoPlayer();
            overlayCanvas.enabled = true;
            SetBlackAlpha(0f);

            if (videoImage != null)
            {
                videoImage.enabled = false;
            }

            var playedVideo = false;
            var resolvedVideoPath = ResolveCutscenePath(finalCutsceneVideoPath);
            if (!string.IsNullOrWhiteSpace(resolvedVideoPath) && File.Exists(resolvedVideoPath) && videoPlayer != null)
            {
                var prepared = false;
                var prepareFailed = false;
                videoCompleted = false;

                void HandlePrepared(VideoPlayer _)
                {
                    prepared = true;
                    EnsureRenderTarget(videoPlayer);
                }

                void HandleError(VideoPlayer _, string message)
                {
                    prepareFailed = true;
                    Debug.LogWarning($"[LabFinalCutsceneController] Final cutscene video error: {message}", this);
                }

                void HandleFinished(VideoPlayer _)
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
                    videoPlayer.url = resolvedVideoPath;
                    videoPlayer.Prepare();

                    var timeout = Mathf.Max(0.5f, videoPrepareTimeoutSeconds);
                    while (!prepared && !prepareFailed && timeout > 0f)
                    {
                        timeout -= Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (prepared && !prepareFailed)
                    {
                        if (videoImage != null)
                        {
                            videoImage.enabled = true;
                        }

                        videoPlayer.Play();
                        while (!videoCompleted)
                        {
                            yield return null;
                        }

                        playedVideo = true;
                    }
                    else
                    {
                        Debug.LogWarning("[LabFinalCutsceneController] Final cutscene video prepare failed or timed out. Falling back to playable director.", this);
                    }
                }
                finally
                {
                    videoPlayer.prepareCompleted -= HandlePrepared;
                    videoPlayer.errorReceived -= HandleError;
                    videoPlayer.loopPointReached -= HandleFinished;
                }
            }

            if (!playedVideo && playableDirector != null)
            {
                playableDirector.Play();
                while (playableDirector.state == PlayState.Playing)
                {
                    yield return null;
                }
            }
            else if (!playedVideo)
            {
                yield return new WaitForSecondsRealtime(preBlackHoldSeconds);
            }

            if (videoPlayer != null)
            {
                videoPlayer.Stop();
            }

            if (videoImage != null)
            {
                videoImage.enabled = false;
            }

            yield return FadeBlackRoutine(1f, fadeToBlackSeconds);
            yield return new WaitForSecondsRealtime(blackHoldSeconds);

            completed = true;
            activeRoutine = null;
            Completed?.Invoke();

            if (endGameAfterBlack)
            {
                EndGame();
            }
        }

        private IEnumerator FadeBlackRoutine(float targetAlpha, float durationSeconds)
        {
            var startAlpha = blackGroup != null ? blackGroup.alpha : 0f;
            var elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / durationSeconds);
                SetBlackAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));
                yield return null;
            }

            SetBlackAlpha(targetAlpha);
        }

        private void ResolveRuntimeReferences()
        {
            controlLock = controlLock != null ? controlLock : FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
        }

        private void EnsureOverlay()
        {
            if (overlayCanvas != null && videoImage != null && blackImage != null && blackGroup != null)
            {
                return;
            }

            var canvasObject = new GameObject("LabFinalCutsceneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 940;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var videoObject = new GameObject("FinalCutsceneVideo", typeof(RectTransform), typeof(RawImage));
            videoObject.transform.SetParent(canvasObject.transform, false);
            videoImage = videoObject.GetComponent<RawImage>();
            videoImage.color = Color.white;
            videoImage.raycastTarget = false;

            var videoRect = videoObject.GetComponent<RectTransform>();
            videoRect.anchorMin = Vector2.zero;
            videoRect.anchorMax = Vector2.one;
            videoRect.offsetMin = Vector2.zero;
            videoRect.offsetMax = Vector2.zero;

            var blackObject = new GameObject("FinalCutsceneBlackOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
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
        }

        private void EnsureVideoPlayer()
        {
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
                if (videoPlayer == null)
                {
                    videoPlayer = gameObject.AddComponent<VideoPlayer>();
                }
            }

            if (videoAudioSource == null)
            {
                videoAudioSource = GetComponent<AudioSource>();
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
            if (videoTexture == null || videoTexture.width != width || videoTexture.height != height)
            {
                if (videoTexture != null)
                {
                    videoTexture.Release();
                    Destroy(videoTexture);
                }

                videoTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "LabFinalCutsceneRT"
                };
                videoTexture.Create();
            }

            player.targetTexture = videoTexture;
            videoImage.texture = videoTexture;
        }

        private static int ResolveVideoDimension(ulong value, int fallback)
        {
            if (value == 0ul || value > 8192ul)
            {
                return fallback;
            }

            return (int)value;
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
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, normalized));
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
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

        private static void EndGame()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
