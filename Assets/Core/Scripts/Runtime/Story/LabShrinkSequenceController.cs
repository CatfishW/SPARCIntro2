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
        [SerializeField] private Color overlayColor = new Color(0.4f, 0.92f, 1f, 1f);

        private Canvas overlayCanvas;
        private CanvasGroup overlayGroup;
        private Image overlayPanelImage;
        private Image overlayGlowImage;
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
            SetOverlayAlpha(alphaIn * alphaOut * pulse);

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
            DeactivateCinematicCamera();

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

            cinematicCamera.CopyFrom(gameplayCamera);
            cinematicCamera.depth = gameplayCamera.depth + 20f;
            cinematicCamera.transform.position = gameplayCameraStartPosition;
            cinematicCamera.transform.rotation = gameplayCameraStartRotation;
            cinematicCamera.enabled = true;
            gameplayCamera.enabled = false;
        }

        private void DeactivateCinematicCamera()
        {
            if (cinematicCamera != null)
            {
                cinematicCamera.enabled = false;
            }

            if (gameplayCamera != null)
            {
                gameplayCamera.transform.position = gameplayCameraStartPosition;
                gameplayCamera.transform.rotation = gameplayCameraStartRotation;
                gameplayCamera.enabled = gameplayCameraWasEnabled;
            }
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

            var forward = basis.forward;
            var right = basis.right;
            var introPosition = focusPoint - (forward * 2.6f) - (right * 1.45f) + (Vector3.up * 1.5f);
            var compressionPosition = focusPoint + (right * 2.2f) + (Vector3.up * 1.35f) - (forward * 0.35f);
            var finalPosition = focusPoint - (forward * 1.25f) + (right * 1.15f) + (Vector3.up * 1.65f);

            var lookPoint = focusPoint + (Vector3.up * 0.7f);
            var introRotation = Quaternion.LookRotation((lookPoint - introPosition).normalized, Vector3.up);
            var compressionRotation = Quaternion.LookRotation((lookPoint - compressionPosition).normalized, Vector3.up);
            var finalRotation = Quaternion.LookRotation((lookPoint - finalPosition).normalized, Vector3.up);

            Vector3 position;
            Quaternion rotation;
            if (normalizedTime < 0.32f)
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.32f, normalizedTime));
                position = Vector3.Lerp(gameplayCameraStartPosition, introPosition, blend);
                rotation = Quaternion.Slerp(gameplayCameraStartRotation, introRotation, blend);
            }
            else if (normalizedTime < 0.72f)
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.72f, normalizedTime));
                position = Vector3.Lerp(introPosition, compressionPosition, blend);
                rotation = Quaternion.Slerp(introRotation, compressionRotation, blend);
            }
            else
            {
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, normalizedTime));
                position = Vector3.Lerp(compressionPosition, finalPosition, blend);
                rotation = Quaternion.Slerp(compressionRotation, finalRotation, blend);
            }

            cinematicCamera.transform.position = position;
            cinematicCamera.transform.rotation = rotation;
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
            overlayPanelImage.color = overlayColor;
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var glowObject = new GameObject("CenterGlow", typeof(RectTransform), typeof(Image));
            glowObject.transform.SetParent(panel.transform, false);
            overlayGlowImage = glowObject.GetComponent<Image>();
            overlayGlowImage.color = new Color(0.55f, 0.96f, 1f, 0.2f);
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
            messageOutline.effectColor = new Color(0.04f, 0.2f, 0.28f, 0.95f);
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
            detailText.color = new Color(0.9f, 0.98f, 1f, 1f);
            var detailRect = detailObject.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0.5f, 0.5f);
            detailRect.anchorMax = new Vector2(0.5f, 0.5f);
            detailRect.pivot = new Vector2(0.5f, 0.5f);
            detailRect.sizeDelta = new Vector2(1180f, 100f);
            detailRect.anchoredPosition = new Vector2(0f, -64f);
        }

        private void SetOverlayAlpha(float alpha)
        {
            EnsureOverlay();
            var clamped = Mathf.Clamp01(alpha);
            overlayGroup.alpha = clamped;

            if (overlayPanelImage != null)
            {
                overlayPanelImage.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, Mathf.Lerp(0.34f, 0.92f, clamped));
            }

            if (overlayGlowImage != null)
            {
                overlayGlowImage.color = new Color(0.55f, 0.96f, 1f, Mathf.Lerp(0.05f, 0.42f, clamped));
            }
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
