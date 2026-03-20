using System;
using System.Collections.Generic;
using UnityEngine;
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
        [SerializeField] private Vector3 defaultOffset = new Vector3(0f, 1.26f, 0f);
        [SerializeField, Min(0.05f)] private float fadeDurationSeconds = 0.36f;
        [SerializeField] private int sortingOrder = 580;

        private readonly Dictionary<int, BubbleView> activeBubbles = new Dictionary<int, BubbleView>();
        private Canvas canvas;
        private RectTransform canvasRect;

        private void Awake()
        {
            EnsureCanvas();
        }

        private void LateUpdate()
        {
            if (activeBubbles.Count == 0)
            {
                return;
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

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
                    bubble.Root.anchoredPosition = localPoint;
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
            if (npc == null || string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            var followTarget = npc.Interactable != null && npc.Interactable.promptAnchor != null
                ? npc.Interactable.promptAnchor
                : npc.transform;

            ShowBubble(
                npc.GetInstanceID(),
                followTarget,
                speakerOverride ?? npc.NpcDisplayName,
                body,
                durationSeconds,
                defaultOffset);
        }

        public void ShowBubble(int id, Transform followTarget, string speaker, string body, float durationSeconds, Vector3 worldOffset)
        {
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
            rootRect.sizeDelta = new Vector2(340f, 98f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.66f);
            panelImage.raycastTarget = false;

            var speaker = CreateText(panel.transform, "Speaker", 14, FontStyle.Bold);
            speaker.rectTransform.anchorMin = new Vector2(0f, 1f);
            speaker.rectTransform.anchorMax = new Vector2(1f, 1f);
            speaker.rectTransform.pivot = new Vector2(0.5f, 1f);
            speaker.rectTransform.offsetMin = new Vector2(10f, -26f);
            speaker.rectTransform.offsetMax = new Vector2(-10f, -4f);
            speaker.alignment = TextAnchor.MiddleLeft;
            speaker.color = new Color(0.91f, 0.96f, 1f, 0.98f);

            var body = CreateText(panel.transform, "Body", 18, FontStyle.Italic);
            body.rectTransform.anchorMin = new Vector2(0f, 0f);
            body.rectTransform.anchorMax = new Vector2(1f, 1f);
            body.rectTransform.offsetMin = new Vector2(10f, 8f);
            body.rectTransform.offsetMax = new Vector2(-10f, -28f);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            body.color = Color.white;

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

        private void EnsureCanvas()
        {
            if (canvas != null && canvasRect != null)
            {
                return;
            }

            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

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
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.raycastTarget = false;
            return text;
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
