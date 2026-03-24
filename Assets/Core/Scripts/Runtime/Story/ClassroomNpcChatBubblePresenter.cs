using System;
using System.Collections.Generic;
using ItemInteraction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomNpcChatBubblePresenter : MonoBehaviour
    {
        private sealed class BubbleView
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Text SpeakerText;
            public Text BodyText;
            public Transform FollowTarget;
            public Vector3 WorldOffset;
            public float ExpireAt;
            public float FadeStartAt;
        }

        [SerializeField] private Camera worldCamera;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private ClassroomStoryConversationPresentationController conversationPresentationController;
        [SerializeField] private bool suppressWhileConversationActive = true;
        [SerializeField] private Vector3 defaultOffset = new Vector3(0f, 1.46f, 0f);
        [SerializeField, Min(0.05f)] private float fadeDurationSeconds = 0.36f;
        [SerializeField] private int sortingOrder = 625;

        private readonly Dictionary<int, BubbleView> activeBubbles = new Dictionary<int, BubbleView>();
        private Canvas canvas;
        private RectTransform canvasRect;

        private void Awake()
        {
            ResolveConversationController();
            ResolveInteractionDirector();
            EnsureCanvas();
        }

        private void LateUpdate()
        {
            if (ShouldSuppressBubbles())
            {
                ClearAllBubbles();
                return;
            }

            if (activeBubbles.Count == 0)
            {
                return;
            }

            worldCamera = ResolveWorldCamera();

            var now = Time.unscaledTime;
            var removal = ListPool<int>.Get();
            foreach (var pair in activeBubbles)
            {
                var id = pair.Key;
                var bubble = pair.Value;
                if (bubble == null || bubble.Root == null || bubble.FollowTarget == null || worldCamera == null)
                {
                    removal.Add(id);
                    continue;
                }

                var worldPoint = bubble.FollowTarget.position + bubble.WorldOffset;
                var viewport = worldCamera.WorldToViewportPoint(worldPoint);
                if (viewport.z <= 0f || viewport.x < -0.1f || viewport.x > 1.1f || viewport.y < -0.1f || viewport.y > 1.1f)
                {
                    bubble.Root.gameObject.SetActive(false);
                }
                else
                {
                    bubble.Root.gameObject.SetActive(true);
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect,
                        worldCamera.WorldToScreenPoint(worldPoint),
                        null,
                        out var localPoint);
                    var canvasSize = canvasRect.rect.size;
                    var halfCanvas = canvasSize * 0.5f;
                    var bubbleRect = bubble.Root.rect;
                    var maxX = halfCanvas.x - (bubbleRect.width * 0.5f) - 14f;
                    var minX = -maxX;
                    var minY = -halfCanvas.y + 14f;
                    var maxY = halfCanvas.y - bubbleRect.height - 14f;
                    bubble.Root.anchoredPosition = new Vector2(
                        Mathf.Clamp(localPoint.x, minX, maxX),
                        Mathf.Clamp(localPoint.y, minY, maxY));
                }

                if (now >= bubble.ExpireAt)
                {
                    removal.Add(id);
                    continue;
                }

                if (now >= bubble.FadeStartAt)
                {
                    var t = Mathf.Clamp01((bubble.ExpireAt - now) / Mathf.Max(0.05f, fadeDurationSeconds));
                    bubble.Group.alpha = t;
                }
                else
                {
                    bubble.Group.alpha = 1f;
                }
            }

            for (var index = 0; index < removal.Count; index++)
            {
                RemoveBubble(removal[index]);
            }

            ListPool<int>.Release(removal);
        }

        public void ShowBubble(StoryNpcAgent npc, string body, float durationSeconds = 4.6f, string speakerOverride = null)
        {
            if (ShouldSuppressBubbles())
            {
                return;
            }

            if (npc == null || string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var followTarget = npc.transform;
            var dynamicOffset = ResolveNpcOffset(npc, defaultOffset);
            var sanitizedBody = SanitizeBubbleText(body);

            ShowBubble(
                npc.GetInstanceID(),
                followTarget,
                speakerOverride ?? npc.NpcDisplayName,
                sanitizedBody,
                durationSeconds,
                dynamicOffset);
        }

        public void ShowBubble(int id, Transform followTarget, string speaker, string body, float durationSeconds, Vector3 worldOffset)
        {
            if (ShouldSuppressBubbles())
            {
                return;
            }

            if (followTarget == null || string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            EnsureCanvas();
            if (!activeBubbles.TryGetValue(id, out var bubble) || bubble == null || bubble.Root == null)
            {
                bubble = CreateBubbleView();
                activeBubbles[id] = bubble;
            }

            bubble.FollowTarget = followTarget;
            bubble.WorldOffset = worldOffset;
            bubble.SpeakerText.text = string.IsNullOrWhiteSpace(speaker) ? "NPC" : speaker;
            bubble.BodyText.text = body.Trim();
            bubble.ExpireAt = Time.unscaledTime + Mathf.Max(1.2f, durationSeconds);
            bubble.FadeStartAt = bubble.ExpireAt - Mathf.Max(0.12f, fadeDurationSeconds);
            bubble.Group.alpha = 1f;
            bubble.Root.gameObject.SetActive(true);
        }

        private BubbleView CreateBubbleView()
        {
            var root = new GameObject("NpcChatBubble", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(canvas.transform, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.sizeDelta = new Vector2(380f, 112f);

            var drop = new GameObject("DropShadow", typeof(RectTransform), typeof(Image));
            drop.transform.SetParent(root.transform, false);
            var dropRect = drop.GetComponent<RectTransform>();
            dropRect.anchorMin = Vector2.zero;
            dropRect.anchorMax = Vector2.one;
            dropRect.offsetMin = new Vector2(6f, -6f);
            dropRect.offsetMax = new Vector2(6f, -6f);
            var dropImage = drop.GetComponent<Image>();
            dropImage.color = new Color(0f, 0f, 0f, 0f);
            dropImage.raycastTarget = false;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(1f, 1f, 1f, 0f);
            panelImage.raycastTarget = false;
            var panelBorder = panel.gameObject.AddComponent<Outline>();
            panelBorder.effectColor = new Color(0.05f, 0.08f, 0.11f, 1f);
            panelBorder.effectDistance = new Vector2(3f, -3f);
            panelBorder.useGraphicAlpha = false;

            var speakerRibbon = new GameObject("SpeakerRibbon", typeof(RectTransform), typeof(Image));
            speakerRibbon.transform.SetParent(panel.transform, false);
            var ribbonRect = speakerRibbon.GetComponent<RectTransform>();
            ribbonRect.anchorMin = new Vector2(0f, 1f);
            ribbonRect.anchorMax = new Vector2(0f, 1f);
            ribbonRect.pivot = new Vector2(0f, 1f);
            ribbonRect.anchoredPosition = new Vector2(10f, -8f);
            ribbonRect.sizeDelta = new Vector2(158f, 30f);
            var ribbonImage = speakerRibbon.GetComponent<Image>();
            ribbonImage.color = new Color(0f, 0f, 0f, 0f);
            ribbonImage.raycastTarget = false;

            var speaker = CreateText(panel.transform, "Speaker", 14, FontStyle.Bold);
            speaker.rectTransform.anchorMin = new Vector2(0f, 1f);
            speaker.rectTransform.anchorMax = new Vector2(1f, 1f);
            speaker.rectTransform.pivot = new Vector2(0.5f, 1f);
            speaker.rectTransform.offsetMin = new Vector2(18f, -34f);
            speaker.rectTransform.offsetMax = new Vector2(-10f, -4f);
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.fontSize = 14;
            speaker.color = new Color(0.08f, 0.11f, 0.16f, 1f);

            var body = CreateText(panel.transform, "Body", 17, FontStyle.Bold);
            body.rectTransform.anchorMin = new Vector2(0f, 0f);
            body.rectTransform.anchorMax = new Vector2(1f, 1f);
            body.rectTransform.offsetMin = new Vector2(12f, 10f);
            body.rectTransform.offsetMax = new Vector2(-12f, -40f);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 14;
            body.resizeTextMaxSize = 17;
            body.color = new Color(0.08f, 0.11f, 0.16f, 1f);

            return new BubbleView
            {
                Root = rootRect,
                Group = root.GetComponent<CanvasGroup>(),
                SpeakerText = speaker,
                BodyText = body
            };
        }

        private void RemoveBubble(int id)
        {
            if (!activeBubbles.TryGetValue(id, out var bubble))
            {
                return;
            }

            activeBubbles.Remove(id);
            if (bubble?.Root != null)
            {
                Destroy(bubble.Root.gameObject);
            }
        }

        private void ClearAllBubbles()
        {
            if (activeBubbles.Count == 0)
            {
                return;
            }

            var ids = ListPool<int>.Get();
            foreach (var pair in activeBubbles)
            {
                ids.Add(pair.Key);
            }

            for (var index = 0; index < ids.Count; index++)
            {
                RemoveBubble(ids[index]);
            }

            ListPool<int>.Release(ids);
        }

        private void EnsureCanvas()
        {
            if (canvas != null && canvasRect != null)
            {
                return;
            }

            worldCamera = ResolveWorldCamera();

            var canvasObject = new GameObject("ClassroomNpcBubbleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
        }

        private static Text CreateText(Transform parent, string name, int fontSize, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = ResolveBuiltinFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.raycastTarget = false;
            return text;
        }

        private Camera ResolveWorldCamera()
        {
            if (worldCamera != null && worldCamera.isActiveAndEnabled && worldCamera.gameObject.activeInHierarchy)
            {
                return worldCamera;
            }

            if (Camera.main != null && Camera.main.isActiveAndEnabled && Camera.main.gameObject.activeInHierarchy)
            {
                worldCamera = Camera.main;
                return worldCamera;
            }

            var cameras = Camera.allCameras;
            for (var index = 0; index < cameras.Length; index++)
            {
                var candidate = cameras[index];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                worldCamera = candidate;
                return worldCamera;
            }

            worldCamera = null;
            return null;
        }

        private bool ShouldSuppressBubbles()
        {
            if (!suppressWhileConversationActive)
            {
                return false;
            }

            ResolveConversationController();
            if (conversationPresentationController != null && conversationPresentationController.IsConversationActive)
            {
                return true;
            }

            ResolveInteractionDirector();
            if (interactionDirector == null)
            {
                return false;
            }

            var currentFocus = interactionDirector.CurrentFocus;
            return currentFocus != null && currentFocus.GetComponent<StoryNpcAgent>() != null;
        }

        private void ResolveConversationController()
        {
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (conversationPresentationController != null && conversationPresentationController.gameObject.scene == activeScene)
            {
                return;
            }

            conversationPresentationController = FindSceneObject<ClassroomStoryConversationPresentationController>(activeScene, includeInactive: true);
        }

        private void ResolveInteractionDirector()
        {
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (interactionDirector != null && interactionDirector.gameObject.scene == activeScene)
            {
                return;
            }

            interactionDirector = FindSceneObject<InteractionDirector>(activeScene, includeInactive: true);
        }

        private static T FindSceneObject<T>(Scene scene, bool includeInactive)
            where T : Component
        {
            if (!scene.IsValid())
            {
                return null;
            }

            var candidates = FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate != null && candidate.gameObject.scene == scene)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Font ResolveBuiltinFont()
        {
            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (Exception)
            {
                // ignored
            }

            if (font != null)
            {
                return font;
            }

            var osCandidates = new[] { "Helvetica Neue", "Arial Unicode MS", "Segoe UI", "Noto Sans" };
            for (var index = 0; index < osCandidates.Length; index++)
            {
                try
                {
                    font = Font.CreateDynamicFontFromOSFont(osCandidates[index], 16);
                    if (font != null)
                    {
                        return font;
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return Font.CreateDynamicFontFromOSFont("Sans Serif", 16);
        }

        private static string SanitizeBubbleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var trimmed = text.Trim();
            if (trimmed.Length <= 128)
            {
                return trimmed;
            }

            return trimmed.Substring(0, 125).TrimEnd() + "...";
        }

        private static Vector3 ResolveNpcOffset(StoryNpcAgent npc, Vector3 fallbackOffset)
        {
            if (npc == null)
            {
                return fallbackOffset;
            }

            var renderers = npc.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return fallbackOffset;
            }

            var hasBounds = false;
            var bounds = default(Bounds);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return fallbackOffset;
            }

            var height = Mathf.Max(0.6f, bounds.size.y);
            var yOffset = Mathf.Clamp((height * 0.76f) + 0.08f, 0.98f, 1.42f);
            return new Vector3(0f, yOffset, 0f);
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new Stack<List<T>>(4);

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>(4);
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                {
                    return;
                }

                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
