using System;
using Blocks.Gameplay.Core.Story;
using UnityEngine;

namespace Blocks.Gameplay.Core
{
    [DisallowMultipleComponent]
    public sealed class ClassroomNpcAmbientController : MonoBehaviour
    {
        [Serializable]
        private sealed class AmbientNpcState
        {
            public string npcId = string.Empty;
            public Transform root;
            public Animator animator;
            public RuntimeAnimatorController idleController;
            public RuntimeAnimatorController gestureController;
            [Min(0.2f)] public float moveSpeed = 0.48f;
            [Min(30f)] public float turnSpeedDegreesPerSecond = 260f;
            [Min(0.2f)] public float roamRadius = 1.35f;
            [Min(0.1f)] public float pauseMinSeconds = 0.8f;
            [Min(0.1f)] public float pauseMaxSeconds = 2.4f;
            public bool canMove = true;

            [NonSerialized] public CapsuleCollider Capsule;
            [NonSerialized] public Vector3 HomePosition;
            [NonSerialized] public Vector3 TargetPosition;
            [NonSerialized] public float PauseUntilTime;
            [NonSerialized] public int ForwardHash;
            [NonSerialized] public int SpeedHash;
            [NonSerialized] public int MotionSpeedHash;
            [NonSerialized] public int TurnHash;
            [NonSerialized] public int OnGroundHash;
            [NonSerialized] public int GroundedHash;
            [NonSerialized] public int CrouchHash;
            [NonSerialized] public int FreeFallHash;
            [NonSerialized] public bool HasForward;
            [NonSerialized] public bool HasSpeed;
            [NonSerialized] public bool HasMotionSpeed;
            [NonSerialized] public bool HasTurn;
            [NonSerialized] public bool HasOnGround;
            [NonSerialized] public bool HasGrounded;
            [NonSerialized] public bool HasCrouch;
            [NonSerialized] public bool HasFreeFall;
            [NonSerialized] public float FeetOffset;
            [NonSerialized] public float BaseRootY;
            [NonSerialized] public float CurrentForwardInput;
            [NonSerialized] public float CurrentTurnInput;
            [NonSerialized] public float MotionSeed;
        }

        [SerializeField] private AmbientNpcState teacher = new AmbientNpcState
        {
            npcId = ClassroomStoryNpcIds.Teacher,
            moveSpeed = 0.33f,
            roamRadius = 0.9f,
            pauseMinSeconds = 1.4f,
            pauseMaxSeconds = 3f
        };

        [SerializeField] private AmbientNpcState friend = new AmbientNpcState
        {
            npcId = ClassroomStoryNpcIds.Friend,
            moveSpeed = 0.52f,
            roamRadius = 1.55f,
            pauseMinSeconds = 0.7f,
            pauseMaxSeconds = 2.2f
        };

        [SerializeField] private AmbientNpcState skeptic = new AmbientNpcState
        {
            npcId = ClassroomStoryNpcIds.Skeptic,
            moveSpeed = 0.56f,
            roamRadius = 1.55f,
            pauseMinSeconds = 0.7f,
            pauseMaxSeconds = 2.2f
        };

