using Blocks.Gameplay.Core;
using Unity.Cinemachine;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStoryConversationPresentationController : MonoBehaviour
    {
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField, Min(0.1f)] private float cameraBlendSpeed = 7f;
        [SerializeField] private Vector3 cameraSideOffset = new Vector3(-1.05f, 1.26f, 1.42f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.12f, 0f);

        private bool conversationActive;
        private bool cachedBrainState;
        private Transform currentSpeaker;
        private CinemachineBrain cinemachineBrain;
        private Vector3 targetCameraPosition;
        private Quaternion targetCameraRotation;

        public void BeginConversation()
        {
            ResolveRuntimeReferences();
            if (conversationActive)
            {
                return;
            }

            conversationActive = true;
            controlLock?.Acquire(unlockCursor: true);

            if (gameplayCamera == null)
            {
                return;
            }

            cinemachineBrain = gameplayCamera.GetComponent<CinemachineBrain>();
            if (cinemachineBrain != null)
            {
                cachedBrainState = cinemachineBrain.enabled;
                cinemachineBrain.enabled = false;
            }

            targetCameraPosition = gameplayCamera.transform.position;
            targetCameraRotation = gameplayCamera.transform.rotation;
        }

        public void EndConversation()
        {
            if (!conversationActive)
            {
                return;
            }

            conversationActive = false;
            currentSpeaker = null;
            controlLock?.Release();

            if (cinemachineBrain != null)
            {
                cinemachineBrain.enabled = cachedBrainState;
            }

            cinemachineBrain = null;
        }

        public void FocusOnSpeaker(string speakerDisplayName)
        {
            if (!conversationActive)
            {
                return;
            }

            ResolveRuntimeReferences();
            currentSpeaker = ResolveSpeakerTransform(speakerDisplayName);
            if (currentSpeaker == null || gameplayCamera == null)
            {
                return;
            }

            var speakerPosition = currentSpeaker.position;
            var speakerForward = currentSpeaker.forward;
            if (speakerForward.sqrMagnitude < 0.0001f)
            {
                speakerForward = Vector3.forward;
            }

            var speakerRight = Vector3.Cross(Vector3.up, speakerForward).normalized;
            if (speakerRight.sqrMagnitude < 0.0001f)
            {
                speakerRight = currentSpeaker.right;
            }

            targetCameraPosition = speakerPosition
                + (speakerRight * cameraSideOffset.x)
                + (Vector3.up * cameraSideOffset.y)
                + (speakerForward * cameraSideOffset.z);

            var lookPoint = speakerPosition + lookOffset;
            var lookDirection = (lookPoint - targetCameraPosition).normalized;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                lookDirection = gameplayCamera.transform.forward;
            }

            targetCameraRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        private void LateUpdate()
        {
            if (!conversationActive || gameplayCamera == null)
            {
                return;
            }

            var delta = Mathf.Max(0.01f, cameraBlendSpeed) * Time.unscaledDeltaTime;
            gameplayCamera.transform.position = Vector3.Lerp(gameplayCamera.transform.position, targetCameraPosition, delta);
            gameplayCamera.transform.rotation = Quaternion.Slerp(gameplayCamera.transform.rotation, targetCameraRotation, delta);
        }

        private Transform ResolveSpeakerTransform(string speakerDisplayName)
        {
            if (string.IsNullOrWhiteSpace(speakerDisplayName))
            {
                return localPlayerManager != null ? localPlayerManager.transform : null;
            }

            if (speakerDisplayName.Equals("You", System.StringComparison.OrdinalIgnoreCase))
            {
                return localPlayerManager != null ? localPlayerManager.transform : null;
            }

            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>();
            if (npcRegistry == null)
            {
                return null;
            }

            var npcs = npcRegistry.Npcs;
            for (var index = 0; index < npcs.Count; index++)
            {
                var candidate = npcs[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.NpcDisplayName.Equals(speakerDisplayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.transform;
                }
            }

            return null;
        }

        private void ResolveRuntimeReferences()
        {
            controlLock = controlLock != null ? controlLock : GetComponent<ClassroomPlayerControlLock>();
            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>();
            gameplayCamera = gameplayCamera != null ? gameplayCamera : Camera.main;

            if (localPlayerManager != null)
            {
                return;
            }

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
                    return;
                }

                if (localPlayerManager == null)
                {
                    localPlayerManager = candidate;
                }
            }
        }
    }
}
