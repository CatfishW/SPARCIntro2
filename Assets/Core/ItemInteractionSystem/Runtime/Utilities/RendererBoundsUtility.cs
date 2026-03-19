using UnityEngine;

namespace ItemInteraction
{
    internal static class RendererBoundsUtility
    {
        public static bool TryCalculateBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.transform.position, Vector3.one * 0.25f);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return true;
        }
    }
}
