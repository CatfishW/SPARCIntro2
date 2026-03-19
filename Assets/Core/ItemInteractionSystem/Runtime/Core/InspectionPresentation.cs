using System;
using UnityEngine;

namespace ItemInteraction
{
    [Serializable]
    public class InspectionPresentation
    {
        [Tooltip("Optional custom prefab used for inspection. When empty, the system creates a render-only clone from the source object.")]
        public GameObject customInspectionPrefab;

        [Tooltip("Optional override root used when auto-cloning visuals for inspection.")]
        public Transform sourceRootOverride;

        [Tooltip("Applies after the object is centered in the inspection rig.")]
        public Vector3 pivotOffset = Vector3.zero;

        [Tooltip("Initial local Euler angles of the inspected object.")]
        public Vector3 defaultEulerAngles = new Vector3(12f, -25f, 0f);

        [Tooltip("Multiplies the camera framing distance. Larger values make the object appear smaller.")]
        [Min(0.1f)]
        public float framingPadding = 1.25f;

        [Tooltip("Perspective field of view used by the preview camera.")]
        [Range(10f, 70f)]
        public float previewFieldOfView = 24f;

        [Tooltip("Optional caption rendered on the inspection overlay.")]
        [TextArea]
        public string caption;
    }
}
