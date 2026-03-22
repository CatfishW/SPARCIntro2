using System;
using System.Collections;
using Blocks.Gameplay.Core;
using ItemInteraction;
using ModularStoryFlow.Runtime.Bridges;
using ModularStoryFlow.Runtime.Channels;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Player;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class LabShrinkSequenceController : MonoBehaviour
    {
        public const string DefaultShrinkTimelineCueId = "lab.shrink.cutscene";
        public const string DefaultShrinkTimelineCueDisplayName = "Lab Shrink Cutscene";

        [SerializeField] private ClassroomPlayerControlLock controlLock;
        [SerializeField] private InteractionDirector interactionDirector;
        [SerializeField] private StoryFlowPlayer storyFlowPlayer;
        [SerializeField] private StoryTimelineDirectorBridge timelineBridge;
        [SerializeField] private LabSceneContext sceneContext;
        [SerializeField] private PlayableAsset shrinkTimelinePlayable;
        [SerializeField] private string shrinkTimelineCueId = DefaultShrinkTimelineCueId;
        [SerializeField] private string shrinkTimelineCueDisplayName = DefaultShrinkTimelineCueDisplayName;
        [SerializeField, Min(0.5f)] private float timelineFallbackDurationSeconds = 3.6f;
        [SerializeField] private Transform shrinkPlayerAnchor;
        [SerializeField, Min(0.05f)] private float minimumActorScale = 0.08f;
        [SerializeField, Min(0.05f)] private float maximumActorScale = 2f;
        [SerializeField] private Color overlayColor = new Color(0.22f, 0.16f, 0.1f, 1f);
        [SerializeField] private string overlayTextureResourcePath = "Story/Generated/LabShrinkEnergyTexture";
        [SerializeField] private string sweepTextureResourcePath = "Story/Generated/LabShrinkSweepTexture";
        [SerializeField] private string particleTextureResourcePath = "Story/Generated/LabShrinkParticleTexture";
        [SerializeField, Min(20f)] private float establishingShotFov = 62f;
        [SerializeField, Min(20f)] private float orbitShotFov = 54f;
        [SerializeField, Min(20f)] private float overheadShotFov = 49f;
        [SerializeField, Min(20f)] private float heroShotFov = 44f;
        [SerializeField, Min(0f)] private float handheldAmplitude = 0.03f;
        [SerializeField, Min(0f)] private float handheldFrequency = 2.7f;
        [SerializeField, Min(0.15f)] private float postShrinkCameraDistance = 0.48f;
        [SerializeField, Min(0.01f)] private float postShrinkCameraHeight = 0.045f;
        [SerializeField, Min(0f)] private float postShrinkLookHeight = 0.02f;
        [SerializeField, Min(25f)] private float postShrinkGameplayFov = 57f;
        [SerializeField] private Color playerShrinkVfxColor = new Color(1f, 0.76f, 0.39f, 0.92f);
        [SerializeField] private Color capShrinkVfxColor = new Color(0.82f, 0.93f, 0.67f, 0.9f);

        private Canvas overlayCanvas;
        private CanvasGroup overlayGroup;
        private Image overlayPanelImage;
        private Image overlayVignetteImage;
        private Image overlayGlowImage;
        private Image overlaySweepImage;
        private RectTransform overlaySweepRect;
        private Sprite overlayPanelSprite;
        private Sprite overlaySweepSprite;
        private Material runtimeParticleMaterial;
        private Text messageText;
        private Text detailText;
        private Coroutine activeRoutine;
        private StoryTimelineResultChannel activeTimelineResults;
        private string activeTimelineRequestId;
        private bool timelineRequestCompleted;
        private bool timelineRequestSucceeded;
        private bool ownsRuntimeTimelinePlayable;

        private bool timelineAnimationActive;
        private AnimationCurve timelineCurve;
        private float timelineDurationSeconds;
        private float timelinePlayerDelay;
        private float timelineCapDelay;
        private CoreMovement localPlayerMovement;
        private Transform capRoot;
        private Vector3 playerStartScale;
        private Vector3 capStartScale;
        private Vector3 playerTargetScale;
        private Vector3 capTargetScale;
        private Vector3 playerStartPosition;
        private Quaternion playerStartRotation;
        private bool hasPlayerStartPose;
        private Camera gameplayCamera;
        private Camera cinematicCamera;
        private bool gameplayCameraWasEnabled;
        private Vector3 gameplayCameraStartPosition;
        private Quaternion gameplayCameraStartRotation;
        private float gameplayCameraStartFov;
        private Transform shrinkVfxRoot;
        private ParticleSystem playerShrinkParticles;
        private ParticleSystem capShrinkParticles;
        private ParticleSystem centerShrinkParticles;

        public event Action Completed;
        public bool IsPlaying => activeRoutine != null;
        internal static LabShrinkSequenceController ActiveInstance { get; private set; }

        private void Awake()
        {
            ActiveInstance = this;
            EnsureOverlay();
            SetOverlayAlpha(0f);
        }

        private void OnEnable()
        {
            ActiveInstance = this;
        }

        private void OnDisable()
        {
            if (activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
                activeRoutine = null;
            }

            UnregisterTimelineResultChannel();
            EndTimelineDrivenShrink(completed: false);
            controlLock?.ForceReleaseAll();
            interactionDirector?.SetInteractionsLocked(false);
            StopShrinkVfx(immediate: true);
            SetOverlayAlpha(0f);

            if (ReferenceEquals(ActiveInstance, this))
            {
                ActiveInstance = null;
            }
        }

        private void OnDestroy()
        {
            if (ownsRuntimeTimelinePlayable && shrinkTimelinePlayable != null)
            {
                Destroy(shrinkTimelinePlayable);
                shrinkTimelinePlayable = null;
                ownsRuntimeTimelinePlayable = false;
            }

            if (overlayPanelSprite != null)
            {
                Destroy(overlayPanelSprite);
                overlayPanelSprite = null;
            }

            if (overlaySweepSprite != null)
            {
                Destroy(overlaySweepSprite);
                overlaySweepSprite = null;
            }

            if (runtimeParticleMaterial != null)
            {
                Destroy(runtimeParticleMaterial);
                runtimeParticleMaterial = null;
            }

            if (ReferenceEquals(ActiveInstance, this))
            {
                ActiveInstance = null;
            }
        }

        public void PlaySequence()
        {
            if (activeRoutine != null)
            {
                return;
            }

            activeRoutine = StartCoroutine(PlayRoutine());
        }

        public void BeginTimelineDrivenShrink(
            float durationSeconds,
            float playerDelay,
            float capDelay,
            AnimationCurve shrinkProgressCurve)
        {
            ResolveRuntimeReferences();
            PrepareShrinkTargets();

            timelineCurve = shrinkProgressCurve ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            timelineDurationSeconds = Mathf.Max(0.5f, durationSeconds);
            timelinePlayerDelay = Mathf.Clamp01(playerDelay);
            timelineCapDelay = Mathf.Clamp01(capDelay);
            timelineAnimationActive = true;
            ActivateCinematicCamera();
            EnsureShrinkVfx();
            UpdateShrinkVfx(0f);
            PlayShrinkVfx();

            if (messageText != null)
            {
                messageText.text = "Shrink field charging...";
            }

            if (detailText != null)
            {
                detailText.text = "Hold still while CAP calibrates the compression beam.";
            }

            SetOverlayAlpha(0f);
        }

        public void EvaluateTimelineDrivenShrink(float normalizedTime)
        {
            if (!timelineAnimationActive)
            {
                return;
            }

            var t = Mathf.Clamp01(normalizedTime);
            UpdateCinematicCameraShot(t);
            var alphaIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.22f, t));
            var alphaOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, t));
            var pulse = 0.88f + (Mathf.Sin(t * Mathf.PI * 8f) * 0.12f);
            var overlayAlpha = alphaIn * alphaOut * pulse;
            SetOverlayAlpha(overlayAlpha);
            UpdateOverlayEffects(t, overlayAlpha);
            UpdateShrinkVfx(t);

            if (messageText != null)
            {
                messageText.text = ResolveStatusLine(t);
            }

            if (detailText != null)
            {
                detailText.text = ResolveDetailLine(t);
            }

            var playerProgress = EvaluateShrinkProgress(t, timelinePlayerDelay);
            var capProgress = EvaluateShrinkProgress(t, timelineCapDelay);

            if (localPlayerMovement != null)
            {
                localPlayerMovement.transform.localScale = Vector3.LerpUnclamped(playerStartScale, playerTargetScale, playerProgress);
                UpdatePlayerPose(t);
            }

            if (capRoot != null)
            {
                capRoot.localScale = Vector3.LerpUnclamped(capStartScale, capTargetScale, capProgress);
            }
        }

        public void EndTimelineDrivenShrink(bool completed)
        {
            if (!timelineAnimationActive)
            {
                return;
            }

            if (localPlayerMovement != null)
            {
                localPlayerMovement.transform.localScale = completed ? playerTargetScale : playerStartScale;
                if (completed && shrinkPlayerAnchor != null)
                {
                    localPlayerMovement.transform.rotation = shrinkPlayerAnchor.rotation;
                    localPlayerMovement.SetPosition(shrinkPlayerAnchor.position);
                    localPlayerMovement.ResetMovementForces();
                }
            }

            if (capRoot != null)
            {
                capRoot.localScale = completed ? capTargetScale : capStartScale;
            }

            timelineAnimationActive = false;
            DeactivateCinematicCamera(completed);
            StopShrinkVfx(immediate: !completed);

            if (completed)
            {
                if (messageText != null)
                {
                    messageText.text = "Tiny mode complete!";
                }

                if (detailText != null)
                {
                    detailText.text = "You and CAP are now rocket-sized.";
                }
            }

            SetOverlayAlpha(0f);
            UpdateOverlayEffects(0f, 0f);
        }

        private IEnumerator PlayRoutine()
        {
            ResolveRuntimeReferences();
            interactionDirector?.SetInteractionsLocked(true);
            controlLock?.Acquire(unlockCursor: false);
            SetOverlayAlpha(0f);

            var playedViaTimelineBridge = false;
            if (TryStartTimelineRequest())
            {
                playedViaTimelineBridge = true;
                var timeoutSeconds = Mathf.Max(1f, timelineFallbackDurationSeconds + 3f);
                while (!timelineRequestCompleted && timeoutSeconds > 0f)
                {
                    timeoutSeconds -= Time.unscaledDeltaTime;
                    yield return null;
                }

                var succeeded = timelineRequestCompleted && timelineRequestSucceeded;
                UnregisterTimelineResultChannel();
                if (!succeeded)
                {
                    playedViaTimelineBridge = false;
                    EndTimelineDrivenShrink(completed: false);
                }
            }

            if (!playedViaTimelineBridge)
            {
                yield return PlayFallbackTimelineRoutine();
            }

            interactionDirector?.SetInteractionsLocked(false);
            controlLock?.ForceReleaseAll();
            activeRoutine = null;
            Completed?.Invoke();
        }

        private IEnumerator PlayFallbackTimelineRoutine()
        {
            BeginTimelineDrivenShrink(
                timelineFallbackDurationSeconds,
                playerDelay: 0.08f,
                capDelay: 0.2f,
                shrinkProgressCurve: null);

            var elapsed = 0f;
            while (elapsed < timelineFallbackDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / timelineFallbackDurationSeconds);
                EvaluateTimelineDrivenShrink(t);
                yield return null;
            }

            EndTimelineDrivenShrink(completed: true);
            yield return new WaitForSecondsRealtime(0.08f);
        }

        private bool TryStartTimelineRequest()
        {
            if (storyFlowPlayer == null || storyFlowPlayer.ProjectConfig == null)
            {
                return false;
            }

            var projectConfig = storyFlowPlayer.ProjectConfig;
            var channels = projectConfig.Channels;
            var timelineRequests = channels?.TimelineRequests;
            var timelineResults = channels?.TimelineResults;
            if (timelineRequests == null || timelineResults == null)
            {
                return false;
            }

            var playableAsset = EnsureTimelinePlayable();
            if (playableAsset == null)
            {
                return false;
            }

            var cueId = string.IsNullOrWhiteSpace(shrinkTimelineCueId) ? DefaultShrinkTimelineCueId : shrinkTimelineCueId.Trim();
            var cueDisplayName = string.IsNullOrWhiteSpace(shrinkTimelineCueDisplayName) ? DefaultShrinkTimelineCueDisplayName : shrinkTimelineCueDisplayName.Trim();
            projectConfig.TimelineCatalog?.AddOrReplaceBinding(cueId, cueDisplayName, playableAsset);
            timelineBridge?.Configure(projectConfig);

            timelineRequestCompleted = false;
            timelineRequestSucceeded = false;
            activeTimelineRequestId = Guid.NewGuid().ToString("N");
            activeTimelineResults = timelineResults;
            activeTimelineResults.Register(HandleTimelineResult);

            timelineRequests.Raise(new StoryTimelineRequest
            {
                SessionId = storyFlowPlayer.SessionId,
                RequestId = activeTimelineRequestId,
                GraphId = storyFlowPlayer.InitialGraph != null ? storyFlowPlayer.InitialGraph.GraphId : string.Empty,
                NodeId = "lab.shrink.sequence",
                CueId = cueId,
                CueDisplayName = cueDisplayName,
                WaitForCompletion = true
            });

            return true;
        }

        private void HandleTimelineResult(StoryTimelineResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(activeTimelineRequestId))
            {
                return;
            }

            if (!string.Equals(result.RequestId, activeTimelineRequestId, StringComparison.Ordinal))
            {
                return;
            }

            timelineRequestCompleted = true;
            timelineRequestSucceeded = result.Completed;
        }

        private void UnregisterTimelineResultChannel()
        {
            if (activeTimelineResults != null)
            {
                activeTimelineResults.Unregister(HandleTimelineResult);
                activeTimelineResults = null;
            }

            activeTimelineRequestId = string.Empty;
        }

        private PlayableAsset EnsureTimelinePlayable()
        {
            if (shrinkTimelinePlayable != null)
            {
                return shrinkTimelinePlayable;
            }

            var runtimePlayable = ScriptableObject.CreateInstance<LabShrinkTimelinePlayableAsset>();
            runtimePlayable.name = "LabShrinkTimelineRuntime";
            runtimePlayable.hideFlags = HideFlags.HideAndDontSave;
            shrinkTimelinePlayable = runtimePlayable;
            ownsRuntimeTimelinePlayable = true;
            return shrinkTimelinePlayable;
        }

        private void PrepareShrinkTargets()
        {
            sceneContext?.ResolveRuntimeReferences();
            TryResolveLocalPlayerMovement(out localPlayerMovement);

            capRoot = sceneContext?.CapNpc != null
                ? sceneContext.CapNpc.transform
                : sceneContext?.CapNpcController != null
                    ? sceneContext.CapNpcController.transform
                    : ResolveCapTransformByName();

            var rocketTransform = sceneContext?.RocketInteractable != null ? sceneContext.RocketInteractable.transform : null;
            var rocketHeight = ResolveRocketHeight(rocketTransform);

            if (localPlayerMovement != null)
            {
                var playerTransform = localPlayerMovement.transform;
                playerStartScale = playerTransform.localScale;
                playerStartPosition = playerTransform.position;
                playerStartRotation = playerTransform.rotation;
                hasPlayerStartPose = true;
                playerTargetScale = ResolveTargetScale(playerTransform, playerStartScale, rocketHeight);
            }
            else
            {
                hasPlayerStartPose = false;
                playerStartScale = Vector3.one;
                playerTargetScale = Vector3.one;
            }

            if (capRoot != null)
            {
                capStartScale = capRoot.localScale;
                capTargetScale = ResolveTargetScale(capRoot, capStartScale, rocketHeight);
            }
            else
            {
                capStartScale = Vector3.one;
                capTargetScale = Vector3.one;
            }
        }

        private void UpdatePlayerPose(float normalizedTime)
        {
            if (!hasPlayerStartPose || localPlayerMovement == null || shrinkPlayerAnchor == null)
            {
                return;
            }

            var poseBlend = Mathf.Clamp01(Mathf.InverseLerp(0.4f, 0.78f, normalizedTime));
            var easedBlend = Mathf.SmoothStep(0f, 1f, poseBlend);
            localPlayerMovement.transform.rotation = Quaternion.Slerp(playerStartRotation, shrinkPlayerAnchor.rotation, easedBlend);
            localPlayerMovement.SetPosition(Vector3.Lerp(playerStartPosition, shrinkPlayerAnchor.position, easedBlend));
            if (poseBlend > 0.995f)
            {
                localPlayerMovement.ResetMovementForces();
            }
        }

        private float EvaluateShrinkProgress(float globalProgress, float actorDelay)
        {
            var delayed = Mathf.Clamp01(Mathf.InverseLerp(actorDelay, 0.97f, globalProgress));
            var curved = timelineCurve != null ? timelineCurve.Evaluate(delayed) : delayed;
            return Mathf.Clamp01(curved);
        }

        private float ResolveRocketHeight(Transform rocketTransform)
        {
            if (rocketTransform != null && TryComputeCombinedHeight(rocketTransform, out var rocketHeight))
            {
                return Mathf.Clamp(rocketHeight, 0.2f, 2.4f);
            }

            return 0.55f;
        }

        private Vector3 ResolveTargetScale(Transform actorRoot, Vector3 startScale, float targetHeight)
        {
            if (actorRoot == null || targetHeight <= 0f)
            {
                return startScale;
            }

            if (!TryComputeCombinedHeight(actorRoot, out var actorHeight) || actorHeight < 0.001f)
            {
                return startScale;
            }

            var averageScale = Mathf.Max(0.001f, (startScale.x + startScale.y + startScale.z) / 3f);
            var scaled = averageScale * (targetHeight / actorHeight);
            var clamped = Mathf.Clamp(scaled, minimumActorScale, maximumActorScale);
            return Vector3.one * clamped;
        }

        private static bool TryComputeCombinedHeight(Transform root, out float height)
        {
            height = 0f;
            if (root == null)
            {
                return false;
            }

            var hasBounds = false;
            Bounds bounds = default;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
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

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            var characterControllers = root.GetComponentsInChildren<CharacterController>(true);
            for (var index = 0; index < characterControllers.Length; index++)
            {
                var controller = characterControllers[index];
                if (controller == null || !controller.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = controller.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(controller.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            height = Mathf.Max(0f, bounds.size.y);
            return height > 0.001f;
        }

        private static Transform ResolveCapTransformByName()
        {
            var cap = GameObject.Find("CAP");
            return cap != null ? cap.transform : null;
        }

        private static string ResolveStatusLine(float normalizedTime)
        {
            if (normalizedTime < 0.2f)
            {
                return "Shrink field charging...";
            }

            if (normalizedTime < 0.84f)
            {
                var progress = Mathf.RoundToInt(Mathf.Lerp(7f, 99f, Mathf.InverseLerp(0.2f, 0.84f, normalizedTime)));
                return $"Molecular compression {progress}%";
            }

            return "Rocket-scale lock acquired.";
        }

        private static string ResolveDetailLine(float normalizedTime)
        {
            if (normalizedTime < 0.45f)
            {
                return "Stabilizing player + CAP mass profile.";
            }

            if (normalizedTime < 0.84f)
            {
                return "Compressing both rigs to match rocket scale.";
            }

            return "Scale match complete. Ready for launch.";
        }

        private void ActivateCinematicCamera()
        {
            gameplayCamera = gameplayCamera != null ? gameplayCamera : Camera.main;
            if (gameplayCamera == null)
            {
                return;
            }

            if (cinematicCamera == null)
            {
                var cameraObject = new GameObject("LabShrinkCinematicCamera", typeof(Camera));
                cameraObject.transform.SetParent(transform, false);
                cinematicCamera = cameraObject.GetComponent<Camera>();
                cinematicCamera.enabled = false;
            }

            gameplayCameraStartPosition = gameplayCamera.transform.position;
            gameplayCameraStartRotation = gameplayCamera.transform.rotation;
            gameplayCameraWasEnabled = gameplayCamera.enabled;
            gameplayCameraStartFov = gameplayCamera.fieldOfView;

            cinematicCamera.CopyFrom(gameplayCamera);
            cinematicCamera.depth = gameplayCamera.depth + 20f;
            cinematicCamera.transform.position = gameplayCameraStartPosition;
            cinematicCamera.transform.rotation = gameplayCameraStartRotation;
            cinematicCamera.fieldOfView = gameplayCameraStartFov;
            cinematicCamera.enabled = true;
            gameplayCamera.enabled = false;
        }

        private void DeactivateCinematicCamera(bool completed)
        {
            if (cinematicCamera != null)
            {
                cinematicCamera.enabled = false;
            }

            if (gameplayCamera != null)
            {
                if (completed && TryResolvePostShrinkCameraPose(out var postPosition, out var postRotation))
                {
                    gameplayCamera.transform.SetPositionAndRotation(postPosition, postRotation);
                    gameplayCamera.fieldOfView = Mathf.Clamp(postShrinkGameplayFov, 30f, 85f);
                }
                else
                {
                    gameplayCamera.transform.SetPositionAndRotation(gameplayCameraStartPosition, gameplayCameraStartRotation);
                    gameplayCamera.fieldOfView = gameplayCameraStartFov;
                }

                gameplayCamera.enabled = gameplayCameraWasEnabled;
            }
        }

        private bool TryResolvePostShrinkCameraPose(out Vector3 position, out Quaternion rotation)
        {
            position = gameplayCameraStartPosition;
            rotation = gameplayCameraStartRotation;

            var playerTransform = localPlayerMovement != null ? localPlayerMovement.transform : shrinkPlayerAnchor;
            if (playerTransform == null)
            {
                return false;
            }

            var playerPoint = playerTransform.position;
            var playerHeight = ResolveActorCameraHeight(playerTransform);
            var lookHeight = Mathf.Clamp(postShrinkLookHeight + (playerHeight * 0.22f), 0.02f, 0.085f);
            var lookTarget = playerPoint + (Vector3.up * lookHeight);
            if (capRoot != null)
            {
                var capHeight = ResolveActorCameraHeight(capRoot);
                var capTargetHeight = Mathf.Clamp(capHeight * 0.23f, 0.02f, 0.08f);
                var capTarget = capRoot.position + (Vector3.up * capTargetHeight);
                lookTarget = Vector3.Lerp(lookTarget, capTarget, 0.27f);
            }

            var retreatDirection = capRoot != null
                ? Vector3.ProjectOnPlane(playerPoint - capRoot.position, Vector3.up)
                : Vector3.ProjectOnPlane(gameplayCameraStartPosition - playerPoint, Vector3.up);

            if (retreatDirection.sqrMagnitude < 0.0001f)
            {
                retreatDirection = -Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up);
            }

            if (retreatDirection.sqrMagnitude < 0.0001f)
            {
                retreatDirection = Vector3.back;
            }

            retreatDirection.Normalize();
            var resolvedCameraDistance = Mathf.Clamp(postShrinkCameraDistance, 0.24f, 1.35f);
            var resolvedCameraHeight = Mathf.Clamp(postShrinkCameraHeight, 0.015f, 0.055f);
            position = playerPoint + (retreatDirection * resolvedCameraDistance) + (Vector3.up * resolvedCameraHeight);
            var lookDirection = lookTarget - position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                lookDirection = Vector3.forward;
            }

            rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            return true;
        }

        private static float ResolveActorCameraHeight(Transform actor)
        {
            if (actor != null && TryComputeCombinedHeight(actor, out var measured))
            {
                return Mathf.Clamp(measured, 0.04f, 0.35f);
            }

            return 0.12f;
        }

        private void UpdateCinematicCameraShot(float normalizedTime)
        {
            if (cinematicCamera == null || !cinematicCamera.enabled)
            {
                return;
            }

            var focusPoint = ResolveCameraFocusPoint();
            var basis = shrinkPlayerAnchor != null
                ? shrinkPlayerAnchor
                : localPlayerMovement != null
                    ? localPlayerMovement.transform
                    : transform;

            var forward = Vector3.ProjectOnPlane(basis.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(gameplayCameraStartRotation * Vector3.forward, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            var establishingPosition = focusPoint - (forward * 3.1f) - (right * 1.8f) + (Vector3.up * 1.85f);
            var orbitPosition = focusPoint - (forward * 1.05f) + (right * 2.45f) + (Vector3.up * 1.4f);
            var overheadPosition = focusPoint - (forward * 0.2f) + (Vector3.up * 3.15f);
            var heroFocusPoint = ResolveHeroFocusPoint(focusPoint);
            var heroPosition = heroFocusPoint - (forward * 1.15f) + (right * 0.95f) + (Vector3.up * 1.2f);

            var establishingLookPoint = focusPoint + (Vector3.up * 0.7f);
            var orbitLookPoint = focusPoint + (Vector3.up * 0.6f);
            var overheadLookPoint = focusPoint + (Vector3.up * 0.2f);
            var heroLookPoint = heroFocusPoint + (Vector3.up * 0.45f);
            var establishingRotation = Quaternion.LookRotation((establishingLookPoint - establishingPosition).normalized, Vector3.up);
            var orbitRotation = Quaternion.LookRotation((orbitLookPoint - orbitPosition).normalized, Vector3.up);
            var overheadRotation = Quaternion.LookRotation((overheadLookPoint - overheadPosition).normalized, Vector3.up);
            var heroRotation = Quaternion.LookRotation((heroLookPoint - heroPosition).normalized, Vector3.up);

            Vector3 position;
            Quaternion rotation;
            float targetFov;
            if (normalizedTime < 0.2f)
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.2f, normalizedTime));
                position = Vector3.Lerp(gameplayCameraStartPosition, establishingPosition, blend);
                rotation = Quaternion.Slerp(gameplayCameraStartRotation, establishingRotation, blend);
                targetFov = Mathf.Lerp(gameplayCameraStartFov, establishingShotFov, blend);
            }
            else if (normalizedTime < 0.55f)
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.55f, normalizedTime));
                position = Vector3.Lerp(establishingPosition, orbitPosition, blend);
                rotation = Quaternion.Slerp(establishingRotation, orbitRotation, blend);
                targetFov = Mathf.Lerp(establishingShotFov, orbitShotFov, blend);
            }
            else if (normalizedTime < 0.82f)
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.82f, normalizedTime));
                position = Vector3.Lerp(orbitPosition, overheadPosition, blend);
                rotation = Quaternion.Slerp(orbitRotation, overheadRotation, blend);
                targetFov = Mathf.Lerp(orbitShotFov, overheadShotFov, blend);
            }
            else
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 1f, normalizedTime));
                position = Vector3.Lerp(overheadPosition, heroPosition, blend);
                rotation = Quaternion.Slerp(overheadRotation, heroRotation, blend);
                targetFov = Mathf.Lerp(overheadShotFov, heroShotFov, blend);
            }

            ApplyHandheldMotion(normalizedTime, ref position, ref rotation);
            cinematicCamera.transform.SetPositionAndRotation(position, rotation);
            cinematicCamera.fieldOfView = Mathf.Lerp(cinematicCamera.fieldOfView, targetFov, 0.65f);
        }

        private Vector3 ResolveHeroFocusPoint(Vector3 fallbackFocusPoint)
        {
            if (shrinkPlayerAnchor != null)
            {
                return shrinkPlayerAnchor.position;
            }

            if (localPlayerMovement != null && sceneContext?.RocketInteractable != null)
            {
                var actorPoint = localPlayerMovement.transform.position;
                var rocketPoint = sceneContext.RocketInteractable.transform.position;
                return Vector3.Lerp(actorPoint, rocketPoint, 0.5f);
            }

            return fallbackFocusPoint;
        }

        private void ApplyHandheldMotion(float normalizedTime, ref Vector3 position, ref Quaternion rotation)
        {
            if (handheldAmplitude <= 0.0001f || handheldFrequency <= 0.0001f)
            {
                return;
            }

            var envelope = 1f - Mathf.Abs((normalizedTime * 2f) - 1f);
            if (envelope <= 0.0001f)
            {
                return;
            }

            var phase = Time.unscaledTime * handheldFrequency;
            var lateral = Mathf.Sin(phase * 1.9f) * handheldAmplitude * envelope;
            var vertical = Mathf.Sin((phase + 0.8f) * 2.35f) * handheldAmplitude * 0.65f * envelope;
            var rollDegrees = Mathf.Sin((phase + 1.2f) * 1.5f) * 0.8f * envelope;

            position += (rotation * Vector3.right * lateral) + (rotation * Vector3.up * vertical);
            rotation *= Quaternion.Euler(0f, 0f, rollDegrees);
        }

        private Vector3 ResolveCameraFocusPoint()
        {
            if (localPlayerMovement != null && capRoot != null)
            {
                return (localPlayerMovement.transform.position + capRoot.position) * 0.5f;
            }

            if (localPlayerMovement != null)
            {
                return localPlayerMovement.transform.position;
            }

            if (capRoot != null)
            {
                return capRoot.position;
            }

            return shrinkPlayerAnchor != null ? shrinkPlayerAnchor.position : transform.position;
        }

        private void ResolveRuntimeReferences()
        {
            controlLock = controlLock != null ? controlLock : FindFirstObjectByType<ClassroomPlayerControlLock>(FindObjectsInactive.Include);
            interactionDirector = interactionDirector != null ? interactionDirector : FindFirstObjectByType<InteractionDirector>(FindObjectsInactive.Include);
            storyFlowPlayer = storyFlowPlayer != null ? storyFlowPlayer : FindFirstObjectByType<StoryFlowPlayer>(FindObjectsInactive.Include);
            timelineBridge = timelineBridge != null ? timelineBridge : FindFirstObjectByType<StoryTimelineDirectorBridge>(FindObjectsInactive.Include);
            sceneContext = sceneContext != null ? sceneContext : FindFirstObjectByType<LabSceneContext>(FindObjectsInactive.Include);
            sceneContext?.ResolveRuntimeReferences();
            shrinkPlayerAnchor = shrinkPlayerAnchor != null ? shrinkPlayerAnchor : sceneContext?.ShrinkPlayerAnchor;
        }

        private void EnsureOverlay()
        {
            if (overlayCanvas != null && overlayGroup != null && messageText != null && detailText != null)
            {
                return;
            }

            var canvasObject = new GameObject("LabShrinkCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 930;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            overlayGroup = canvasObject.AddComponent<CanvasGroup>();
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;

            var panel = new GameObject("FlashPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            overlayPanelImage = panel.GetComponent<Image>();
            overlayPanelImage.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0.24f);
            TryApplyOverlayTexture(overlayPanelImage, overlayTextureResourcePath, ref overlayPanelSprite);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var vignetteObject = new GameObject("EdgeVignette", typeof(RectTransform), typeof(Image));
            vignetteObject.transform.SetParent(panel.transform, false);
            overlayVignetteImage = vignetteObject.GetComponent<Image>();
            overlayVignetteImage.color = new Color(0.05f, 0.03f, 0.02f, 0.44f);
            overlayVignetteImage.raycastTarget = false;
            var vignetteRect = vignetteObject.GetComponent<RectTransform>();
            vignetteRect.anchorMin = Vector2.zero;
            vignetteRect.anchorMax = Vector2.one;
            vignetteRect.offsetMin = Vector2.zero;
            vignetteRect.offsetMax = Vector2.zero;

            var sweepObject = new GameObject("IonSweep", typeof(RectTransform), typeof(Image));
            sweepObject.transform.SetParent(panel.transform, false);
            overlaySweepImage = sweepObject.GetComponent<Image>();
            overlaySweepImage.color = new Color(0.97f, 0.83f, 0.5f, 0.12f);
            TryApplyOverlayTexture(overlaySweepImage, sweepTextureResourcePath, ref overlaySweepSprite);
            overlaySweepImage.raycastTarget = false;
            overlaySweepRect = sweepObject.GetComponent<RectTransform>();
            overlaySweepRect.anchorMin = new Vector2(0.5f, 0.5f);
            overlaySweepRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlaySweepRect.pivot = new Vector2(0.5f, 0.5f);
            overlaySweepRect.sizeDelta = new Vector2(1820f, 240f);
            overlaySweepRect.anchoredPosition = new Vector2(-1180f, 0f);
            overlaySweepRect.localRotation = Quaternion.Euler(0f, 0f, -18f);

            var glowObject = new GameObject("CenterGlow", typeof(RectTransform), typeof(Image));
            glowObject.transform.SetParent(panel.transform, false);
            overlayGlowImage = glowObject.GetComponent<Image>();
            overlayGlowImage.color = new Color(0.99f, 0.73f, 0.36f, 0.2f);
            if (overlayPanelSprite != null)
            {
                overlayGlowImage.sprite = overlayPanelSprite;
            }
            overlayGlowImage.raycastTarget = false;
            var glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.sizeDelta = new Vector2(1020f, 640f);
            glowRect.anchoredPosition = Vector2.zero;

            var messageObject = new GameObject("Message", typeof(RectTransform), typeof(Text));
            messageObject.transform.SetParent(panel.transform, false);
            messageText = messageObject.GetComponent<Text>();
            messageText.alignment = TextAnchor.MiddleCenter;
            messageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            messageText.fontSize = 50;
            messageText.fontStyle = FontStyle.Bold;
            messageText.color = Color.white;
            var messageOutline = messageObject.AddComponent<Outline>();
            messageOutline.effectColor = new Color(0.2f, 0.09f, 0.03f, 0.95f);
            messageOutline.effectDistance = new Vector2(2f, -2f);
            var messageRect = messageObject.GetComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0.5f, 0.5f);
            messageRect.anchorMax = new Vector2(0.5f, 0.5f);
            messageRect.pivot = new Vector2(0.5f, 0.5f);
            messageRect.sizeDelta = new Vector2(1160f, 150f);
            messageRect.anchoredPosition = new Vector2(0f, 12f);

            var detailObject = new GameObject("Detail", typeof(RectTransform), typeof(Text));
            detailObject.transform.SetParent(panel.transform, false);
            detailText = detailObject.GetComponent<Text>();
            detailText.alignment = TextAnchor.MiddleCenter;
            detailText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            detailText.fontSize = 28;
            detailText.color = new Color(1f, 0.93f, 0.82f, 1f);
            var detailRect = detailObject.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailRect.pivot = new Vector2(0.5f, 0.5f);
            detailRect.sizeDelta = new Vector2(1180f, 100f);
            detailRect.anchoredPosition = new Vector2(0f, -64f);
        }

        private void TryApplyOverlayTexture(Image image, string resourcePath, ref Sprite runtimeSprite)
        {
            if (image == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            var texture = Resources.Load<Texture2D>(resourcePath.Trim());
            if (texture == null)
            {
                return;
            }

            if (runtimeSprite != null)
            {
                Destroy(runtimeSprite);
                runtimeSprite = null;
            }

            runtimeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            image.sprite = runtimeSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
        }

        private void ApplyParticleTexture(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                return;
            }

            var material = ResolveOrCreateParticleMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
                renderer.trailMaterial = material;
            }
        }

        private Material ResolveOrCreateParticleMaterial()
        {
            if (runtimeParticleMaterial != null)
            {
                return runtimeParticleMaterial;
            }

            var shader = FindFirstAvailableShader(
                "Universal Render Pipeline/Particles/Unlit",
                "Universal Render Pipeline/Unlit",
                "Particles/Standard Unlit",
                "Sprites/Default",
                "Particles/Additive",
                "Legacy Shaders/Particles/Additive");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = "LabShrinkRuntimeParticleMaterial"
            };

            var particleTexture = string.IsNullOrWhiteSpace(particleTextureResourcePath)
                ? null
                : Resources.Load<Texture2D>(particleTextureResourcePath.Trim());
            AssignTextureToParticleMaterial(material, particleTexture);

            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", Color.white);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            runtimeParticleMaterial = material;
            return runtimeParticleMaterial;
        }

        private static void AssignTextureToParticleMaterial(Material material, Texture2D texture)
        {
            if (material == null)
            {
                return;
            }

            var resolvedTexture = texture != null ? texture : Texture2D.whiteTexture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", resolvedTexture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", resolvedTexture);
            }
        }

        private static Shader FindFirstAvailableShader(params string[] shaderNames)
        {
            if (shaderNames == null)
            {
                return null;
            }

            for (var index = 0; index < shaderNames.Length; index++)
            {
                var shaderName = shaderNames[index];
                if (string.IsNullOrWhiteSpace(shaderName))
                {
                    continue;
                }

                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private void SetOverlayAlpha(float alpha)
        {
            EnsureOverlay();
            var clamped = Mathf.Clamp01(alpha);
            overlayGroup.alpha = clamped;

            if (overlayPanelImage != null)
            {
                overlayPanelImage.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Lerp(0.04f, 0.24f, clamped));
            }

            if (overlayVignetteImage != null)
            {
                overlayVignetteImage.color = new Color(0.05f, 0.03f, 0.02f, Mathf.Lerp(0.1f, 0.44f, clamped));
            }

            if (overlayGlowImage != null)
            {
                overlayGlowImage.color = new Color(0.99f, 0.73f, 0.36f, Mathf.Lerp(0.02f, 0.2f, clamped));
            }

            if (overlaySweepImage != null)
            {
                var sweepAlpha = Mathf.Lerp(0.01f, 0.14f, clamped);
                overlaySweepImage.color = new Color(0.97f, 0.83f, 0.5f, sweepAlpha);
            }
        }

        private void UpdateOverlayEffects(float normalizedTime, float intensity)
        {
            if (overlaySweepRect != null)
            {
                var x = Mathf.Lerp(-1220f, 1220f, normalizedTime);
                var y = Mathf.Sin(normalizedTime * Mathf.PI * 5f) * 22f;
                overlaySweepRect.anchoredPosition = new Vector2(x, y);
            }

            if (overlaySweepImage != null)
            {
                var pulse = 0.55f + (Mathf.Sin(normalizedTime * Mathf.PI * 9f) * 0.45f);
                overlaySweepImage.color = new Color(0.97f, 0.83f, 0.5f, Mathf.Clamp01(intensity * pulse * 0.2f));
            }

            if (overlayGlowImage != null)
            {
                var glowRect = overlayGlowImage.rectTransform;
                var scale = Mathf.Lerp(0.85f, 1.18f, Mathf.Clamp01(intensity));
                glowRect.sizeDelta = new Vector2(1020f * scale, 640f * scale);
            }
        }

        private void EnsureShrinkVfx()
        {
            if (shrinkVfxRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("LabShrinkVfxRig");
            rootObject.transform.SetParent(transform, false);
            shrinkVfxRoot = rootObject.transform;

            playerShrinkParticles = CreateShrinkParticleSystem("PlayerShrinkParticles", shrinkVfxRoot, playerShrinkVfxColor, 0.22f, 26f);
            capShrinkParticles = CreateShrinkParticleSystem("CapShrinkParticles", shrinkVfxRoot, capShrinkVfxColor, 0.2f, 22f);
            centerShrinkParticles = CreateShrinkParticleSystem("CenterCompressionParticles", shrinkVfxRoot, new Color(0.99f, 0.79f, 0.44f, 0.84f), 0.3f, 34f);
            ApplyParticleTexture(playerShrinkParticles);
            ApplyParticleTexture(capShrinkParticles);
            ApplyParticleTexture(centerShrinkParticles);
        }

        private static ParticleSystem CreateShrinkParticleSystem(string objectName, Transform parent, Color color, float radius, float baseEmission)
        {
            var particleObject = new GameObject(objectName, typeof(ParticleSystem));
            particleObject.transform.SetParent(parent, false);
            var particleSystem = particleObject.GetComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.085f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.gravityModifier = 0f;
            main.maxParticles = 280;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var emission = particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = baseEmission;

            var shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.Lerp(color, Color.white, 0.18f), 0.45f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(Mathf.Clamp01(color.a), 0f),
                    new GradientAlphaKey(Mathf.Clamp01(color.a * 0.72f), 0.58f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLifetime = particleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.05f));

            var noise = particleSystem.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;

            var trails = particleSystem.trails;
            trails.enabled = true;
            trails.ribbonCount = 1;
            trails.ratio = 0.22f;
            trails.lifetime = 0.18f;
            trails.dieWithParticles = true;

            var renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.alignment = ParticleSystemRenderSpace.Facing;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return particleSystem;
        }

        private void PlayShrinkVfx()
        {
            PlayParticle(playerShrinkParticles);
            PlayParticle(capShrinkParticles);
            PlayParticle(centerShrinkParticles);
        }

        private void StopShrinkVfx(bool immediate)
        {
            StopParticle(playerShrinkParticles, immediate);
            StopParticle(capShrinkParticles, immediate);
            StopParticle(centerShrinkParticles, immediate);
        }

        private void UpdateShrinkVfx(float normalizedTime)
        {
            EnsureShrinkVfx();
            var playerPoint = localPlayerMovement != null
                ? localPlayerMovement.transform.position
                : shrinkPlayerAnchor != null
                    ? shrinkPlayerAnchor.position
                    : transform.position;

            if (playerShrinkParticles != null)
            {
                playerShrinkParticles.transform.position = playerPoint + (Vector3.up * 0.09f);
            }

            if (capShrinkParticles != null)
            {
                capShrinkParticles.transform.position = capRoot != null
                    ? capRoot.position + (Vector3.up * 0.09f)
                    : playerPoint + (Vector3.right * 0.18f) + (Vector3.up * 0.08f);
            }

            if (centerShrinkParticles != null)
            {
                centerShrinkParticles.transform.position = ResolveCameraFocusPoint() + (Vector3.up * 0.14f);
            }

            var energy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.07f, 0.9f, normalizedTime));
            SetEmissionAndShape(playerShrinkParticles, Mathf.Lerp(16f, 84f, energy), Mathf.Lerp(0.14f, 0.28f, energy), energy);
            SetEmissionAndShape(capShrinkParticles, Mathf.Lerp(14f, 76f, energy), Mathf.Lerp(0.12f, 0.25f, energy), energy);
            SetEmissionAndShape(centerShrinkParticles, Mathf.Lerp(20f, 102f, energy), Mathf.Lerp(0.18f, 0.38f, energy), energy);
        }

        private static void SetEmissionAndShape(ParticleSystem particleSystem, float emissionRate, float radius, float energy)
        {
            if (particleSystem == null)
            {
                return;
            }

            var emission = particleSystem.emission;
            emission.rateOverTime = emissionRate;

            var shape = particleSystem.shape;
            shape.radius = Mathf.Max(0.05f, radius);

            var main = particleSystem.main;
            main.startSpeedMultiplier = Mathf.Lerp(0.85f, 1.6f, Mathf.Clamp01(energy));
            main.startSizeMultiplier = Mathf.Lerp(0.9f, 1.25f, Mathf.Clamp01(energy));

            var noise = particleSystem.noise;
            noise.strength = Mathf.Lerp(0.11f, 0.32f, Mathf.Clamp01(energy));
            noise.frequency = Mathf.Lerp(0.55f, 1.35f, Mathf.Clamp01(energy));

            var trails = particleSystem.trails;
            trails.lifetime = Mathf.Lerp(0.12f, 0.24f, Mathf.Clamp01(energy));
        }

        private static void PlayParticle(ParticleSystem particleSystem)
        {
            if (particleSystem != null && !particleSystem.isPlaying)
            {
                particleSystem.Play(true);
            }
        }

        private static void StopParticle(ParticleSystem particleSystem, bool immediate)
        {
            if (particleSystem == null)
            {
                return;
            }

            particleSystem.Stop(true, immediate ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        private static bool TryResolveLocalPlayerMovement(out CoreMovement movement)
        {
            movement = null;
            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                movement = taggedPlayer.GetComponent<CoreMovement>();
                if (movement != null)
                {
                    return true;
                }
            }

            var managers = FindObjectsByType<CorePlayerManager>(FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                var candidate = managers[index];
                if (candidate == null || !candidate.IsOwner)
                {
                    continue;
                }

                movement = candidate.CoreMovement;
                return movement != null;
            }

            return false;
        }
    }
}