        [SerializeField] private Collider floorCollider;
        [SerializeField, Min(0f)] private float groundingOffset = 0.015f;
        [SerializeField, Min(0.01f)] private float groundHeightClampRange = 0.2f;
        [SerializeField, Min(0.2f)] private float destinationTolerance = 0.12f;
        [SerializeField, Min(0.1f)] private float personalSpaceRadius = 0.55f;
        [SerializeField, Min(0f)] private float avoidanceStrength = 0.95f;
        [SerializeField, Min(0f)] private float wanderJitterStrength = 0.16f;
        [SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.18f;
        [SerializeField, Min(0.3f)] private float obstacleProbeHeight = 1.2f;
        [SerializeField, Min(0.5f)] private float animationBlendSharpness = 8f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private bool syncAnimatorControllerFromPlayer = true;
        [SerializeField] private RuntimeAnimatorController fallbackSharedController;

        private AmbientNpcState[] cast;
        private ClassroomPlayerControlLock playerControlLock;
        private RuntimeAnimatorController sharedController;
        private readonly Collider[] obstacleBuffer = new Collider[24];

        private void Awake()
        {
            cast = new[] { teacher, friend, skeptic };
            playerControlLock = FindFirstObjectByType<ClassroomPlayerControlLock>();

            for (var index = 0; index < cast.Length; index++)
            {
                InitializeNpc(cast[index]);
            }

            SyncAnimatorControllersFromPlayer(forceRefresh: true);
            SnapCastToGround();
        }

        private void Update()
        {
            if (cast == null || cast.Length == 0)
            {
                return;
            }

            if (playerControlLock == null)
            {
                playerControlLock = FindFirstObjectByType<ClassroomPlayerControlLock>();
            }

            if (sharedController == null)
            {
                SyncAnimatorControllersFromPlayer(forceRefresh: false);
            }

            var pauseForConversation = playerControlLock != null && playerControlLock.IsLocked;
            for (var index = 0; index < cast.Length; index++)
            {
                UpdateNpc(cast[index], pauseForConversation);
            }
        }

        public void Configure(
            Transform teacherRoot,
            Transform friendRoot,
            Transform skepticRoot,
            Collider sharedFloorCollider,
            RuntimeAnimatorController idleController,
            RuntimeAnimatorController interactionController,
            RuntimeAnimatorController reactionController)
        {
            teacher.root = teacherRoot;
            friend.root = friendRoot;
            skeptic.root = skepticRoot;

            teacher.animator = teacherRoot != null ? teacherRoot.GetComponentInChildren<Animator>(true) : null;
            friend.animator = friendRoot != null ? friendRoot.GetComponentInChildren<Animator>(true) : null;
            skeptic.animator = skepticRoot != null ? skepticRoot.GetComponentInChildren<Animator>(true) : null;

            teacher.idleController = idleController;
            teacher.gestureController = interactionController;

            friend.idleController = idleController;
            friend.gestureController = reactionController;

            skeptic.idleController = idleController;
            skeptic.gestureController = interactionController;

            floorCollider = sharedFloorCollider;

            cast = new[] { teacher, friend, skeptic };
            for (var index = 0; index < cast.Length; index++)
            {
                InitializeNpc(cast[index]);
            }

            SyncAnimatorControllersFromPlayer(forceRefresh: true);
            SnapCastToGround();
        }

        public bool TrySetNpcCanMove(string npcId, bool canMove)
        {
            var npc = ResolveNpcState(npcId);
            if (npc == null)
            {
                return false;
            }

            npc.canMove = canMove;
            return true;
        }

        public bool TrySnapNpcToGround(string npcId)
        {
            var npc = ResolveNpcState(npcId);
            if (npc == null)
            {
                return false;
            }

            EnsureGrounded(npc, keepHorizontalPosition: true);
            return true;
        }

        public bool TryGetNpcTransform(string npcId, out Transform root)
        {
            root = null;
            var npc = ResolveNpcState(npcId);
            if (npc == null || npc.root == null)
            {
                return false;
            }

            root = npc.root;
            return true;
        }

        private void InitializeNpc(AmbientNpcState npc)
        {
            if (npc == null || npc.root == null)
            {
                return;
            }

            if (npc.animator == null)
            {
                npc.animator = npc.root.GetComponentInChildren<Animator>(true);
            }

            if (npc.animator != null)
            {
                var controller = sharedController != null ? sharedController : npc.idleController;
                if (npc.animator.runtimeAnimatorController != controller && controller != null)
                {
                    npc.animator.runtimeAnimatorController = controller;
                }

                npc.animator.applyRootMotion = false;
                CacheAnimatorParameters(npc);
            }

            npc.Capsule = npc.root.GetComponent<CapsuleCollider>();
            npc.HomePosition = npc.root.position;
            npc.TargetPosition = npc.root.position;
            npc.PauseUntilTime = 0f;
            npc.CurrentForwardInput = 0f;
            npc.CurrentTurnInput = 0f;
            npc.MotionSeed = UnityEngine.Random.Range(0f, 100f);

            npc.FeetOffset = 0f;
            if (npc.Capsule != null)
            {
                npc.FeetOffset = Mathf.Clamp(
                    (npc.Capsule.height * 0.5f) - npc.Capsule.center.y,
                    0f,
                    1.45f);
            }
            else if (TryGetCombinedBounds(npc.root.gameObject, out var bounds))
            {
                npc.FeetOffset = Mathf.Clamp(npc.root.position.y - bounds.min.y, 0f, 1.45f);
            }

            npc.BaseRootY = npc.root.position.y;
            PickNextDestination(npc, immediate: true);
        }

        private void UpdateNpc(AmbientNpcState npc, bool pauseForConversation)
        {
            if (npc == null || npc.root == null)
            {
                return;
            }

            if (pauseForConversation || !npc.canMove)
            {
                var lookTarget = ResolveConversationLookTarget(npc);
                RotateTowards(npc, lookTarget - npc.root.position);
                ApplyAnimator(npc, 0f, 0f);
                EnsureGrounded(npc, keepHorizontalPosition: true);
                return;
            }

            if (Time.time < npc.PauseUntilTime)
            {
                var socialLookTarget = ResolveConversationLookTarget(npc);
                RotateTowards(npc, socialLookTarget - npc.root.position);
                ApplyAnimator(npc, 0f, 0f);
                EnsureGrounded(npc, keepHorizontalPosition: true);
                return;
            }

            var toDestination = npc.TargetPosition - npc.root.position;
            toDestination.y = 0f;
            var distance = toDestination.magnitude;

            if (distance <= destinationTolerance)
            {
                PickNextDestination(npc, immediate: false);
                ApplyAnimator(npc, 0f, 0f);
                EnsureGrounded(npc, keepHorizontalPosition: true);
                return;
            }

            var desiredDirection = toDestination / Mathf.Max(distance, 0.0001f);
            desiredDirection += ComputeAvoidance(npc) * avoidanceStrength;
            desiredDirection += ComputeWanderJitter(npc) * wanderJitterStrength;
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude > 1f)
            {
                desiredDirection.Normalize();
            }

            var gaitFactor = 0.92f + (Mathf.Sin((Time.time * 0.9f) + npc.MotionSeed) * 0.12f);
            var resolvedSpeed = npc.moveSpeed * Mathf.Max(0.65f, gaitFactor);
            var moveDelta = desiredDirection * (resolvedSpeed * Time.deltaTime);
            if (moveDelta.magnitude > distance)
            {
                moveDelta = toDestination;
            }

            var proposed = npc.root.position + moveDelta;
            proposed = ClampToFloorBounds(proposed);
            var groundedProposed = ProjectToGround(npc, proposed);
            if (IsBlockedByEnvironment(npc, groundedProposed))
            {
                PickNextDestination(npc, immediate: false);
                ApplyAnimator(npc, 0f, 0f);
                EnsureGrounded(npc, keepHorizontalPosition: true);
                return;
            }

            npc.root.position = groundedProposed;

            RotateTowards(npc, desiredDirection);
            var turnInput = Vector3.SignedAngle(npc.root.forward, desiredDirection, Vector3.up) / 90f;
            var forwardInput = Mathf.Clamp(moveDelta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f), 0f, resolvedSpeed + 0.001f) / Mathf.Max(0.001f, resolvedSpeed);
            ApplyAnimator(npc, forwardInput, turnInput);
        }

