using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStorySceneTransition : MonoBehaviour
    {
        [SerializeField] private string targetScenePath = string.Empty;
        [SerializeField] private string targetSceneName = string.Empty;
        [SerializeField, Min(0f)] private float fadeInDurationSeconds = 1.1f;
        [SerializeField, Min(0f)] private float fadeOutDurationSeconds = 0.45f;
        [SerializeField] private Color fadeColor = Color.black;
        [SerializeField] private bool loadRequested;

        private Canvas overlayCanvas;
        private CanvasGroup overlayGroup;
        private Image overlayImage;
        private Coroutine transitionRoutine;

        public string TargetScenePath => targetScenePath;
        public string TargetSceneName => targetSceneName;
        public bool LoadRequested => loadRequested;
        public bool HasTargetScene => !string.IsNullOrWhiteSpace(targetSceneName);

        private void Awake()
        {
            EnsureOverlayBuilt();
            SetOverlayAlpha(1f);
        }

        private void Start()
        {
            StartIntroFade();
        }

        private void Update()
        {
            if (loadRequested && transitionRoutine == null)
            {
                CommitLoadIfRequested();
            }
        }

        public void Configure(string scenePath, string sceneName)
        {
            targetScenePath = scenePath;
            targetSceneName = sceneName;
        }

        public void RequestLoad()
        {
            loadRequested = true;
        }

        public void ResetRequest()
        {
            loadRequested = false;
        }

        public void CommitLoadIfRequested()
        {
            if (!loadRequested)
            {
                return;
            }

            if (!HasTargetScene)
            {
                loadRequested = false;
                Debug.LogWarning("ClassroomStorySceneTransition has no lab scene target configured yet.", this);
                return;
            }

            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            transitionRoutine = StartCoroutine(FadeOutAndLoadRoutine());
        }

        private void StartIntroFade()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            if (fadeInDurationSeconds <= Mathf.Epsilon)
            {
                SetOverlayAlpha(0f);
                transitionRoutine = null;
                return;
            }

            transitionRoutine = StartCoroutine(FadeOverlayRoutine(0f, fadeInDurationSeconds));
        }

        private IEnumerator FadeOutAndLoadRoutine()
        {
            loadRequested = false;
            yield return FadeOverlayRoutine(1f, fadeOutDurationSeconds);
            transitionRoutine = null;
            SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
        }

        private IEnumerator FadeOverlayRoutine(float targetAlpha, float durationSeconds)
        {
            EnsureOverlayBuilt();

            var startAlpha = overlayGroup != null ? overlayGroup.alpha : 0f;
            if (durationSeconds <= Mathf.Epsilon)
            {
                SetOverlayAlpha(targetAlpha);
                transitionRoutine = null;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / durationSeconds);
                var eased = Mathf.SmoothStep(0f, 1f, t);
                SetOverlayAlpha(Mathf.Lerp(startAlpha, targetAlpha, eased));
                yield return null;
            }

            SetOverlayAlpha(targetAlpha);
            transitionRoutine = null;
        }

        private void EnsureOverlayBuilt()
        {
            if (overlayCanvas != null && overlayGroup != null && overlayImage != null)
            {
                return;
            }

            var canvasObject = new GameObject("SceneFadeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 700;

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

            overlayGroup = canvasObject.AddComponent<CanvasGroup>();
            overlayGroup.alpha = 1f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            var imageObject = new GameObject("FadePanel", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            overlayImage = imageObject.GetComponent<Image>();
            overlayImage.color = fadeColor;
            overlayImage.raycastTarget = false;

            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
        }

        private void SetOverlayAlpha(float alpha)
        {
            EnsureOverlayBuilt();
            overlayGroup.alpha = Mathf.Clamp01(alpha);
            overlayImage.color = fadeColor;
        }
    }
}
