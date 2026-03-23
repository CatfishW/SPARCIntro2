using Blocks.Gameplay.Core;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine.AI;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomStoryConversationPresentationController : MonoBehaviour
    {
        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private ClassroomNpcAmbientController ambientController;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField, Min(0.1f)] private float cameraBlendSpeed = 7f;
        [SerializeField] private Vector3 cameraSideOffset = new Vector3(-1.05f, 1.26f, 1.42f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.12f, 0f);
        [SerializeField] private bool hideLocalPlayerBodyInConversation = true;
        [SerializeField] private string playerCullingLayerName = "Player";
        [SerializeField] private LayerMask conversationCameraObstructionMask = ~0;
        [SerializeField, Min(0.02f)] private float occlusionProbeRadius = 0.08f;
        [SerializeField, Min(0.06f)] private float occlusionBackoffDistance = 0.16f;
        [SerializeField, Min(0.6f)] private float desiredConversationDistance = 1.4f;
        [SerializeField, Min(0.05f)] private float desiredConversationDistanceTolerance = 0.35f;
        [SerializeField, Min(1f)] private float facingTurnSpeed = 12f;

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
        private bool localPlayerBodyHidden;
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
            ResolveRuntimeReferences();
            if (conversationActive)
            {
                return;
            }

            conversationActive = true;
            controlLock?.Acquire(unlockCursor: true);
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
            controlLock?.Release();

            if (cinemachineBrain != null)
            {
                cinemachineBrain.enabled = cachedBrainState;
            }

            cinemachineBrain = null;
            RestoreConversationVisibility();
            RestoreConversationCollisionState();
            ReleaseTargetMovementLock();
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

            if (!speakerDisplayName.Equals("You", System.StringComparison.OrdinalIgnoreCase))
            {
                currentConversationTarget = currentSpeaker;
            }

            MaintainConversationVisibility();
            ComputeConversationCameraPose(currentSpeaker, forceFallbackToCurrentCamera: false);
        }

        private void LateUpdate()
        {
            if (!conversationActive || gameplayCamera == null)
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

            RepositionPlayerNearConversationTarget();
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
            ambientController = ambientController != null ? ambientController : FindFirstObjectByType<ClassroomNpcAmbientController>();
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

        private void ApplyConversationVisibility()
        {
            if (gameplayCamera == null)
            {
                return;
            }

            cachedCameraCullingMask = gameplayCamera.cullingMask;
            cullingMaskOverridden = false;
            localPlayerBodyHidden = false;
            disabledPlayerRenderers.Clear();
            MaintainConversationVisibility();
        }

        private void RestoreConversationVisibility()
        {
            SetLocalPlayerBodyHidden(false, force: true);
        }

        private void MaintainConversationVisibility()
        {
            if (!conversationActive)
            {
                SetLocalPlayerBodyHidden(false, force: false);
                return;
            }

            var shouldHide = ShouldHideLocalPlayerBodyForCurrentSpeaker();
            SetLocalPlayerBodyHidden(shouldHide, force: false);
        }

        private bool ShouldHideLocalPlayerBodyForCurrentSpeaker()
        {
            if (!hideLocalPlayerBodyInConversation)
            {
                return false;
            }

            if (currentSpeaker == null)
            {
                return false;
            }

            return !IsCurrentSpeakerLocalPlayer();
        }

        private bool IsCurrentSpeakerLocalPlayer()
        {
            if (localPlayerManager == null || currentSpeaker == null)
            {
                return false;
            }

            var localRoot = localPlayerManager.transform;
            return currentSpeaker == localRoot ||
                   currentSpeaker.IsChildOf(localRoot) ||
                   localRoot.IsChildOf(currentSpeaker);
        }

        private void SetLocalPlayerBodyHidden(bool shouldHide, bool force)
        {
            if (!force && shouldHide == localPlayerBodyHidden)
            {
                if (shouldHide)
                {
                    EnsurePlayerRenderersHidden();
                }

                return;
            }

            if (shouldHide)
            {
                HidePlayerLayerInCamera();
                EnsurePlayerRenderersHidden();
            }
            else
            {
                RestorePlayerLayerInCamera();
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

            localPlayerBodyHidden = shouldHide;
        }

        private void EnsurePlayerRenderersHidden()
        {
            if (disabledPlayerRenderers.Count == 0)
            {
                CollectLocalPlayerRenderers(disabledPlayerRenderers);
            }

            for (var index = 0; index < disabledPlayerRenderers.Count; index++)
            {
                var renderer = disabledPlayerRenderers[index];
                if (renderer != null && renderer.enabled)
                {
                    renderer.enabled = false;
                }
            }
        }

        private void HidePlayerLayerInCamera()
        {
            if (gameplayCamera == null)
            {
                return;
            }

            var requestedLayer = LayerMask.NameToLayer(playerCullingLayerName);
            if (requestedLayer < 0)
            {
                return;
            }

            var bit = 1 << requestedLayer;
            if ((gameplayCamera.cullingMask & bit) == 0)
            {
                return;
            }

            gameplayCamera.cullingMask &= ~bit;
            cullingMaskOverridden = true;
        }

        private void RestorePlayerLayerInCamera()
        {
            if (gameplayCamera == null || !cullingMaskOverridden)
            {
                return;
            }

            gameplayCamera.cullingMask = cachedCameraCullingMask;
            cullingMaskOverridden = false;
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

            if (ambientController != null && !string.IsNullOrWhiteSpace(movementLockedNpc.NpcId))
            {
                ambientController.TrySetNpcCanMove(movementLockedNpc.NpcId, false);
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

            if (movementLockedNpc != null && ambientController != null && !string.IsNullOrWhiteSpace(movementLockedNpc.NpcId))
            {
                ambientController.TrySetNpcCanMove(movementLockedNpc.NpcId, true);
            }

            movementLockedNpc = null;
        }
    }
}
