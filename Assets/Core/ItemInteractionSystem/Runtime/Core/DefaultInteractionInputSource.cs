using UnityEngine;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    public class DefaultInteractionInputSource : InteractionInputSource
    {
        [Header("Prompt Slots")]
        public KeyCode topKey = KeyCode.Alpha1;
        public KeyCode leftKey = KeyCode.Alpha2;
        public KeyCode rightKey = KeyCode.Alpha3;
        public KeyCode bottomKey = KeyCode.Alpha4;

        [Header("Inspection")]
        public KeyCode closeKey = KeyCode.Escape;
        public KeyCode rotateWithMouseButton = KeyCode.Mouse0;
        [Min(0.01f)] public float rotationSensitivity = 120f;
        [Min(0.01f)] public float zoomSensitivity = 0.15f;
        public bool invertY = false;

        [Header("Hint Labels")]
        public string topHint = "1";
        public string leftHint = "2";
        public string rightHint = "3";
        public string bottomHint = "4";

        public override bool TryGetTriggeredSlot(out InteractionOptionSlot slot)
        {
            if (Input.GetKeyDown(topKey))
            {
                slot = InteractionOptionSlot.Top;
                return true;
            }

            if (Input.GetKeyDown(leftKey))
            {
                slot = InteractionOptionSlot.Left;
                return true;
            }

            if (Input.GetKeyDown(rightKey))
            {
                slot = InteractionOptionSlot.Right;
                return true;
            }

            if (Input.GetKeyDown(bottomKey))
            {
                slot = InteractionOptionSlot.Bottom;
                return true;
            }

            slot = default;
            return false;
        }

        public override bool GetCloseRequested()
        {
            return Input.GetKeyDown(closeKey) || Input.GetMouseButtonDown(1);
        }

        public override bool GetInspectionInteractHeld()
        {
            return Input.GetMouseButton(0) || Input.GetKey(rotateWithMouseButton);
        }

        public override Vector2 GetInspectionRotateDelta()
        {
            if (!GetInspectionInteractHeld())
            {
                return Vector2.zero;
            }

            var ySign = invertY ? 1f : -1f;
            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y") * ySign;
            return new Vector2(mx, my) * (rotationSensitivity * 0.05f);
        }

        public override float GetInspectionZoomDelta()
        {
            return -Input.mouseScrollDelta.y * zoomSensitivity;
        }

        public override string GetSlotHint(InteractionOptionSlot slot)
        {
            switch (slot)
            {
                case InteractionOptionSlot.Top:
                    return topHint;
                case InteractionOptionSlot.Left:
                    return leftHint;
                case InteractionOptionSlot.Right:
                    return rightHint;
                case InteractionOptionSlot.Bottom:
                    return bottomHint;
                default:
                    return string.Empty;
            }
        }
    }
}

