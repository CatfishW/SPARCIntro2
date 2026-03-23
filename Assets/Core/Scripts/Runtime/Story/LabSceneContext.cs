using ItemInteraction;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();

            if (!IsInScene(storyFlowPlayer, activeScene))
            {
                storyFlowPlayer = null;
            }

            if (!IsInScene(npcRegistry, activeScene))
            {
                npcRegistry = null;
            }

            if (!IsInScene(capNpc, activeScene))
            {
                capNpc = null;
            }

            if (!IsInScene(controlLock, activeScene))
            {
                controlLock = null;
            }

            if (!IsInScene(capNpcController, activeScene))
            {
                capNpcController = null;
            }

            if (!IsInScene(capConversationDirector, activeScene))
            {
                capConversationDirector = null;
            }

            if (!IsInScene(bodyInspectionUi, activeScene))
            {
                bodyInspectionUi = null;
            }

            if (!IsInScene(lightPuzzleUi, activeScene))
            {
                lightPuzzleUi = null;
            }

            if (!IsInScene(shrinkSequenceController, activeScene))
            {
                shrinkSequenceController = null;
            }

            if (!IsInScene(finalCutsceneController, activeScene))
            {
                finalCutsceneController = null;
            }

            if (!IsInScene(doorController, activeScene))
            {
                doorController = null;
            }

            if (!IsInScene(objectivePanelUi, activeScene))
            {
                objectivePanelUi = null;
            }

            if (!IsInScene(cameraFocusController, activeScene))
            {
                cameraFocusController = null;
            }

            storyFlowPlayer = storyFlowPlayer != null ? storyFlowPlayer : FindSceneObject<StoryFlowPlayer>(activeScene);
            npcRegistry = npcRegistry != null ? npcRegistry : FindSceneObject<StoryNpcRegistry>(activeScene);
            capNpc = capNpc != null ? capNpc : ResolveNpc(activeScene);

            bodyInteractable = ResolveInteractable(bodyInteractable, "lab.bodyModel", "Body Model", activeScene);
            dnaMachineInteractable = ResolveInteractable(dnaMachineInteractable, "lab.shrinkMachine", "Shrink Machine", activeScene);
            rocketInteractable = ResolveInteractable(rocketInteractable, "lab.rocket", "Mini Rocket", activeScene);

            controlLock = controlLock != null ? controlLock : FindSceneObject<ClassroomPlayerControlLock>(activeScene);

            capNpcController = capNpcController != null ? capNpcController : FindSceneObject<LabCapNpcController>(activeScene);
            capConversationDirector = capConversationDirector != null ? capConversationDirector : FindSceneObject<LabCapConversationDirector>(activeScene);
            bodyInspectionUi = bodyInspectionUi != null ? bodyInspectionUi : FindSceneObject<LabBodyInspectionUi>(activeScene);
            lightPuzzleUi = ResolveLightPuzzleUi(lightPuzzleUi, activeScene);
            shrinkSequenceController = shrinkSequenceController != null ? shrinkSequenceController : FindSceneObject<LabShrinkSequenceController>(activeScene);
            finalCutsceneController = finalCutsceneController != null ? finalCutsceneController : FindSceneObject<LabFinalCutsceneController>(activeScene);
            doorController = doorController != null ? doorController : FindSceneObject<LabDoorController>(activeScene);
            objectivePanelUi = objectivePanelUi != null ? objectivePanelUi : FindSceneObject<LabObjectivePanelUi>(activeScene);
            cameraFocusController = cameraFocusController != null ? cameraFocusController : FindSceneObject<LabCameraFocusController>(activeScene);

            if (bodyPreviewAnchor == null && bodyInteractable != null)
            {
                bodyPreviewAnchor = bodyInteractable.transform;
            }

            if (rocketFocusAnchor == null && rocketInteractable != null)
            {
                rocketFocusAnchor = rocketInteractable.transform;
            }
        }

        private static LabLightPuzzleUi ResolveLightPuzzleUi(LabLightPuzzleUi existing, Scene activeScene)
        {
            if (IsPreferredLightPuzzleUi(existing, activeScene))
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

                if (IsPreferredLightPuzzleUi(candidate, activeScene))
                {
                    return candidate;
                }

                fallback ??= candidate;
            }

            return fallback;
        }

        private static bool IsPreferredLightPuzzleUi(LabLightPuzzleUi candidate, Scene activeScene)
        {
            if (candidate == null)
            {
                return false;
            }

            if (activeScene.IsValid() && candidate.gameObject.scene != activeScene)
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

        private StoryNpcAgent ResolveNpc(Scene activeScene)
        {
            if (npcRegistry != null)
            {
                var registeredCap = npcRegistry.GetNpc("cap");
                if (registeredCap != null && (!activeScene.IsValid() || registeredCap.gameObject.scene == activeScene))
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

                if (activeScene.IsValid() && candidate.gameObject.scene != activeScene)
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

        private static InteractableItem ResolveInteractable(InteractableItem existing, string storyIdHint, string nameHint, Scene activeScene)
        {
            if (IsMatchingInteractable(existing, storyIdHint, nameHint, activeScene))
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

                if (activeScene.IsValid() && candidate.gameObject.scene != activeScene)
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

                if (activeScene.IsValid() && candidate.gameObject.scene != activeScene)
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

        private static bool IsMatchingInteractable(InteractableItem candidate, string storyIdHint, string nameHint, Scene activeScene)
        {
            if (candidate == null)
            {
                return false;
            }

            if (activeScene.IsValid() && candidate.gameObject.scene != activeScene)
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

        private static bool IsInScene(Component component, Scene scene)
        {
            if (component == null)
            {
                return false;
            }

            var componentScene = component.gameObject.scene;
            if (!scene.IsValid())
            {
                return componentScene.IsValid();
            }

            return componentScene.IsValid() && componentScene == scene;
        }

        private static T FindSceneObject<T>(Scene scene)
            where T : Component
        {
            T fallback = null;
            var candidates = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                {
                    continue;
                }

                fallback ??= candidate;
                if (!scene.IsValid() || candidate.gameObject.scene == scene)
                {
                    return candidate;
                }
            }

            return fallback;
        }
    }
}
