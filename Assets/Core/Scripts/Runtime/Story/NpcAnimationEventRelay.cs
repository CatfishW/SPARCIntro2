using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class NpcAnimationEventRelay : MonoBehaviour
    {
        public void OnFootstepWalk()
        {
            // Intentionally left blank: absorbs shared player-controller footstep events for NPCs.
        }

        public void OnFootstepRun()
        {
            // Intentionally left blank.
        }

        public void OnLand()
        {
            // Intentionally left blank.
        }
    }
}
