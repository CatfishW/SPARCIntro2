using UnityEngine;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    internal static class BedroomStoryUiFactory
    {
        private static Font sansFont;
        private static Font serifFont;
        private static Sprite horizontalDividerSprite;

        public static Font DefaultSansFont
        {
            get
            {
                if (sansFont == null)
                {
                    sansFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return sansFont;
            }
        }

        public static Font DefaultSerifFont
        {
            get
            {
                if (serifFont == null)
                {
                    serifFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return serifFont;
            }
        }

        public static Sprite HorizontalDividerSprite
        {
            get
            {
                if (horizontalDividerSprite == null)
                {
                    horizontalDividerSprite = CreateHorizontalDividerSprite();
                }

                return horizontalDividerSprite;
            }
        }

        public static GameObject CreateUiObject(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        public static Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var gameObject = CreateUiObject(name, parent);
            var text = gameObject.AddComponent<Text>();
            text.font = DefaultSansFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            return text;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            var gameObject = CreateUiObject(name, parent);
            var image = gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Sprite CreateHorizontalDividerSprite()
        {
            const int width = 256;
            const int height = 4;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "BedroomStoryDividerGradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var horizontal = x / (width - 1f);
                    var distanceFromCenter = Mathf.Abs((horizontal * 2f) - 1f);
                    var alpha = Mathf.SmoothStep(1f, 0f, distanceFromCenter);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "BedroomStoryDividerGradientSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
