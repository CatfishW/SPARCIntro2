using Blocks.Gameplay.Core;
using ItemInteraction;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomPlayerControlLock : MonoBehaviour
    {
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private CoreHUD localHud;

        private int lockDepth;
        private bool cachedCursorState;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;
        private bool cachedCoreInputEnabled;
        private bool cachedCoreCameraEnabled;
        private bool cachedHudEnabled;
        private bool cachedHudDocumentEnabled;
        private UIDocument hudDocument;

        public bool IsLocked => lockDepth > 0;

        public void Acquire(bool unlockCursor)
        {
            ResolveRuntimeReferences();
            if (lockDepth == 0)
            {
                CachePreviousState();
                ApplyLockedState(unlockCursor);
            }

            lockDepth++;
        }

        public void Release()
        {
            if (lockDepth <= 0)
            {
                return;
            }

            lockDepth--;
            if (lockDepth > 0)
            {
                return;
            }

            RestorePreviousState();
        }

        public void ForceReleaseAll()
        {
            lockDepth = 0;
            RestorePreviousState();
        }

        private void CachePreviousState()
        {
            ResolveRuntimeReferences();

            previousCursorLockMode = UnityEngine.Cursor.lockState;
            previousCursorVisible = UnityEngine.Cursor.visible;
            cachedCursorState = true;

            cachedCoreInputEnabled = localPlayerManager != null && localPlayerManager.CoreInput != null && localPlayerManager.CoreInput.enabled;
            cachedCoreCameraEnabled = localPlayerManager != null && localPlayerManager.CoreCamera != null && localPlayerManager.CoreCamera.enabled;
            cachedHudEnabled = localHud != null && localHud.enabled;

            if (localHud != null)
            {
                hudDocument = localHud.GetComponent<UIDocument>();
                cachedHudDocumentEnabled = hudDocument != null && hudDocument.enabled;
            }
            else
            {
                hudDocument = null;
                cachedHudDocumentEnabled = false;
            }
        }

        private void ApplyLockedState(bool unlockCursor)
        {
            interactionDirector?.SetInteractionsLocked(true);

            if (localPlayerManager != null)
            {
                localPlayerManager.SetMovementInputEnabled(false);
                if (localPlayerManager.CoreInput != null)
                {
                    localPlayerManager.CoreInput.enabled = false;
                }

                if (localPlayerManager.CoreCamera != null)
                {
                    localPlayerManager.CoreCamera.enabled = false;
                }
            }

            if (localHud != null)
            {
                localHud.enabled = false;
            }

            if (hudDocument != null)
            {
                hudDocument.enabled = false;
            }

            if (unlockCursor)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
        }

        private void RestorePreviousState()
        {
            interactionDirector?.SetInteractionsLocked(false);

            if (localPlayerManager != null)
            {
                if (localPlayerManager.CoreInput != null)
                {
                    localPlayerManager.CoreInput.enabled = cachedCoreInputEnabled;
                }

                if (localPlayerManager.CoreCamera != null)
                {
                    localPlayerManager.CoreCamera.enabled = cachedCoreCameraEnabled;
                }

                localPlayerManager.SetMovementInputEnabled(true);
            }

            if (localHud != null)
            {
                localHud.enabled = cachedHudEnabled;
            }

            if (hudDocument != null)
            {
                hudDocument.enabled = cachedHudDocumentEnabled;
            }

            if (cachedCursorState)
            {
                UnityEngine.Cursor.lockState = previousCursorLockMode;
                UnityEngine.Cursor.visible = previousCursorVisible;
                cachedCursorState = false;
            }
        }

        private void ResolveRuntimeReferences()
        {
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (interactionDirector != null && interactionDirector.gameObject.scene != activeScene)
            {
                interactionDirector = null;
            }

            interactionDirector = interactionDirector != null ? interactionDirector : FindInteractionDirector(activeScene);

            if (!IsValidPlayerManager(localPlayerManager, activeScene))
            {
                localPlayerManager = null;
            }

            if (localPlayerManager == null)
            {
                if (StorySceneLocalPlayerSpawner.TryResolveSceneLocalPlayerMovement(activeScene, out var movement) && movement != null)
                {
                    localPlayerManager = movement.GetComponentInParent<CorePlayerManager>() ??
                                         movement.GetComponent<CorePlayerManager>();
                }
            }

            if (localPlayerManager == null)
            {
                CorePlayerManager ownerInActiveScene = null;
                CorePlayerManager firstInActiveScene = null;
                CorePlayerManager ownerAnywhere = null;
                CorePlayerManager firstAnywhere = null;

                var players = FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < players.Length; index++)
                {
                    var candidate = players[index];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (firstAnywhere == null)
                    {
                        firstAnywhere = candidate;
                    }

                    var candidateScene = candidate.gameObject.scene;
                    var inActiveScene = candidateScene.IsValid() && candidateScene == activeScene;
                    if (inActiveScene)
                    {
                        if (firstInActiveScene == null)
                        {
                            firstInActiveScene = candidate;
                        }

                        if (candidate.IsOwner)
                        {
                            ownerInActiveScene = candidate;
                            break;
                        }
                    }

                    if (candidate.IsOwner && ownerAnywhere == null)
                    {
                        ownerAnywhere = candidate;
                    }
                }

                localPlayerManager = ownerInActiveScene ?? firstInActiveScene ?? ownerAnywhere ?? firstAnywhere;
            }

            if (!IsValidHud(localHud, activeScene))
            {
                localHud = null;
                hudDocument = null;
            }

            if (localHud == null)
            {
                CoreHUD ownerInActiveScene = null;
                CoreHUD firstInActiveScene = null;
                CoreHUD ownerAnywhere = null;
                CoreHUD firstAnywhere = null;

                var huds = FindObjectsByType<CoreHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < huds.Length; index++)
                {
                    var candidate = huds[index];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (firstAnywhere == null)
                    {
                        firstAnywhere = candidate;
                    }

                    var candidateScene = candidate.gameObject.scene;
                    var inActiveScene = candidateScene.IsValid() && candidateScene == activeScene;
                    if (inActiveScene)
                    {
                        if (firstInActiveScene == null)
                        {
                            firstInActiveScene = candidate;
                        }

                        if (candidate.IsOwner)
                        {
                            ownerInActiveScene = candidate;
                            break;
                        }
                    }

                    if (candidate.IsOwner && ownerAnywhere == null)
                    {
                        ownerAnywhere = candidate;
                    }
                }

                localHud = ownerInActiveScene ?? firstInActiveScene ?? ownerAnywhere ?? firstAnywhere;
            }

            if (localHud != null && hudDocument == null)
            {
                hudDocument = localHud.GetComponent<UIDocument>();
            }
        }

        private static InteractionDirector FindInteractionDirector(Scene activeScene)
        {
            InteractionDirector fallback = null;
            var directors = FindObjectsByType<InteractionDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < directors.Length; index++)
            {
                var candidate = directors[index];
                if (candidate == null)
                {
                    continue;
                }

                fallback ??= candidate;
                if (candidate.gameObject.scene == activeScene)
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private static bool IsValidPlayerManager(CorePlayerManager candidate, Scene activeScene)
        {
            if (candidate == null)
            {
                return false;
            }

            var scene = candidate.gameObject.scene;
            return scene.IsValid() && scene == activeScene;
        }

        private static bool IsValidHud(CoreHUD candidate, Scene activeScene)
        {
            if (candidate == null)
            {
                return false;
            }

            var scene = candidate.gameObject.scene;
            return scene.IsValid() && scene == activeScene;
        }
    }
}
