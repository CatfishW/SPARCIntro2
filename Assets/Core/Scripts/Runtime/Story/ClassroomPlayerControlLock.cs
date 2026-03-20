using Blocks.Gameplay.Core;
using ItemInteraction;
using UnityEngine;
using UnityEngine.UIElements;

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
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>();

            if (localPlayerManager == null)
            {
                var players = FindObjectsByType<CorePlayerManager>(FindObjectsSortMode.None);
                for (var index = 0; index < players.Length; index++)
                {
                    var candidate = players[index];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (candidate.IsOwner)
                    {
                        localPlayerManager = candidate;
                        break;
                    }

                    if (localPlayerManager == null)
                    {
                        localPlayerManager = candidate;
                    }
                }
            }

            if (localHud == null)
            {
                var huds = FindObjectsByType<CoreHUD>(FindObjectsSortMode.None);
                for (var index = 0; index < huds.Length; index++)
                {
                    var candidate = huds[index];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (candidate.IsOwner)
                    {
                        localHud = candidate;
                        break;
                    }

                    if (localHud == null)
                    {
                        localHud = candidate;
                    }
                }
            }

            if (localHud != null && hudDocument == null)
            {
                hudDocument = localHud.GetComponent<UIDocument>();
            }
        }
    }
}
