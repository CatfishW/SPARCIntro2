using ItemInteraction;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabSceneContext : MonoBehaviour
    {
        [SerializeField] private StoryFlowPlayer storyFlowPlayer;
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private StoryNpcAgent capNpc;
        [SerializeField] private InteractableItem bodyInteractable;
        [SerializeField] private InteractableItem dnaMachineInteractable;
        [SerializeField] private InteractableItem rocketInteractable;
        [SerializeField] private Transform bodyPreviewAnchor;
        [SerializeField] private Transform shrinkPlayerAnchor;
        [SerializeField] private Transform rocketFocusAnchor;
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private LabCapNpcController capNpcController;
        [SerializeField] private LabCapConversationDirector capConversationDirector;
        [SerializeField] private LabBodyInspectionUi bodyInspectionUi;
        [SerializeField] private LabLightPuzzleUi lightPuzzleUi;
        [SerializeField] private LabShrinkSequenceController shrinkSequenceController;
        [SerializeField] private LabFinalCutsceneController finalCutsceneController;
        [SerializeField] private LabDoorController doorController;
        [SerializeField] private LabObjectivePanelUi objectivePanelUi;
        [SerializeField] private LabCameraFocusController cameraFocusController;
        [SerializeField] private bool startupCooldownActive;

        public StoryFlowPlayer StoryFlowPlayer => storyFlowPlayer;
        public StoryNpcRegistry NpcRegistry => npcRegistry;
        public StoryNpcAgent CapNpc => capNpc;
        public InteractableItem BodyInteractable => bodyInteractable;
        public InteractableItem DnaMachineInteractable => dnaMachineInteractable;
        public InteractableItem RocketInteractable => rocketInteractable;
        public Transform BodyPreviewAnchor => bodyPreviewAnchor;
        public Transform ShrinkPlayerAnchor => shrinkPlayerAnchor;
        public Transform RocketFocusAnchor => rocketFocusAnchor;
        public ClassroomPlayerControlLock ControlLock => controlLock;
        public LabCapNpcController CapNpcController => capNpcController;
        public LabCapConversationDirector CapConversationDirector => capConversationDirector;
        public LabBodyInspectionUi BodyInspectionUi => bodyInspectionUi;
        public LabLightPuzzleUi LightPuzzleUi => lightPuzzleUi;
        public LabShrinkSequenceController ShrinkSequenceController => shrinkSequenceController;
        public LabFinalCutsceneController FinalCutsceneController => finalCutsceneController;
        public LabDoorController DoorController => doorController;
        public LabObjectivePanelUi ObjectivePanelUi => objectivePanelUi;
        public LabCameraFocusController CameraFocusController => cameraFocusController;
        public bool StartupCooldownActive => startupCooldownActive;

        private void Awake()
        {
            ResolveRuntimeReferences();
        }

        public void ResolveRuntimeReferences()
        {
            storyFlowPlayer = storyFlowPlayer != null ? storyFlowPlayer : FindFirstObjectByType<StoryFlowPlayer>(FindObjectsInactive.Include);
            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>(FindObjectsInactive.Include);
            capNpc = capNpc != null ? capNpc : ResolveNpc();

            bodyInteractable = ResolveInteractable(bodyInteractable, "lab.bodyModel", "Body Model");
            dnaMachineInteractable = ResolveInteractable(dnaMachineInteractable, "lab.shrinkMachine", "Shrink Machine");
            rocketInteractable = ResolveInteractable(rocketInteractable, "lab.rocket", "Mini Rocket");

            controlLock = controlLock != null ? controlLock : FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);

            capNpcController = capNpcController != null ? capNpcController : FindFirstObjectByType<LabCapNpcController>(FindObjectsInactive.Include);
            capConversationDirector = capConversationDirector != null ? capConversationDirector : FindFirstObjectByType<LabCapConversationDirector>(FindObjectsInactive.Include);
            bodyInspectionUi = bodyInspectionUi != null ? bodyInspectionUi : FindFirstObjectByType<LabBodyInspectionUi>(FindObjectsInactive.Include);
            lightPuzzleUi = ResolveLightPuzzleUi(lightPuzzleUi);
            shrinkSequenceController = shrinkSequenceController != null ? shrinkSequenceController : FindFirstObjectByType<LabShrinkSequenceController>(FindObjectsInactive.Include);
            finalCutsceneController = finalCutsceneController != null ? finalCutsceneController : FindFirstObjectByType<LabFinalCutsceneController>(FindObjectsInactive.Include);
            doorController = doorController != null ? doorController : FindFirstObjectByType<LabDoorController>(FindObjectsInactive.Include);
            objectivePanelUi = objectivePanelUi != null ? objectivePanelUi : FindFirstObjectByType<LabObjectivePanelUi>(FindObjectsInactive.Include);
            cameraFocusController = cameraFocusController != null ? cameraFocusController : FindFirstObjectByType<LabCameraFocusController>(FindObjectsInactive.Include);

            if (bodyPreviewAnchor == null && bodyInteractable != null)
            {
                bodyPreviewAnchor = bodyInteractable.transform;
            }

            if (rocketFocusAnchor == null && rocketInteractable != null)
            {
                rocketFocusAnchor = rocketInteractable.transform;
            }
        }

        private static LabLightPuzzleUi ResolveLightPuzzleUi(LabLightPuzzleUi existing)
        {
            if (IsPreferredLightPuzzleUi(existing))
            {
                return existing;
            }

            var candidates = FindObjectsByType<LabLightPuzzleUi>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            LabLightPuzzleUi fallback = null;
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                {
                    continue;
                }

                if (IsPreferredLightPuzzleUi(candidate))
                {
                    return candidate;
                }

                fallback ??= candidate;
            }

            return fallback;
        }

        private static bool IsPreferredLightPuzzleUi(LabLightPuzzleUi candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            var document = candidate.GetComponent<UIDocument>();
            if (document == null)
            {
                return false;
            }

            return string.Equals(candidate.gameObject.name, "LabLightPuzzleUiRoot", System.StringComparison.OrdinalIgnoreCase);
        }

        private StoryNpcAgent ResolveNpc()
        {
            if (npcRegistry != null)
            {
                var registeredCap = npcRegistry.GetNpc("cap");
                if (registeredCap != null)
                {
                    return registeredCap;
                }
            }

            var npcs = FindObjectsByType<StoryNpcAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < npcs.Length; index++)
            {
                var candidate = npcs[index];
                if (candidate == null)
                {
                    continue;
                }

                var name = candidate.gameObject.name;
                if (name.IndexOf("CAP", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("MO_", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static InteractableItem ResolveInteractable(InteractableItem existing, string storyIdHint, string nameHint)
        {
            if (IsMatchingInteractable(existing, storyIdHint, nameHint))
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(storyIdHint) && string.IsNullOrWhiteSpace(nameHint))
            {
                return null;
            }

            var interactables = FindObjectsByType<InteractableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var index = 0; index < interactables.Length; index++)
            {
                var candidate = interactables[index];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(storyIdHint) &&
                    string.Equals(candidate.storyId, storyIdHint, System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            for (var index = 0; index < interactables.Length; index++)
            {
                var candidate = interactables[index];
                if (candidate == null)
                {
                    continue;
                }

                var candidateName = candidate.gameObject.name;
                if (!string.IsNullOrWhiteSpace(nameHint) &&
                    (string.Equals(candidateName, nameHint, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.displayName, nameHint, System.StringComparison.OrdinalIgnoreCase) ||
                     candidateName.IndexOf(nameHint, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     candidate.displayName.IndexOf(nameHint, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsMatchingInteractable(InteractableItem candidate, string storyIdHint, string nameHint)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(storyIdHint) &&
                string.Equals(candidate.storyId, storyIdHint, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(nameHint) &&
                (string.Equals(candidate.displayName, nameHint, System.StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.gameObject.name, nameHint, System.StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        public void SetStartupCooldownActive(bool value)
        {
            startupCooldownActive = value;
        }
    }
}
