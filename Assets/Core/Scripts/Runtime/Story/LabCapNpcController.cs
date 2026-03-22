using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ItemInteraction;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabCapNpcController : MonoBehaviour
    {
        private const string WalkPrefabAssetPath = "Assets/SM_Peer_Agent_Mo/PREFAB/MO_WALK.prefab";
        private const string DanceClipAssetPath = "Assets/Core/Art/3D Casual Character/Animation/Anim@Dance_1.FBX";
        private const string DanceClipName = "Dance_1";
        private const string MouthOpenMaterialAssetPath = "Assets/SM_Peer_Agent_Mo/Materials/M_MOUTH_OPEN.mat";

        [SerializeField] private StoryNpcAgent npcAgent;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private RuntimeAnimatorController idleController;
        [SerializeField] private RuntimeAnimatorController walkController;
        [SerializeField] private RuntimeAnimatorController danceController;
        [SerializeField] private AnimationClip danceClip;
        [SerializeField] private string danceStateName = "Dance_1";
        [SerializeField] private string conversationBoolName = "Conversation";
        [SerializeField] private string reactionTriggerName = "React";
        [SerializeField, Min(0.1f)] private float danceDurationSeconds = 2.8f;
        [SerializeField, Min(0.25f)] private float followDistance = 2.3f;
        [SerializeField, Min(0.25f)] private float stopDistance = 1.85f;
        [SerializeField, Min(0.1f)] private float followSideOffset = 0.95f;
        [SerializeField, Min(0.25f)] private float followMoveSpeed = 1.45f;
        [SerializeField, Min(30f)] private float followTurnSpeed = 420f;
        [SerializeField, Min(0.1f)] private float avoidanceProbeRadius = 0.22f;
        [SerializeField, Min(0.25f)] private float avoidanceProbeDistance = 0.9f;
        [SerializeField] private Material mouthOpenMaterial;

        public StoryNpcAgent NpcAgent => npcAgent;
        public float DanceDuration => Mathf.Max(0.1f, danceDurationSeconds);

        private Transform cachedPlayerTransform;
        private bool autoFollowPlayer;
        private bool followPlayer;
        private bool hasManualFollowOverride;
        private bool manualFollowOverrideValue;
        private bool conversationMode;
        private bool walking;
        private bool talking;
        private Coroutine danceRoutine;
        private AnimatorOverrideController runtimeDanceController;
        private Collider[] selfColliders;
        private Renderer[] mouthRenderers;
        private Material[][] defaultMouthMaterials;

        private void Awake()
        {
            ResolveReferences();
            ApplyRuntimeDefaults();
            EnsureInteractionCollider();
            ConfigureForLabMission();
        }

        private void Update()
        {
            if (!followPlayer || conversationMode)
            {
                SetWalkAnimationActive(false);
                return;
            }

            var player = ResolvePlayerTransform();
            if (player == null)
            {
                SetWalkAnimationActive(false);
                return;
            }

            var root = transform;
            var targetPosition = player.position - (player.forward * followDistance) + (player.right * followSideOffset);
            var delta = targetPosition - root.position;
            delta.y = 0f;
            var distance = delta.magnitude;
            var isMoving = distance > stopDistance;
            if (distance > stopDistance)
            {
                var desiredDirection = delta.normalized;
                var steeredDirection = ResolveObstacleAvoidedDirection(root.position, desiredDirection, Mathf.Max(avoidanceProbeDistance, stopDistance));
                var move = steeredDirection * (followMoveSpeed * Time.deltaTime);
                if (move.magnitude > distance - stopDistance)
                {
                    move = steeredDirection * Mathf.Max(0f, distance - stopDistance);
                }

                root.position += move;
            }

            var lookDirection = player.position - root.position;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                var targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                root.rotation = Quaternion.RotateTowards(root.rotation, targetRotation, followTurnSpeed * Time.deltaTime);
            }

            SetWalkAnimationActive(isMoving);
        }

        private void OnValidate()
        {
            ResolveReferences();
            ApplyRuntimeDefaults();
            EnsureInteractionCollider();
        }

        public void ConfigureForLabMission()
        {
            ResolveReferences();
            EnsureInteractionCollider();
            if (npcAgent == null)
            {
                return;
            }

            npcAgent.ConfigureNpc(
                "cap",
                "CAP",
                BuildDefaultOptions(),
                "You",
                "CAP looks ready to guide the tiny science mission.",
                2.5f,
                "lab",
                includeLookOption: true,
                includeInspectOption: false);

            npcAgent.ApplyNpcPresentation();
        }

        public void SetConversationMode(bool value)
        {
            conversationMode = value;
            SetWalkAnimationActive(false);
            if (animator == null)
            {
                return;
            }

            if (value)
            {
                SwitchAnimatorController(idleController);
            }

            if (HasParameter(conversationBoolName, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(conversationBoolName, value);
            }

            ApplyEffectiveFollowState();
        }

        public void PlayReaction()
        {
            if (animator == null)
            {
                return;
            }

            StopDanceRoutine();
            SetWalkAnimationActive(false);
            SwitchAnimatorController(idleController);

            if (HasParameter(reactionTriggerName, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(reactionTriggerName);
            }
        }

        public void SetFollowPlayer(bool value)
        {
            autoFollowPlayer = value;
            ApplyEffectiveFollowState();
        }

        public void SetManualFollowOverride(bool value)
        {
            hasManualFollowOverride = true;
            manualFollowOverrideValue = value;
            ApplyEffectiveFollowState();
        }

        public void ClearManualFollowOverride()
        {
            hasManualFollowOverride = false;
            ApplyEffectiveFollowState();
        }

        public void PlayDance()
        {
            ResolveReferences();
            ApplyRuntimeDefaults();
            if (animator == null)
            {
                return;
            }

            StopDanceRoutine();
            danceRoutine = StartCoroutine(PlayDanceRoutine());
        }

        public void SetTalking(bool value)
        {
            if (talking == value)
            {
                return;
            }

            talking = value;
            EnsureTalkingMaterials();
            ApplyTalkingState(value);
        }

        private void ResolveReferences()
        {
            npcAgent = npcAgent != null ? npcAgent : GetComponent<StoryNpcAgent>();
            animator = animator != null ? animator : GetComponentInChildren<Animator>(true);
            visualRoot = visualRoot != null ? visualRoot : transform;
            selfColliders = selfColliders != null && selfColliders.Length > 0 ? selfColliders : GetComponentsInChildren<Collider>(true);
        }

        private void EnsureInteractionCollider()
        {
            var rootCollider = GetComponent<Collider>();
            if (rootCollider == null)
            {
                var capsule = gameObject.AddComponent<CapsuleCollider>();
                capsule.radius = 0.35f;
                capsule.height = 1.7f;
                capsule.center = new Vector3(0f, 0.85f, 0f);
                capsule.direction = 1;
                rootCollider = capsule;
            }

            if (rootCollider is CapsuleCollider capsuleCollider)
            {
                capsuleCollider.isTrigger = false;
                capsuleCollider.radius = 0.35f;
                capsuleCollider.height = 1.7f;
                capsuleCollider.center = new Vector3(0f, 0.85f, 0f);
                capsuleCollider.direction = 1;
            }

            selfColliders = GetComponentsInChildren<Collider>(true);
        }

        private void ApplyRuntimeDefaults()
        {
            if (followDistance < 2f)
            {
                followDistance = 2.3f;
            }

            if (stopDistance < 1.5f)
            {
                stopDistance = 1.85f;
            }

            if (Mathf.Abs(followSideOffset) < 0.35f)
            {
                followSideOffset = 0.95f;
            }

            if (animator == null)
            {
                return;
            }

            animator.applyRootMotion = false;
            idleController = idleController != null ? idleController : animator.runtimeAnimatorController;
            walkController = walkController != null ? walkController : FindControllerByName("WALK");
            mouthOpenMaterial = mouthOpenMaterial != null ? mouthOpenMaterial : FindMaterialByName("M_MOUTH_OPEN");
#if UNITY_EDITOR
            if (walkController == null)
            {
                var walkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WalkPrefabAssetPath);
                var walkAnimator = walkPrefab != null ? walkPrefab.GetComponentInChildren<Animator>(true) : null;
                if (walkAnimator != null)
                {
                    walkController = walkAnimator.runtimeAnimatorController;
                }
            }

            if (mouthOpenMaterial == null)
            {
                mouthOpenMaterial = AssetDatabase.LoadAssetAtPath<Material>(MouthOpenMaterialAssetPath);
            }

            if (danceClip == null)
            {
                danceClip = LoadAnimationClipFromAsset(DanceClipAssetPath, DanceClipName);
            }
#endif

            if (danceClip == null)
            {
                danceClip = FindAnimationClipByName(DanceClipName);
            }

            var generatedDanceController = BuildDanceController();
            if (generatedDanceController != null)
            {
                danceController = generatedDanceController;
            }
        }

        private IEnumerator PlayDanceRoutine()
        {
            var wasFollowing = followPlayer;
            followPlayer = false;
            SetWalkAnimationActive(false);
            SwitchAnimatorController(danceController != null ? danceController : idleController);
            TryPlayDanceState();
            yield return new WaitForSeconds(Mathf.Max(0.1f, danceDurationSeconds));
            danceRoutine = null;
            if (!wasFollowing)
            {
                SwitchAnimatorController(idleController);
            }

            ApplyEffectiveFollowState();
        }

        private void StopDanceRoutine()
        {
            if (danceRoutine == null)
            {
                return;
            }

            StopCoroutine(danceRoutine);
            danceRoutine = null;
            if (animator != null)
            {
                SwitchAnimatorController(idleController);
            }
        }

        private void ApplyEffectiveFollowState()
        {
            var shouldFollow = hasManualFollowOverride ? manualFollowOverrideValue : autoFollowPlayer;
            if (shouldFollow)
            {
                StopDanceRoutine();
            }

            followPlayer = shouldFollow;
            if (!followPlayer)
            {
                SetWalkAnimationActive(false);
            }
        }

        private Transform ResolvePlayerTransform()
        {
            if (cachedPlayerTransform != null && cachedPlayerTransform.gameObject.activeInHierarchy)
            {
                return cachedPlayerTransform;
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                cachedPlayerTransform = taggedPlayer.transform;
                return cachedPlayerTransform;
            }

            return null;
        }

        private bool HasParameter(string parameterName, AnimatorControllerParameterType type)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
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

        private void SetWalkAnimationActive(bool value)
        {
            if (animator == null || walking == value)
            {
                return;
            }

            walking = value;
            if (value)
            {
                SwitchAnimatorController(walkController);
                return;
            }

            SwitchAnimatorController(idleController);
        }

        private void SwitchAnimatorController(RuntimeAnimatorController controller)
        {
            if (animator == null || controller == null || animator.runtimeAnimatorController == controller)
            {
                return;
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
        }

        private void TryPlayDanceState()
        {
            if (animator == null || string.IsNullOrWhiteSpace(danceStateName))
            {
                return;
            }

            animator.Play(0, 0, 0f);
            animator.Update(0f);
        }

        private Vector3 ResolveObstacleAvoidedDirection(Vector3 origin, Vector3 desiredDirection, float probeDistance)
        {
            if (desiredDirection.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            var flattenedDesired = Vector3.ProjectOnPlane(desiredDirection, Vector3.up).normalized;
            if (!IsDirectionBlocked(origin, flattenedDesired, probeDistance))
            {
                return flattenedDesired;
            }

            var candidateAngles = new[] { 32f, -32f, 56f, -56f, 80f, -80f };
            for (var index = 0; index < candidateAngles.Length; index++)
            {
                var steered = Quaternion.AngleAxis(candidateAngles[index], Vector3.up) * flattenedDesired;
                if (!IsDirectionBlocked(origin, steered, probeDistance))
                {
                    return steered.normalized;
                }
            }

            return Vector3.zero;
        }

        private bool IsDirectionBlocked(Vector3 origin, Vector3 direction, float probeDistance)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var ray = new Ray(origin + (Vector3.up * 0.35f), direction.normalized);
            var hits = Physics.SphereCastAll(ray, avoidanceProbeRadius, probeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var index = 0; index < hits.Length; index++)
            {
                var collider = hits[index].collider;
                if (collider == null || collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void EnsureTalkingMaterials()
        {
            if (mouthRenderers != null && defaultMouthMaterials != null)
            {
                return;
            }

            var targetRoot = visualRoot != null ? visualRoot : transform;
            var renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            var renderBuffer = new List<Renderer>(2);
            var materialsBuffer = new List<Material[]>(2);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (!IsMouthRenderer(renderer))
                {
                    continue;
                }

                renderBuffer.Add(renderer);
                materialsBuffer.Add(CloneMaterials(renderer.sharedMaterials));
            }

            mouthRenderers = renderBuffer.ToArray();
            defaultMouthMaterials = materialsBuffer.ToArray();
        }

        private void ApplyTalkingState(bool value)
        {
            if (mouthRenderers == null || defaultMouthMaterials == null)
            {
                return;
            }

            for (var rendererIndex = 0; rendererIndex < mouthRenderers.Length; rendererIndex++)
            {
                var renderer = mouthRenderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                var baseMaterials = defaultMouthMaterials[rendererIndex];
                if (baseMaterials == null || baseMaterials.Length == 0)
                {
                    continue;
                }

                if (!value || mouthOpenMaterial == null)
                {
                    renderer.sharedMaterials = CloneMaterials(baseMaterials);
                    continue;
                }

                var talkingMaterials = CloneMaterials(baseMaterials);
                for (var materialIndex = 0; materialIndex < talkingMaterials.Length; materialIndex++)
                {
                    talkingMaterials[materialIndex] = mouthOpenMaterial;
                }

                renderer.sharedMaterials = talkingMaterials;
            }
        }

        private static bool IsMouthRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            var transformName = renderer.transform.name;
            if (transformName.IndexOf("Mo_LP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transformName.IndexOf("mouth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var sharedMaterials = renderer.sharedMaterials;
            for (var index = 0; index < sharedMaterials.Length; index++)
            {
                var material = sharedMaterials[index];
                if (material != null && material.name.IndexOf("M_MOUTH", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Material[] CloneMaterials(Material[] source)
        {
            if (source == null)
            {
                return Array.Empty<Material>();
            }

            var clone = new Material[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static Material FindMaterialByName(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
            {
                return null;
            }

            var materials = Resources.FindObjectsOfTypeAll<Material>();
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && string.Equals(material.name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    return material;
                }
            }

            return null;
        }

        private static RuntimeAnimatorController FindControllerByName(string controllerName)
        {
            if (string.IsNullOrWhiteSpace(controllerName))
            {
                return null;
            }

            var candidates = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.name, controllerName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private RuntimeAnimatorController BuildDanceController()
        {
            if (idleController == null || danceClip == null)
            {
                return danceController;
            }

            if (runtimeDanceController != null && runtimeDanceController.runtimeAnimatorController == idleController)
            {
                return runtimeDanceController;
            }

            var overrideController = new AnimatorOverrideController(idleController);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
            overrideController.GetOverrides(overrides);
            if (overrides.Count == 0)
            {
                return danceController;
            }

            for (var index = 0; index < overrides.Count; index++)
            {
                overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, danceClip);
            }

            overrideController.ApplyOverrides(overrides);
            overrideController.name = $"{idleController.name}_LabDanceOverride";
            runtimeDanceController = overrideController;
            return runtimeDanceController;
        }

        private static AnimationClip FindAnimationClipByName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            var clips = Resources.FindObjectsOfTypeAll<AnimationClip>();
            for (var index = 0; index < clips.Length; index++)
            {
                var clip = clips[index];
                if (clip != null && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private static AnimationClip LoadAnimationClipFromAsset(string assetPath, string clipName)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase));
        }
#endif

        private static IEnumerable<StoryNpcOptionDefinition> BuildDefaultOptions()
        {
            return new[]
            {
                new StoryNpcOptionDefinition
                {
                    id = "talk",
                    label = "Talk",
                    slot = InteractionOptionSlot.Top,
                    visible = true,
                    enabled = true,
                    interactionId = "lab.cap.talk"
                }
            };
        }
    }
}
