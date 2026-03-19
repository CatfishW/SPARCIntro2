using UnityEngine;
using UnityEngine.UI;

namespace ItemInteraction
{
    internal static class RuntimeUiFactory
    {
        private static Font cachedFont;

        public static Font DefaultFont
        {
            get
            {
                if (cachedFont == null)
                {
                    cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return cachedFont;
            }
        }

        public static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static Text CreateText(string name, Transform parent, int fontSize, TextAnchor anchor)
        {
            var go = CreateUiObject(name, parent);
            var text = go.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            return text;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = CreateUiObject(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }
    }
}
