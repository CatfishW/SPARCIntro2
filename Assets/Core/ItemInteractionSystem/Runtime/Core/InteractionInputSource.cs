using UnityEngine;

namespace ItemInteraction
{
    public abstract class InteractionInputSource : MonoBehaviour
    {
        public abstract bool TryGetTriggeredSlot(out InteractionOptionSlot slot);
        public abstract bool GetCloseRequested();
        public abstract bool GetInspectionInteractHeld();
        public abstract Vector2 GetInspectionRotateDelta();
        public abstract float GetInspectionZoomDelta();
        public abstract string GetSlotHint(InteractionOptionSlot slot);
    }
}