        private void PickNextDestination(AmbientNpcState npc, bool immediate)
        {
            if (npc == null || npc.root == null)
            {
                return;
            }

            var anchor = npc.HomePosition;
            var randomPoint = UnityEngine.Random.insideUnitCircle * npc.roamRadius;
            var destination = new Vector3(anchor.x + randomPoint.x, npc.root.position.y, anchor.z + randomPoint.y);
            destination = ClampToFloorBounds(destination);
            npc.TargetPosition = ProjectToGround(npc, destination);

            if (!immediate)
            {
                npc.PauseUntilTime = Time.time + UnityEngine.Random.Range(npc.pauseMinSeconds, npc.pauseMaxSeconds);
            }
            else
            {
                npc.PauseUntilTime = Time.time + UnityEngine.Random.Range(0.35f, 0.9f);
            }
        }

        private Vector3 ComputeAvoidance(AmbientNpcState npc)
        {
            var avoidance = Vector3.zero;
            if (cast == null)
            {
                return avoidance;
            }

            for (var index = 0; index < cast.Length; index++)
            {
                var other = cast[index];
                if (other == null || other == npc || other.root == null)
                {
                    continue;
                }

                var delta = npc.root.position - other.root.position;
                delta.y = 0f;
                var distance = delta.magnitude;
                if (distance <= 0.001f || distance >= personalSpaceRadius)
                {
                    continue;
                }

                var push = 1f - (distance / personalSpaceRadius);
                avoidance += delta.normalized * push;
            }

            return avoidance;
        }

