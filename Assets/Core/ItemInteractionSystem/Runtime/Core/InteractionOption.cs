using System;
using UnityEngine;
using UnityEngine.Events;

namespace ItemInteraction
{
    [Serializable]
    public class InteractionOption
    {
        [Tooltip("External story-facing option identifier. Example: look, inspect, read, take.")]
        public string id = "look";

        [Tooltip("Visible label shown to the player.")]
        public string label = "Look";

        [Tooltip("Direct input slot used to trigger this option.")]
        public InteractionOptionSlot slot = InteractionOptionSlot.Top;

        [Tooltip("Optional label override for the input hint. Leave empty to use the input source default.")]
        public string hintOverride;

        [Tooltip("Whether this option should appear in the prompt.")]
        public bool visible = true;

        [Tooltip("Whether this option can currently be triggered.")]
        public bool enabled = true;

        [Tooltip("Opens the 3D inspection overlay after invoking this option.")]
        public bool opensInspection;

        [Tooltip("Enable to use the inspection presentation below instead of the item default.")]
        public bool useInspectionOverride;

        [Tooltip("Optional inspection presentation override for this specific option.")]
        public InspectionPresentation inspectionOverride = new InspectionPresentation();

        [Tooltip("Optional local callback for simple scene hookups.")]
        public UnityEvent onInvoked = new UnityEvent();

        public bool IsAvailable => visible && enabled;
    }
}
