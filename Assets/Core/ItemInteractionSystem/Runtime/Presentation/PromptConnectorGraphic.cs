using UnityEngine;
using UnityEngine.UI;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    internal sealed class PromptConnectorGraphic : MaskableGraphic
    {
        [SerializeField, Min(6)] private int segments = 18;
        [SerializeField, Min(0.5f)] private float thickness = 2.25f;
        [SerializeField, Min(0.5f)] private float startDotRadius = 3.75f;
        [SerializeField, Min(0f)] private float curveStrength = 78f;

        private Vector2 startPoint;
        private Vector2 endPoint;
        private bool hasCurve;
        private bool curveToRight;
        private float opacity = 1f;

        public void SetCurve(Vector2 start, Vector2 end, bool rightSidePrompt, float alpha)
        {
            startPoint = start;
            endPoint = end;
            curveToRight = rightSidePrompt;
            opacity = Mathf.Clamp01(alpha);
            hasCurve = true;
            SetVerticesDirty();
        }

        public void Clear()
        {
            if (!hasCurve)
            {
                return;
            }

            hasCurve = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (!hasCurve || opacity <= 0.001f)
            {
                return;
            }

            var tint = color;
            tint.a *= opacity;

            var direction = Mathf.Sign(endPoint.x - startPoint.x);
            if (Mathf.Abs(direction) < 0.5f)
            {
                direction = curveToRight ? 1f : -1f;
            }

            var dx = Mathf.Abs(endPoint.x - startPoint.x);
            var dy = endPoint.y - startPoint.y;
            var tangent = Mathf.Lerp(curveStrength * 0.45f, curveStrength, Mathf.Clamp01(dx / 320f));

            var control1 = startPoint + new Vector2(direction * tangent, Mathf.Clamp(dy * 0.15f, -18f, 18f));
            var control2 = endPoint - new Vector2(direction * (tangent * 0.9f), Mathf.Clamp(dy * 0.22f, -22f, 22f));

            var previousPoint = startPoint;
            for (var index = 1; index <= segments; index++)
            {
                var t = index / (float)segments;
                var point = EvaluateCubic(startPoint, control1, control2, endPoint, t);
                AddSegment(vh, previousPoint, point, thickness, tint);
                previousPoint = point;
            }

            AddDisc(vh, startPoint, startDotRadius, tint, 10);
        }

        private static Vector2 EvaluateCubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            var omt = 1f - t;
            return
                (omt * omt * omt * a) +
                (3f * omt * omt * t * b) +
                (3f * omt * t * t * c) +
                (t * t * t * d);
        }

        private static void AddSegment(VertexHelper vh, Vector2 from, Vector2 to, float width, Color color)
        {
            var delta = to - from;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var normal = new Vector2(-delta.y, delta.x).normalized * (width * 0.5f);
            var vertIndex = vh.currentVertCount;

            vh.AddVert(from - normal, color, Vector2.zero);
            vh.AddVert(from + normal, color, Vector2.zero);
            vh.AddVert(to + normal, color, Vector2.zero);
            vh.AddVert(to - normal, color, Vector2.zero);

            vh.AddTriangle(vertIndex + 0, vertIndex + 1, vertIndex + 2);
            vh.AddTriangle(vertIndex + 2, vertIndex + 3, vertIndex + 0);
        }

        private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color color, int slices)
        {
            var centerIndex = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);

            for (var index = 0; index <= slices; index++)
            {
                var angle = (index / (float)slices) * Mathf.PI * 2f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vh.AddVert(point, color, Vector2.zero);

                if (index == 0)
                {
                    continue;
                }

                vh.AddTriangle(centerIndex, centerIndex + index, centerIndex + index + 1);
            }
        }
    }
}