        private Vector3 ResolveConversationLookTarget(AmbientNpcState npc)
        {
            var nearest = default(AmbientNpcState);
            var nearestDistance = float.MaxValue;

            for (var index = 0; index < cast.Length; index++)
            {
                var candidate = cast[index];
                if (candidate == null || candidate == npc || candidate.root == null || npc.root == null)
                {
                    continue;
                }

                var distance = Vector3.SqrMagnitude(candidate.root.position - npc.root.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                }
            }

            return nearest != null && nearest.root != null
                ? nearest.root.position + (Vector3.up * 1.1f)
                : npc.root.position + (npc.root.forward * 2f);
        }

        private void RotateTowards(AmbientNpcState npc, Vector3 worldDirection)
        {
            if (npc == null || npc.root == null)
            {
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var targetRotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            npc.root.rotation = Quaternion.RotateTowards(
                npc.root.rotation,
                targetRotation,
                npc.turnSpeedDegreesPerSecond * Time.deltaTime);
        }

        private void ApplyAnimator(AmbientNpcState npc, float forwardInput, float turnInput)
        {
            if (npc?.animator == null)
            {
                return;
            }

            var blendT = 1f - Mathf.Exp(-Mathf.Max(0.1f, animationBlendSharpness) * Time.deltaTime);
            npc.CurrentForwardInput = Mathf.Lerp(npc.CurrentForwardInput, Mathf.Clamp(forwardInput, 0f, 1f), blendT);
            npc.CurrentTurnInput = Mathf.Lerp(npc.CurrentTurnInput, Mathf.Clamp(turnInput, -1f, 1f), blendT);

            if (npc.HasForward)
            {
                npc.animator.SetFloat(npc.ForwardHash, npc.CurrentForwardInput);
            }

            if (npc.HasSpeed)
            {
                npc.animator.SetFloat(npc.SpeedHash, npc.CurrentForwardInput);
            }

            if (npc.HasMotionSpeed)
            {
                npc.animator.SetFloat(npc.MotionSpeedHash, Mathf.Lerp(0.2f, 1f, npc.CurrentForwardInput));
            }

            if (npc.HasTurn)
            {
                npc.animator.SetFloat(npc.TurnHash, npc.CurrentTurnInput);
            }

            if (npc.HasOnGround)
            {
                npc.animator.SetBool(npc.OnGroundHash, true);
            }

            if (npc.HasGrounded)
            {
                npc.animator.SetBool(npc.GroundedHash, true);
            }

            if (npc.HasCrouch)
            {
                npc.animator.SetBool(npc.CrouchHash, false);
            }

            if (npc.HasFreeFall)
            {
                npc.animator.SetBool(npc.FreeFallHash, false);
            }
        }

        private void CacheAnimatorParameters(AmbientNpcState npc)
        {
            npc.ForwardHash = Animator.StringToHash("Forward");
            npc.SpeedHash = Animator.StringToHash("Speed");
            npc.MotionSpeedHash = Animator.StringToHash("MotionSpeed");
            npc.TurnHash = Animator.StringToHash("Turn");
            npc.OnGroundHash = Animator.StringToHash("OnGround");
            npc.GroundedHash = Animator.StringToHash("Grounded");
            npc.CrouchHash = Animator.StringToHash("Crouch");
            npc.FreeFallHash = Animator.StringToHash("FreeFall");

            npc.HasForward = HasParameter(npc.animator, "Forward", AnimatorControllerParameterType.Float);
            npc.HasSpeed = HasParameter(npc.animator, "Speed", AnimatorControllerParameterType.Float);
            npc.HasMotionSpeed = HasParameter(npc.animator, "MotionSpeed", AnimatorControllerParameterType.Float);
            npc.HasTurn = HasParameter(npc.animator, "Turn", AnimatorControllerParameterType.Float);
            npc.HasOnGround = HasParameter(npc.animator, "OnGround", AnimatorControllerParameterType.Bool);
            npc.HasGrounded = HasParameter(npc.animator, "Grounded", AnimatorControllerParameterType.Bool);
            npc.HasCrouch = HasParameter(npc.animator, "Crouch", AnimatorControllerParameterType.Bool);
            npc.HasFreeFall = HasParameter(npc.animator, "FreeFall", AnimatorControllerParameterType.Bool);
        }

        private static bool HasParameter(Animator animator, string parameterName, AnimatorControllerParameterType type)
        {
            if (animator == null)
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (parameter.type == type && parameter.name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        private void SyncAnimatorControllersFromPlayer(bool forceRefresh)
        {
            if (!syncAnimatorControllerFromPlayer)
            {
                return;
            }

            if (sharedController != null && !forceRefresh)
            {
                ApplySharedAnimatorController(sharedController);
                return;
            }

            var resolved = ResolvePlayerAnimatorController();
            if (resolved == null)
            {
                resolved = fallbackSharedController;
            }

            if (resolved == null)
            {
                return;
            }

            sharedController = resolved;
            ApplySharedAnimatorController(sharedController);
        }

        private RuntimeAnimatorController ResolvePlayerAnimatorController()
        {
            var players = FindObjectsByType<CorePlayerManager>(FindObjectsSortMode.None);
            for (var index = 0; index < players.Length; index++)
            {
                var player = players[index];
                if (player == null || !player.IsOwner)
                {
                    continue;
                }

                var animator = player.GetComponentInChildren<Animator>(true);
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    return animator.runtimeAnimatorController;
                }
            }

            return null;
        }

        private void ApplySharedAnimatorController(RuntimeAnimatorController controller)
        {
            if (controller == null || cast == null)
            {
                return;
            }

            for (var index = 0; index < cast.Length; index++)
            {
                var npc = cast[index];
                if (npc?.animator == null)
                {
                    continue;
                }

                if (npc.animator.runtimeAnimatorController != controller)
                {
                    npc.animator.runtimeAnimatorController = controller;
                    CacheAnimatorParameters(npc);
                }
            }
        }

        private void SnapCastToGround()
        {
            if (cast == null)
            {
                return;
            }

            for (var index = 0; index < cast.Length; index++)
            {
                EnsureGrounded(cast[index], keepHorizontalPosition: true);
            }
        }

        private void EnsureGrounded(AmbientNpcState npc, bool keepHorizontalPosition)
        {
            if (npc == null || npc.root == null)
            {
                return;
            }

            var position = npc.root.position;
            if (!keepHorizontalPosition)
            {
                position = ClampToFloorBounds(position);
            }

            npc.root.position = ProjectToGround(npc, position);
        }

        private Vector3 ClampToFloorBounds(Vector3 worldPosition)
        {
            if (floorCollider == null)
            {
                return worldPosition;
            }

            var bounds = floorCollider.bounds;
            worldPosition.x = Mathf.Clamp(worldPosition.x, bounds.min.x + 0.35f, bounds.max.x - 0.35f);
            worldPosition.z = Mathf.Clamp(worldPosition.z, bounds.min.z + 0.35f, bounds.max.z - 0.35f);
            return worldPosition;
        }

        private Vector3 ProjectToGround(AmbientNpcState npc, Vector3 worldPosition)
        {
            var floorY = ResolveFloorY(npc, worldPosition);
            var rootY = floorY + groundingOffset + (npc != null ? npc.FeetOffset : 0f);
            if (npc != null && npc.BaseRootY != 0f)
            {
                rootY = Mathf.Clamp(rootY, npc.BaseRootY - groundHeightClampRange, npc.BaseRootY + groundHeightClampRange);
            }

            worldPosition.y = rootY;
            return worldPosition;
        }

        private float ResolveFloorY(AmbientNpcState npc, Vector3 aroundPosition)
        {
            if (floorCollider != null)
            {
                var expected = npc != null ? npc.BaseRootY - npc.FeetOffset : aroundPosition.y;
                var probe = new Vector3(aroundPosition.x, expected + 0.6f, aroundPosition.z);
                var closest = floorCollider.ClosestPoint(probe);
                if (closest != probe)
                {
                    return closest.y;
                }

                return floorCollider.bounds.max.y;
            }

            var origin = aroundPosition + (Vector3.up * 2.5f);
            var hits = Physics.RaycastAll(origin, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                var expectedFloor = npc != null ? npc.BaseRootY - npc.FeetOffset : aroundPosition.y;
                var foundCandidate = false;
                var candidateY = float.MinValue;
                for (var index = 0; index < hits.Length; index++)
                {
                    var hit = hits[index];
                    if (hit.collider == null || hit.normal.y < 0.3f)
                    {
                        continue;
                    }

                    if (hit.point.y > expectedFloor + 0.4f)
                    {
                        continue;
                    }

                    if (!foundCandidate || hit.point.y > candidateY)
                    {
                        candidateY = hit.point.y;
                        foundCandidate = true;
                    }
                }

                if (foundCandidate)
                {
                    return candidateY;
                }

                var nearest = hits[0].point.y;
                var nearestDistance = Mathf.Abs(nearest - expectedFloor);
                for (var index = 1; index < hits.Length; index++)
                {
                    var distance = Mathf.Abs(hits[index].point.y - expectedFloor);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = hits[index].point.y;
                    }
                }

                return nearest;
            }

            return aroundPosition.y;
        }

        private static bool TryGetCombinedBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
            {
                return false;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private Vector3 ComputeWanderJitter(AmbientNpcState npc)
        {
            if (npc == null || npc.root == null)
            {
                return Vector3.zero;
            }

            var sampleX = Mathf.PerlinNoise(npc.MotionSeed + (Time.time * 0.22f), 0.13f) - 0.5f;
            var sampleZ = Mathf.PerlinNoise(0.41f, npc.MotionSeed + (Time.time * 0.22f)) - 0.5f;
            return new Vector3(sampleX, 0f, sampleZ);
        }

        private bool IsBlockedByEnvironment(AmbientNpcState npc, Vector3 worldPosition)
        {
            if (npc == null || npc.root == null)
            {
                return false;
            }

            var radius = Mathf.Max(0.06f, obstacleProbeRadius);
            var probeHeight = Mathf.Max(obstacleProbeHeight, radius * 2f);
            var bottom = worldPosition + (Vector3.up * radius);
            var top = worldPosition + (Vector3.up * (probeHeight - radius));
            var hitCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                obstacleBuffer,
                obstacleMask,
                QueryTriggerInteraction.Ignore);

            for (var index = 0; index < hitCount; index++)
            {
                var collider = obstacleBuffer[index];
                if (collider == null)
                {
                    continue;
                }

                if (floorCollider != null && collider == floorCollider)
                {
                    continue;
                }

                if (collider.transform.IsChildOf(npc.root))
                {
                    continue;
                }

                if (IsNpcCollider(collider))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool IsNpcCollider(Collider collider)
        {
            if (collider == null || cast == null)
            {
                return false;
            }

            for (var index = 0; index < cast.Length; index++)
            {
                var npc = cast[index];
                if (npc?.root == null)
                {
                    continue;
                }

                if (collider.transform.IsChildOf(npc.root))
                {
                    return true;
                }
            }

            return false;
        }

        private AmbientNpcState ResolveNpcState(string npcId)
        {
            if (cast == null || cast.Length == 0 || string.IsNullOrWhiteSpace(npcId))
            {
                return null;
            }

            for (var index = 0; index < cast.Length; index++)
            {
                var npc = cast[index];
                if (npc == null)
                {
                    continue;
                }

                if (string.Equals(npc.npcId, npcId, StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }
    }
}
