using System;
using ItemInteraction;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [Serializable]
    public sealed class StoryNpcOptionDefinition
    {
        [Tooltip("Stable option id used by story routing. Example: talk, ask, reassure, joke.")]
        public string id = "talk";

        [Tooltip("Label shown in the interaction prompt.")]
        public string label = "Talk";

        [Tooltip("Input slot used by the interaction system.")]
        public InteractionOptionSlot slot = InteractionOptionSlot.Top;

        [Tooltip("Optional custom hint shown in the prompt UI.")]
        public string hintOverride = string.Empty;

        [Tooltip("Whether this option is currently visible.")]
        public bool visible = true;

        [Tooltip("Whether this option can currently be selected.")]
        public bool enabled = true;

        [Tooltip("Optional stable route id. If left empty the agent builds one from npcId + option id.")]
        public string interactionId = string.Empty;

        [Tooltip("Whether this option should open the inspection view.")]
        public bool opensInspection;

        [Tooltip("Whether to use the custom inspection data below.")]
        public bool useInspectionOverride;

        [Tooltip("Optional inspection override for this option.")]
        public InspectionPresentation inspectionOverride = new InspectionPresentation();
    }
}
