using System.Collections;
using ItemInteraction;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabDoorController : MonoBehaviour
    {
        private const string LookOptionId = "look";
        private const string UseOptionId = "use";
        private static readonly Vector3 DefaultDoorOpenOffset = new Vector3(-1.15f, 0f, 0f);
        private static readonly Vector3 DefaultRightDoorOpenOffset = new Vector3(-1.15f, 0f, 0f);
        private static readonly Vector3 DefaultPromptAnchorLocalOffset = new Vector3(0f, 1.9f, 0f);
        private static readonly Vector3 DefaultLeafLiftOffset = new Vector3(0f, 0.12f, 0f);

        [SerializeField] private InteractableItem interactable;
        [SerializeField] private SelectableOutline outline;
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;
        [SerializeField] private Transform leftDoorLeaf;
        [SerializeField] private Transform rightDoorLeaf;
        [SerializeField] private Transform promptAnchorOverride;
        [SerializeField] private Vector3 leftOpenOffset = new Vector3(-1.15f, 0f, 0f);
        [SerializeField] private Vector3 rightOpenOffset = new Vector3(-1.15f, 0f, 0f);
        [SerializeField] private Vector3 leftLiftOffset = new Vector3(0f, 0.12f, 0f);
        [SerializeField] private Vector3 rightLiftOffset = new Vector3(0f, 0.12f, 0f);
        [SerializeField, Min(0.05f)] private float animationDurationSeconds = 0.45f;
        [SerializeField] private Vector3 promptAnchorLocalOffset = new Vector3(0f, 1.7f, 0f);

        private Vector3 leftClosedLocalPosition;
        private Vector3 rightClosedLocalPosition;
        private Collider[] blockingColliders;
        private bool unlocked;
        private bool open;
        private Coroutine activeAnimation;

        private void Awake()
        {
            ResolveReferences();
            ClearRuntimeStaticFlags();
            ApplyRuntimeDefaults();
            CacheClosedPositions();
            ConfigurePresentation();
            UpdateBlockingColliders();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ClearRuntimeStaticFlags();
            ApplyRuntimeDefaults();
            CacheClosedPositions();
            if (interactable != null)
            {
                interactable.OptionTriggered -= HandleOptionTriggered;
                interactable.OptionTriggered += HandleOptionTriggered;
            }
        }

        private void OnDisable()
        {
            if (interactable != null)
            {
                interactable.OptionTriggered -= HandleOptionTriggered;
            }
        }

        public void SetUnlocked(bool value)
        {
            unlocked = value;
            ConfigurePresentation();
        }

        public void SetOpenImmediate(bool value)
        {
            open = value;
            ApplyDoorPose(value ? 1f : 0f);
            UpdateBlockingColliders();
            ConfigurePresentation();
        }

        public void OpenForCapAssistance()
        {
            SetUnlocked(true);
            if (activeAnimation != null)
            {
                StopCoroutine(activeAnimation);
                activeAnimation = null;
            }

            if (!Application.isPlaying)
            {
                SetOpenImmediate(true);
                return;
            }

            if (open)
            {
                ConfigurePresentation();
                return;
            }

            activeAnimation = StartCoroutine(AnimateDoorRoutine(true));
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactable || !string.Equals(invocation.OptionId, UseOptionId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!unlocked)
            {
                return;
            }

            if (activeAnimation != null)
            {
                StopCoroutine(activeAnimation);
            }

            activeAnimation = StartCoroutine(AnimateDoorRoutine(!open));
        }

        private IEnumerator AnimateDoorRoutine(bool targetOpen)
        {
            var start = open ? 1f : 0f;
            var end = targetOpen ? 1f : 0f;
            var elapsed = 0f;
            while (elapsed < animationDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / animationDurationSeconds);
                ApplyDoorPose(Mathf.Lerp(start, end, t));
                yield return null;
            }

            open = targetOpen;
            ApplyDoorPose(open ? 1f : 0f);
            UpdateBlockingColliders();
            activeAnimation = null;
            ConfigurePresentation();
        }

        private void ApplyDoorPose(float t)
        {
            var eased = Mathf.SmoothStep(0f, 1f, t);
            if (leftDoorLeaf != null)
            {
                var leftOpenPose = leftClosedLocalPosition + ResolveLeafLocalDelta(leftDoorLeaf, leftOpenOffset + leftLiftOffset);
                leftDoorLeaf.localPosition = Vector3.Lerp(leftClosedLocalPosition, leftOpenPose, eased);
            }

            if (rightDoorLeaf != null)
            {
                var rightOpenPose = rightClosedLocalPosition + ResolveLeafLocalDelta(rightDoorLeaf, rightOpenOffset + rightLiftOffset);
                rightDoorLeaf.localPosition = Vector3.Lerp(rightClosedLocalPosition, rightOpenPose, eased);
            }
        }

        private void ConfigurePresentation()
        {
            ResolveReferences();
            ApplyRuntimeDefaults();
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = "Broken Door";
            interactable.storyId = "lab.brokenDoor";
            interactable.promptAnchor = EnsurePromptAnchor();
            interactable.inspectionSourceRoot = transform;
            interactable.outline = outline != null ? outline : interactable.outline;
            interactable.maxFocusDistanceOverride = Mathf.Max(interactable.maxFocusDistanceOverride, 5f);
            interactable.lookDialogueSpeaker = "You";
            interactable.lookDialogueBody = unlocked
                ? (open ? "The door is open now." : "CAP said the door controls are ready now.")
                : "The door is locked until I finish greeting CAP.";
            interactable.isInteractable = true;
            interactable.options.Clear();
            interactable.options.Add(new InteractionOption
            {
                id = LookOptionId,
                label = "Look",
                slot = InteractionOptionSlot.Top,
                visible = true,
                enabled = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = UseOptionId,
                label = unlocked ? (open ? "Close Door" : "Open Door") : "Locked",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = unlocked
            });
        }

        private void ResolveReferences()
        {
            interactable = interactable != null ? interactable : GetComponent<InteractableItem>();
            outline = outline != null ? outline : GetComponent<SelectableOutline>();
            leftDoor = leftDoor != null ? leftDoor : transform.Find("Door1") ?? FindDescendantByName(transform, "Door1");
            rightDoor = rightDoor != null ? rightDoor : transform.Find("Door2") ?? FindDescendantByName(transform, "Door2");
            leftDoorLeaf = leftDoorLeaf != null ? leftDoorLeaf : leftDoor != null ? leftDoor.Find("Top") ?? FindDescendantByName(leftDoor, "Top") : FindDescendantByName(transform, "Top");
            rightDoorLeaf = rightDoorLeaf != null ? rightDoorLeaf : rightDoor != null ? rightDoor.Find("Top 2") ?? FindDescendantByName(rightDoor, "Top 2") : FindDescendantByName(transform, "Top 2");
            blockingColliders = blockingColliders == null || blockingColliders.Length == 0 ? GetComponentsInChildren<Collider>(true) : blockingColliders;
        }

        private void ApplyRuntimeDefaults()
        {
            if (leftDoorLeaf == null && leftDoor != null)
            {
                leftDoorLeaf = leftDoor.Find("Top");
            }

            if (rightDoorLeaf == null && rightDoor != null)
            {
                rightDoorLeaf = rightDoor.Find("Top 2");
            }

            if (leftOpenOffset.magnitude < 1f)
            {
                leftOpenOffset = DefaultDoorOpenOffset;
            }

            if (rightOpenOffset.magnitude < 1f)
            {
                rightOpenOffset = DefaultRightDoorOpenOffset;
            }

            leftOpenOffset = new Vector3(-Mathf.Abs(leftOpenOffset.x), leftOpenOffset.y, leftOpenOffset.z);
            rightOpenOffset = new Vector3(-Mathf.Abs(rightOpenOffset.x), rightOpenOffset.y, rightOpenOffset.z);

            if (leftLiftOffset.sqrMagnitude < 0.0001f)
            {
                leftLiftOffset = DefaultLeafLiftOffset;
            }

            if (rightLiftOffset.sqrMagnitude < 0.0001f)
            {
                rightLiftOffset = DefaultLeafLiftOffset;
            }

            if (promptAnchorLocalOffset.y < DefaultPromptAnchorLocalOffset.y)
            {
                promptAnchorLocalOffset = new Vector3(promptAnchorLocalOffset.x, DefaultPromptAnchorLocalOffset.y, promptAnchorLocalOffset.z);
            }

            if (promptAnchorOverride != null && promptAnchorOverride.parent == transform)
            {
                promptAnchorOverride.localPosition = promptAnchorLocalOffset;
                promptAnchorOverride.localRotation = Quaternion.identity;
            }
        }

        private void CacheClosedPositions()
        {
            if (leftDoorLeaf != null)
            {
                leftClosedLocalPosition = leftDoorLeaf.localPosition;
            }

            if (rightDoorLeaf != null)
            {
                rightClosedLocalPosition = rightDoorLeaf.localPosition;
            }
        }

        private Transform EnsurePromptAnchor()
        {
            if (promptAnchorOverride != null)
            {
                promptAnchorOverride.localPosition = promptAnchorLocalOffset;
                return promptAnchorOverride;
            }

            var existingAnchor = transform.Find("PromptAnchor");
            if (existingAnchor == null)
            {
                var anchorObject = new GameObject("PromptAnchor");
                anchorObject.transform.SetParent(transform, false);
                existingAnchor = anchorObject.transform;
            }

            existingAnchor.localPosition = promptAnchorLocalOffset;
            existingAnchor.localRotation = Quaternion.identity;
            promptAnchorOverride = existingAnchor;
            return promptAnchorOverride;
        }

        private void UpdateBlockingColliders()
        {
            if (blockingColliders == null)
            {
                return;
            }

            for (var index = 0; index < blockingColliders.Length; index++)
            {
                var collider = blockingColliders[index];
                if (collider == null)
                {
                    continue;
                }

                if (collider.isTrigger)
                {
                    continue;
                }

                collider.enabled = !open;
            }

            if (interactable != null)
            {
                interactable.isInteractable = true;
            }
        }

        private static Vector3 ResolveLeafLocalDelta(Transform leaf, Vector3 leafLocalOffset)
        {
            if (leaf == null)
            {
                return Vector3.zero;
            }

            var parent = leaf.parent;
            if (parent == null)
            {
                return leafLocalOffset;
            }

            var worldOffset = leaf.TransformDirection(leafLocalOffset);
            return parent.InverseTransformDirection(worldOffset);
        }

        private static Transform FindDescendantByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (string.Equals(child.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                var nested = FindDescendantByName(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private void ClearRuntimeStaticFlags()
        {
            SetNonStatic(transform);
            SetNonStatic(leftDoor);
            SetNonStatic(rightDoor);
            SetNonStatic(leftDoorLeaf);
            SetNonStatic(rightDoorLeaf);
        }

        private static void SetNonStatic(Transform target)
        {
            if (target == null)
            {
                return;
            }

            target.gameObject.isStatic = false;
        }
    }
}
