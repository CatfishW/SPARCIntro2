using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabCameraFocusController : MonoBehaviour
    {
        private static readonly Vector3 LabCameraOffset = new Vector3(-1.45f, 1.18f, 2.35f);
        private static readonly Vector3 LabLookOffset = new Vector3(0f, 1.12f, 0f);

        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField, Min(0.1f)] private float cameraBlendSpeed = 7f;
        [SerializeField] private Vector3 cameraSideOffset = new Vector3(-1.45f, 1.18f, 2.35f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.12f, 0f);
        [SerializeField] private bool hideLocalPlayerBodyInConversation;
        [SerializeField] private string playerCullingLayerName = "Player";
        [SerializeField] private LayerMask conversationCameraObstructionMask = ~0;
        [SerializeField, Min(0.02f)] private float occlusionProbeRadius = 0.08f;
        [SerializeField, Min(0.06f)] private float occlusionBackoffDistance = 0.16f;
        [SerializeField, Min(0.6f)] private float desiredConversationDistance = 2.1f;
        [SerializeField, Min(0.05f)] private float desiredConversationDistanceTolerance = 0.55f;
        [SerializeField, Min(1f)] private float facingTurnSpeed = 12f;
        [SerializeField] private bool repositionLocalPlayerDuringConversation;

        private bool conversationActive;
        private bool cachedBrainState;
        private Transform currentSpeaker;
        private Transform currentConversationTarget;
        private Transform pendingConversationTarget;
        private CinemachineBrain cinemachineBrain;
        private Vector3 targetCameraPosition;
        private Quaternion targetCameraRotation;
        private int cachedCameraCullingMask;
        private bool cullingMaskOverridden;
        private readonly List<Renderer> disabledPlayerRenderers = new List<Renderer>(12);
        private readonly List<ColliderPair> ignoredColliderPairs = new List<ColliderPair>(24);
        private readonly RaycastHit[] cameraOcclusionHits = new RaycastHit[24];
        private StoryNpcAgent movementLockedNpc;
        private NavMeshAgent movementLockedNavAgent;
        private bool movementLockedNavAgentWasStopped;

        private readonly struct ColliderPair
        {
            public ColliderPair(Collider first, Collider second)
            {
                First = first;
                Second = second;
            }

            public Collider First { get; }
            public Collider Second { get; }
        }

        public bool IsConversationActive => conversationActive;

        private void Awake()
        {
            ApplyLabConversationDefaults();
            ResolveRuntimeReferences();
        }

        public void SetConversationTarget(Transform target)
        {
            pendingConversationTarget = target;
            if (!conversationActive)
            {
                return;
            }

            currentConversationTarget = target;
            PrepareConversationParticipants();
        }

        public void BeginConversation()
        {
            ApplyLabConversationDefaults();
            ResolveRuntimeReferences();
            if (conversationActive)
            {
                return;
            }

            conversationActive = true;
            currentConversationTarget = pendingConversationTarget;
            pendingConversationTarget = null;

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
            ApplyConversationVisibility();
            PrepareConversationParticipants();
        }

        public void EndConversation()
        {
            if (!conversationActive)
            {
                return;
            }

            conversationActive = false;
            currentSpeaker = null;
            currentConversationTarget = null;

            if (cinemachineBrain != null)
            {
                cinemachineBrain.enabled = cachedBrainState;
            }

            cinemachineBrain = null;
            RestoreConversationVisibility();
            RestoreConversationCollisionState();
            ReleaseTargetMovementLock();
        }

        public void FocusOn(Transform target)
        {
            ApplyLabConversationDefaults();
            ResolveRuntimeReferences();
            SetConversationTarget(target);
            if (!conversationActive)
            {
                BeginConversation();
            }

            currentSpeaker = target;
            if (currentSpeaker != null)
            {
                currentConversationTarget = currentSpeaker;
                PrepareConversationParticipants();
                ComputeConversationCameraPose(currentSpeaker, forceFallbackToCurrentCamera: false);
            }
        }

        public void FocusOnSpeaker(string speakerDisplayName)
        {
            if (!conversationActive)
            {
                return;
            }

            ApplyLabConversationDefaults();
            ResolveRuntimeReferences();
            currentSpeaker = ResolveSpeakerTransform(speakerDisplayName);
            if (currentSpeaker == null || gameplayCamera == null)
            {
                return;
            }

            if (!speakerDisplayName.Equals("You", System.StringComparison.OrdinalIgnoreCase))
            {
                currentConversationTarget = currentSpeaker;
            }

            ComputeConversationCameraPose(currentSpeaker, forceFallbackToCurrentCamera: false);
        }

        public void ClearFocus()
        {
            EndConversation();
        }

        private void LateUpdate()
        {
            if (!conversationActive)
            {
                return;
            }

            ResolveRuntimeReferences();
            if (gameplayCamera == null)
            {
                return;
            }

            UpdateParticipantFacing();
            if (currentSpeaker != null)
            {
                ComputeConversationCameraPose(currentSpeaker, forceFallbackToCurrentCamera: true);
            }

            MaintainConversationVisibility();

            var delta = Mathf.Max(0.01f, cameraBlendSpeed) * Time.unscaledDeltaTime;
            gameplayCamera.transform.position = Vector3.Lerp(gameplayCamera.transform.position, targetCameraPosition, delta);
            gameplayCamera.transform.rotation = Quaternion.Slerp(gameplayCamera.transform.rotation, targetCameraRotation, delta);
        }

        private void ComputeConversationCameraPose(Transform speaker, bool forceFallbackToCurrentCamera)
        {
            if (speaker == null || gameplayCamera == null)
            {
                return;
            }

            var listener = ResolveConversationListenerTransform(speaker);
            var speakerPosition = speaker.position;
            var lookPoint = speakerPosition + lookOffset;
            var toListener = listener != null ? listener.position - speakerPosition : speaker.forward;
            toListener.y = 0f;
            if (toListener.sqrMagnitude <= 0.0001f)
            {
                toListener = speaker.forward;
                toListener.y = 0f;
            }

            if (toListener.sqrMagnitude <= 0.0001f)
            {
                toListener = Vector3.forward;
            }

            var forward = toListener.normalized;
            var side = Vector3.Cross(Vector3.up, forward).normalized;
            if (side.sqrMagnitude <= 0.0001f)
            {
                side = speaker.right.sqrMagnitude > 0.0001f ? speaker.right.normalized : Vector3.right;
            }

            var elevation = Vector3.up * Mathf.Max(0.35f, cameraSideOffset.y);
            var sideMagnitude = Mathf.Max(0.45f, Mathf.Abs(cameraSideOffset.x));
            var forwardMagnitude = Mathf.Clamp(
                Mathf.Abs(cameraSideOffset.z) * 0.58f,
                0.58f,
                Mathf.Max(0.82f, desiredConversationDistance - 0.25f));

            var sideCandidates = new[]
            {
                sideMagnitude,
                -sideMagnitude,
                sideMagnitude * 1.3f,
                -sideMagnitude * 1.3f,
                sideMagnitude * 0.7f,
                -sideMagnitude * 0.7f,
                0f
            };

            var forwardCandidates = new[]
            {
                forwardMagnitude,
                Mathf.Max(0.45f, forwardMagnitude * 0.8f),
                Mathf.Max(0.35f, forwardMagnitude * 0.62f)
            };

            var foundCandidate = false;
            var fallbackCandidate = speakerPosition + elevation + (forward * forwardMagnitude) + (side * sideMagnitude);
            Vector3 selectedPosition = fallbackCandidate;

            for (var forwardIndex = 0; forwardIndex < forwardCandidates.Length && !foundCandidate; forwardIndex++)
            {
                var forwardDistance = forwardCandidates[forwardIndex];
                for (var sideIndex = 0; sideIndex < sideCandidates.Length; sideIndex++)
                {
                    var candidate = speakerPosition + elevation + (forward * forwardDistance) + (side * sideCandidates[sideIndex]);
                    if (!TryAdjustCameraPositionForOcclusion(lookPoint, candidate, out var adjustedCandidate))
                    {
                        continue;
                    }

                    selectedPosition = adjustedCandidate;
                    foundCandidate = true;
                    break;
                }
            }

            if (!foundCandidate && forceFallbackToCurrentCamera)
            {
                selectedPosition = gameplayCamera.transform.position;
            }

            var lookDirection = lookPoint - selectedPosition;
            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                lookDirection = gameplayCamera.transform.forward;
            }

            targetCameraPosition = selectedPosition;
            targetCameraRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        private bool TryAdjustCameraPositionForOcclusion(Vector3 lookPoint, Vector3 candidatePosition, out Vector3 adjustedPosition)
        {
            adjustedPosition = candidatePosition;
            var direction = candidatePosition - lookPoint;
            var distance = direction.magnitude;
            if (distance <= 0.05f)
            {
                return false;
            }

            direction /= distance;
            var hitCount = Physics.SphereCastNonAlloc(
                lookPoint,
                occlusionProbeRadius,
                direction,
                cameraOcclusionHits,
                distance,
                conversationCameraObstructionMask,
                QueryTriggerInteraction.Ignore);

            var nearestDistance = float.PositiveInfinity;
            Collider nearestBlockingCollider = null;

            for (var index = 0; index < hitCount; index++)
            {
                var hit = cameraOcclusionHits[index];
                if (hit.collider == null)
                {
                    continue;
                }

                if (ShouldIgnoreOcclusionCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestBlockingCollider = hit.collider;
                }
            }

            if (nearestBlockingCollider == null)
            {
                return true;
            }

            adjustedPosition = lookPoint + (direction * Mathf.Max(0.25f, nearestDistance - occlusionBackoffDistance));
            var remaining = adjustedPosition - lookPoint;
            return remaining.sqrMagnitude > 0.08f;
        }

        private bool ShouldIgnoreOcclusionCollider(Collider collider)
        {
            if (collider == null)
            {
                return true;
            }

            if (currentSpeaker != null && collider.transform.IsChildOf(currentSpeaker))
            {
                return true;
            }

            if (localPlayerManager != null && collider.transform.IsChildOf(localPlayerManager.transform))
            {
                return true;
            }

            return false;
        }

        private Transform ResolveConversationListenerTransform(Transform speaker)
        {
            if (speaker == null)
            {
                return null;
            }

            var localPlayerTransform = localPlayerManager != null ? localPlayerManager.transform : null;
            if (localPlayerTransform != null && (speaker == localPlayerTransform || speaker.IsChildOf(localPlayerTransform)))
            {
                return currentConversationTarget;
            }

            return localPlayerTransform != null ? localPlayerTransform : currentConversationTarget;
        }

        private void PrepareConversationParticipants()
        {
            ResolveRuntimeReferences();
            if (localPlayerManager == null || currentConversationTarget == null)
            {
                return;
            }

            if (repositionLocalPlayerDuringConversation)
            {
                RepositionPlayerNearConversationTarget();
            }

            ApplyConversationCollisionState();
            ApplyTargetMovementLock();
            UpdateParticipantFacing(immediate: true);
        }

        private void RepositionPlayerNearConversationTarget()
        {
            if (localPlayerManager == null || currentConversationTarget == null)
            {
                return;
            }

            var playerRoot = localPlayerManager.transform;
            var playerPosition = playerRoot.position;
            var targetPosition = currentConversationTarget.position;

            var delta = playerPosition - targetPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                delta = -currentConversationTarget.forward;
                delta.y = 0f;
            }

            var distance = delta.magnitude;
            var minDistance = Mathf.Max(0.4f, desiredConversationDistance - desiredConversationDistanceTolerance);
            var maxDistance = desiredConversationDistance + desiredConversationDistanceTolerance;
            if (distance >= minDistance && distance <= maxDistance)
            {
                return;
            }

            var direction = delta.normalized;
            var targetPoint = targetPosition + (direction * desiredConversationDistance);
            targetPoint.y = playerPosition.y;
            playerRoot.position = targetPoint;
        }

        private void UpdateParticipantFacing(bool immediate = false)
        {
            if (localPlayerManager == null || currentConversationTarget == null)
            {
                return;
            }

            var playerTransform = localPlayerManager.transform;
            var targetTransform = currentConversationTarget;
            if (playerTransform == null || targetTransform == null)
            {
                return;
            }

            FaceTowards(playerTransform, targetTransform.position, immediate);
            FaceTowards(targetTransform, playerTransform.position, immediate);
        }

        private void FaceTowards(Transform actor, Vector3 worldTarget, bool immediate)
        {
            if (actor == null)
            {
                return;
            }

            var direction = worldTarget - actor.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            if (immediate)
            {
                actor.rotation = targetRotation;
                return;
            }

            var blend = 1f - Mathf.Exp(-Mathf.Max(1f, facingTurnSpeed) * Time.unscaledDeltaTime);
            actor.rotation = Quaternion.Slerp(actor.rotation, targetRotation, blend);
        }

        private void ApplyConversationCollisionState()
        {
            RestoreConversationCollisionState();
            if (localPlayerManager == null || currentConversationTarget == null)
            {
                return;
            }

            var playerColliders = localPlayerManager.GetComponentsInChildren<Collider>(true);
            var targetColliders = currentConversationTarget.GetComponentsInChildren<Collider>(true);
            if (playerColliders == null || targetColliders == null)
            {
                return;
            }

            for (var playerIndex = 0; playerIndex < playerColliders.Length; playerIndex++)
            {
                var playerCollider = playerColliders[playerIndex];
                if (playerCollider == null || !playerCollider.enabled || playerCollider.isTrigger)
                {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < targetColliders.Length; targetIndex++)
                {
                    var targetCollider = targetColliders[targetIndex];
                    if (targetCollider == null || !targetCollider.enabled || targetCollider.isTrigger)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(playerCollider, targetCollider, true);
                    ignoredColliderPairs.Add(new ColliderPair(playerCollider, targetCollider));
                }
            }
        }

        private void RestoreConversationCollisionState()
        {
            for (var index = 0; index < ignoredColliderPairs.Count; index++)
            {
                var pair = ignoredColliderPairs[index];
                if (pair.First == null || pair.Second == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(pair.First, pair.Second, false);
            }

            ignoredColliderPairs.Clear();
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

            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
            if (npcRegistry == null || npcRegistry.gameObject.scene != activeScene)
            {
                npcRegistry = FindSceneObject<StoryNpcRegistry>(activeScene);
            }

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
            var activeScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();

            if (controlLock != null && controlLock.gameObject.scene != activeScene)
            {
                controlLock = null;
            }

            if (npcRegistry != null && npcRegistry.gameObject.scene != activeScene)
            {
                npcRegistry = null;
            }

            controlLock = controlLock != null ? controlLock : FindSceneObject<ClassroomPlayerControlLock>(activeScene);
            npcRegistry = npcRegistry != null ? npcRegistry : FindSceneObject<StoryNpcRegistry>(activeScene);

            if (gameplayCamera == null || gameplayCamera.gameObject.scene != activeScene)
            {
                gameplayCamera = FindPreferredSceneCamera(activeScene);
            }

            var previousPlayerManager = localPlayerManager;
            if (!IsValidLocalPlayerManager(localPlayerManager, activeScene))
            {
                localPlayerManager = null;
            }

            if (localPlayerManager == null &&
                StorySceneLocalPlayerSpawner.TryResolveSceneLocalPlayerMovement(activeScene, out var sceneLocalMovement) &&
                sceneLocalMovement != null)
            {
                localPlayerManager = sceneLocalMovement.GetComponentInParent<CorePlayerManager>() ??
                                     sceneLocalMovement.GetComponent<CorePlayerManager>();
            }

            if (localPlayerManager == null)
            {
                CorePlayerManager ownerInActiveScene = null;
                CorePlayerManager firstInActiveScene = null;

                var players = FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var index = 0; index < players.Length; index++)
                {
                    var candidate = players[index];
                    if (candidate == null)
                    {
                        continue;
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
                }

                localPlayerManager = ownerInActiveScene ?? firstInActiveScene;
            }

            if (previousPlayerManager != localPlayerManager)
            {
                disabledPlayerRenderers.Clear();
            }
        }

        private static bool IsValidLocalPlayerManager(CorePlayerManager candidate, Scene activeScene)
        {
            if (candidate == null)
            {
                return false;
            }

            var scene = candidate.gameObject.scene;
            return scene.IsValid() && scene == activeScene;
        }

        private static Camera FindPreferredSceneCamera(Scene activeScene)
        {
            Camera fallback = null;
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < cameras.Length; index++)
            {
                var candidate = cameras[index];
                if (candidate == null || candidate.gameObject.scene != activeScene)
                {
                    continue;
                }

                if (candidate.CompareTag("MainCamera"))
                {
                    return candidate;
                }

                if (fallback == null && candidate.isActiveAndEnabled)
                {
                    fallback = candidate;
                }
                else if (fallback == null)
                {
                    fallback = candidate;
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            var main = Camera.main;
            return main != null && main.gameObject.scene == activeScene ? main : null;
        }

        private static T FindSceneObject<T>(Scene activeScene)
            where T : Component
        {
            if (!activeScene.IsValid())
            {
                return null;
            }

            var candidates = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject.scene == activeScene)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ApplyConversationVisibility()
        {
            if (gameplayCamera == null)
            {
                return;
            }

            cachedCameraCullingMask = gameplayCamera.cullingMask;
            cullingMaskOverridden = false;
            disabledPlayerRenderers.Clear();

            if (!hideLocalPlayerBodyInConversation)
            {
                return;
            }

            var requestedLayer = LayerMask.NameToLayer(playerCullingLayerName);
            if (requestedLayer >= 0)
            {
                var bit = 1 << requestedLayer;
                if ((gameplayCamera.cullingMask & bit) != 0)
                {
                    gameplayCamera.cullingMask &= ~bit;
                    cullingMaskOverridden = true;
                }
            }

            CollectLocalPlayerRenderers(disabledPlayerRenderers);
            for (var index = 0; index < disabledPlayerRenderers.Count; index++)
            {
                var renderer = disabledPlayerRenderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                renderer.enabled = false;
            }
        }

        private void RestoreConversationVisibility()
        {
            if (gameplayCamera != null && cullingMaskOverridden)
            {
                gameplayCamera.cullingMask = cachedCameraCullingMask;
                cullingMaskOverridden = false;
            }

            for (var index = 0; index < disabledPlayerRenderers.Count; index++)
            {
                var renderer = disabledPlayerRenderers[index];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            disabledPlayerRenderers.Clear();
        }

        private void MaintainConversationVisibility()
        {
            if (!conversationActive || !hideLocalPlayerBodyInConversation)
            {
                return;
            }

            if (disabledPlayerRenderers.Count == 0)
            {
                CollectLocalPlayerRenderers(disabledPlayerRenderers);
            }

            for (var index = 0; index < disabledPlayerRenderers.Count; index++)
            {
                var renderer = disabledPlayerRenderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.enabled)
                {
                    renderer.enabled = false;
                }
            }
        }

        private void CollectLocalPlayerRenderers(List<Renderer> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            var seen = new HashSet<Renderer>();

            if (localPlayerManager != null)
            {
                AddRenderersFromTransform(localPlayerManager.transform, target, seen);

                if (localPlayerManager.CoreMovement != null)
                {
                    AddRenderersFromTransform(localPlayerManager.CoreMovement.transform, target, seen);
                }
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                AddRenderersFromTransform(taggedPlayer.transform, target, seen);
            }

            var animators = FindObjectsByType<CoreAnimator>(FindObjectsSortMode.None);
            for (var index = 0; index < animators.Length; index++)
            {
                var coreAnimator = animators[index];
                if (coreAnimator == null || !coreAnimator.IsOwner)
                {
                    continue;
                }

                var animatorTransform = coreAnimator.transform;
                if (animatorTransform == null)
                {
                    continue;
                }

                AddRenderersFromTransform(animatorTransform, target, seen);
                if (animatorTransform.root != null)
                {
                    AddRenderersFromTransform(animatorTransform.root, target, seen);
                }
            }
        }

        private static void AddRenderersFromTransform(Transform root, List<Renderer> target, HashSet<Renderer> seen)
        {
            if (root == null || target == null || seen == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !seen.Add(renderer))
                {
                    continue;
                }

                target.Add(renderer);
            }
        }

        private void ApplyTargetMovementLock()
        {
            ReleaseTargetMovementLock();
            if (currentConversationTarget == null)
            {
                return;
            }

            movementLockedNpc = currentConversationTarget.GetComponentInParent<StoryNpcAgent>();
            if (movementLockedNpc == null)
            {
                return;
            }

            movementLockedNavAgent = movementLockedNpc.GetComponent<NavMeshAgent>();
            if (movementLockedNavAgent != null)
            {
                movementLockedNavAgentWasStopped = movementLockedNavAgent.isStopped;
                movementLockedNavAgent.isStopped = true;
                movementLockedNavAgent.velocity = Vector3.zero;
            }
        }

        private void ReleaseTargetMovementLock()
        {
            if (movementLockedNavAgent != null)
            {
                movementLockedNavAgent.isStopped = movementLockedNavAgentWasStopped;
                movementLockedNavAgent = null;
                movementLockedNavAgentWasStopped = false;
            }

            movementLockedNpc = null;
        }

        private void ApplyLabConversationDefaults()
        {
            cameraSideOffset = LabCameraOffset;
            lookOffset = LabLookOffset;
            hideLocalPlayerBodyInConversation = false;
            desiredConversationDistance = 2.1f;
            desiredConversationDistanceTolerance = 0.55f;
            repositionLocalPlayerDuringConversation = false;
        }
    }
}
