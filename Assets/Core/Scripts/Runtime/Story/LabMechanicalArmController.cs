using System.Collections;
using ItemInteraction;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InteractableItem))]
    public sealed class LabMechanicalArmController : MonoBehaviour
    {
        private const string LookOptionId = "look";
        private const string InspectOptionId = "inspect";
        private const string ActivateOptionId = "activate";

        [SerializeField] private InteractableItem interactable;
        [SerializeField] private SelectableOutline outline;
        [SerializeField] private Transform promptAnchor;
        [SerializeField] private Transform basePivot;
        [SerializeField] private Transform shoulderPivot;
        [SerializeField] private Transform forearmPivot;
        [SerializeField, Min(0.1f)] private float maxFocusDistance = 4.8f;
        [SerializeField, Min(0.1f)] private float activateDurationSeconds = 0.48f;
        [SerializeField] private Vector3 baseActivateEuler = new Vector3(0f, 18f, 0f);
        [SerializeField] private Vector3 shoulderActivateEuler = new Vector3(-16f, 0f, 0f);
        [SerializeField] private Vector3 forearmActivateEuler = new Vector3(24f, 0f, 0f);

        private Coroutine activationRoutine;
        private Quaternion baseLocalRotation;
        private Quaternion shoulderLocalRotation;
        private Quaternion forearmLocalRotation;

        private void Awake()
        {
            ResolveReferences();
            CacheRestPose();
            ConfigurePresentation();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheRestPose();
            ConfigurePresentation();
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

        private void OnValidate()
        {
            ResolveReferences();
            CacheRestPose();
            ConfigurePresentation();
        }

        private void ConfigurePresentation()
        {
            ResolveReferences();
            if (interactable == null)
            {
                return;
            }

            interactable.displayName = "Robotic Arm";
            interactable.storyId = "lab.roboticArm";
            interactable.promptAnchor = promptAnchor != null ? promptAnchor : transform;
            interactable.inspectionSourceRoot = transform;
            interactable.outline = outline != null ? outline : interactable.outline;
            interactable.maxFocusDistanceOverride = Mathf.Max(interactable.maxFocusDistanceOverride, maxFocusDistance);
            interactable.lookDialogueSpeaker = "You";
            interactable.lookDialogueBody = "A precise robotic arm for careful lab work. It looks ready for a small demo.";
            interactable.lookDialogueDisplayDurationSeconds = 2.4f;
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
                id = InspectOptionId,
                label = "Inspect",
                slot = InteractionOptionSlot.Right,
                visible = true,
                enabled = true,
                opensInspection = true
            });
            interactable.options.Add(new InteractionOption
            {
                id = ActivateOptionId,
                label = "Activate",
                slot = InteractionOptionSlot.Bottom,
                visible = true,
                enabled = true
            });
        }

        private void HandleOptionTriggered(InteractionInvocation invocation)
        {
            if (invocation == null || invocation.Target != interactable)
            {
                return;
            }

            if (!string.Equals(invocation.OptionId, ActivateOptionId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (activationRoutine != null)
            {
                StopCoroutine(activationRoutine);
            }

            if (!Application.isPlaying)
            {
                CacheRestPose();
                ApplyPose(1f);
                return;
            }

            activationRoutine = StartCoroutine(PlayActivationRoutine());
        }

        private IEnumerator PlayActivationRoutine()
        {
            CacheRestPose();
            var upDuration = Mathf.Max(0.12f, activateDurationSeconds * 0.52f);
            var downDuration = Mathf.Max(0.12f, activateDurationSeconds * 0.48f);

            yield return RotateRoutine(0f, 1f, upDuration);
            yield return RotateRoutine(1f, 0f, downDuration);
            ApplyPose(0f);
            activationRoutine = null;
        }

        private IEnumerator RotateRoutine(float start, float end, float duration)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = Mathf.SmoothStep(start, end, t);
                ApplyPose(eased);
                yield return null;
            }

            ApplyPose(end);
        }

        private void ApplyPose(float amount)
        {
            if (basePivot != null)
            {
                basePivot.localRotation = baseLocalRotation * Quaternion.Euler(baseActivateEuler * amount);
            }

            if (shoulderPivot != null)
            {
                shoulderPivot.localRotation = shoulderLocalRotation * Quaternion.Euler(shoulderActivateEuler * amount);
            }

            if (forearmPivot != null)
            {
                forearmPivot.localRotation = forearmLocalRotation * Quaternion.Euler(forearmActivateEuler * amount);
            }
        }

        private void ResolveReferences()
        {
            interactable = interactable != null ? interactable : GetComponent<InteractableItem>();
            outline = outline != null ? outline : GetComponent<SelectableOutline>();
            promptAnchor = promptAnchor != null ? promptAnchor : EnsurePromptAnchor();
            basePivot = basePivot != null ? basePivot : transform.Find("Mechanical Arm 1/Base") ?? transform.Find("Base");
            shoulderPivot = shoulderPivot != null ? shoulderPivot : transform.Find("Mechanical Arm 1/Point005") ?? transform.Find("Point005");
            forearmPivot = forearmPivot != null ? forearmPivot : transform.Find("Mechanical Arm 1/Point005/Point001/Point004/Point002/Point003")
                ?? transform.Find("Point005/Point001/Point004/Point002/Point003");
        }

        private void CacheRestPose()
        {
            if (basePivot != null)
            {
                baseLocalRotation = basePivot.localRotation;
            }

            if (shoulderPivot != null)
            {
                shoulderLocalRotation = shoulderPivot.localRotation;
            }

            if (forearmPivot != null)
            {
                forearmLocalRotation = forearmPivot.localRotation;
            }
        }

        private Transform EnsurePromptAnchor()
        {
            var existing = transform.Find("PromptAnchor");
            if (existing != null)
            {
                existing.localPosition = new Vector3(0f, 1.35f, 0.18f);
                return existing;
            }

            var anchorObject = new GameObject("PromptAnchor");
            var anchor = anchorObject.transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = new Vector3(0f, 1.35f, 0.18f);
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }
    }
}
