using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class InteractionDirector : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private InteractionInputSource inputSource;

        [Header("Focus")]
        [SerializeField] private float maxFocusDistance = 4f;
        [SerializeField] private LayerMask focusMask = Physics.DefaultRaycastLayers;
        [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Runtime State")]
        [SerializeField] private bool lockInteractions;

        private readonly List<InteractionOption> visibleOptions = new List<InteractionOption>(8);
        private InteractableItem currentFocus;
        private InteractionPromptPresenter promptPresenter;
        private ItemInspectionPresenter inspectionPresenter;
        private Coroutine openInspectionRoutine;
        private Transform cachedPlayerTransform;
        private float nextPlayerResolveTime;

        private Dictionary<InteractableItem, float> itemCooldowns = new Dictionary<InteractableItem, float>();
        private float cooldownDuration = 2f;
        private int highlightedOptionIndex = 0;

        public event Action<InteractableItem> FocusChanged;
        public event Action<InteractionInvocation> OptionInvoked;
        public event Action<bool> InspectionStateChanged;

        public bool IsInspectionOpen => inspectionPresenter != null && inspectionPresenter.IsOpen;
        public InteractableItem CurrentFocus => currentFocus;

        public void SetInteractionsLocked(bool value)
        {
            lockInteractions = value;
            if (value)
            {
                ClearFocus();
            }
        }

        public bool TryOpenInspection(InteractableItem item, InteractionOption option)
        {
            if (item == null)
            {
                return false;
            }

            if (openInspectionRoutine != null)
            {
                StopCoroutine(openInspectionRoutine);
            }

            openInspectionRoutine = StartCoroutine(OpenInspectionRoutine(item, item.ResolveInspectionPresentation(option)));
            return true;
        }

        public void CloseInspection()
        {
            if (inspectionPresenter == null || !inspectionPresenter.IsOpen)
            {
                return;
            }

            inspectionPresenter.Close();
            InspectionStateChanged?.Invoke(false);
        }

        private void Awake()
        {
            if (gameplayCamera == null)
            {
                gameplayCamera = Camera.main;
            }

            if (inputSource == null)
            {
                inputSource = GetComponent<InteractionInputSource>();
                if (inputSource == null)
                {
                    inputSource = gameObject.AddComponent<DefaultInteractionInputSource>();
                }
            }

            EnsurePresenters();
        }

        private void EnsurePresenters()
        {
            if (promptPresenter == null)
            {
                promptPresenter = GetComponentInChildren<InteractionPromptPresenter>(true);
                if (promptPresenter == null)
                {
                    var promptGo = new GameObject("InteractionPromptPresenter", typeof(RectTransform));
                    promptGo.transform.SetParent(transform, false);
                    promptPresenter = promptGo.AddComponent<InteractionPromptPresenter>();
                }
            }

            if (inspectionPresenter == null)
            {
                inspectionPresenter = GetComponentInChildren<ItemInspectionPresenter>(true);
                if (inspectionPresenter == null)
                {
                    var inspectionGo = new GameObject("ItemInspectionPresenter", typeof(RectTransform));
                    inspectionGo.transform.SetParent(transform, false);
                    inspectionPresenter = inspectionGo.AddComponent<ItemInspectionPresenter>();
                }
            }
        }

        private void Update()
        {
            EnsurePresenters();

            if (lockInteractions)
            {
                promptPresenter.Hide();
                return;
            }

            if (IsInspectionOpen)
            {
                HandleInspectionState();
                return;
            }

            UpdateFocus();
            HandlePromptInput();
        }

        private void HandleInspectionState()
        {
            if (inputSource != null)
            {
                inspectionPresenter.Tick(inputSource);

                if (inputSource.GetCloseRequested())
                {
                    CloseInspection();
                }
            }
        }

        private void UpdateFocus()
        {
            if (gameplayCamera == null)
            {
                return;
            }

            var playerTransform = ResolvePlayerTransform();
            if (TryGetDirectFocusCandidate(playerTransform, out var directCandidate))
            {
                SetFocus(directCandidate);
                currentFocus.CollectVisibleOptions(visibleOptions);

                if (visibleOptions.Count == 0)
                {
                    if (currentFocus.TryGetOption(InteractionOptionSlot.Top, out var top) && top != null)
                        visibleOptions.Add(top);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Left, out var left) && left != null)
                        visibleOptions.Add(left);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Right, out var right) && right != null)
                        visibleOptions.Add(right);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Bottom, out var bottom) && bottom != null)
                        visibleOptions.Add(bottom);
                }

                promptPresenter.Render(currentFocus, visibleOptions, gameplayCamera, inputSource);
                return;
            }

            var referencePos = playerTransform != null ? playerTransform.position : gameplayCamera.transform.position;

            InteractableItem bestCandidate = null;
            float bestScore = float.MaxValue;

            var interactables = InteractableItem.ActiveInteractables;
            for (var index = 0; index < interactables.Count; index++)
            {
                var candidate = interactables[index];
                if (candidate == null || !candidate.isInteractable || !candidate.HasAnyVisibleOptions())
                {
                    continue;
                }

                if (itemCooldowns.TryGetValue(candidate, out var nextTime) && Time.time < nextTime)
                {
                    continue;
                }

                var focusPosition = candidate.GetPromptWorldPosition();
                var distanceToPlayer = Vector3.Distance(referencePos, focusPosition);
                if (distanceToPlayer > candidate.EffectiveMaxDistance(maxFocusDistance))
                {
                    continue;
                }

                var toItem = focusPosition - gameplayCamera.transform.position;
                var distanceToCamera = toItem.magnitude;
                if (distanceToCamera <= 0.001f)
                {
                    continue;
                }

                var directionToItem = toItem / distanceToCamera;
                var dot = Vector3.Dot(gameplayCamera.transform.forward, directionToItem);
                if (dot < 0.55f)
                {
                    continue;
                }

                var viewportPoint = gameplayCamera.WorldToViewportPoint(focusPosition);
                if (viewportPoint.z <= 0f ||
                    viewportPoint.x < -0.1f || viewportPoint.x > 1.1f ||
                    viewportPoint.y < -0.1f || viewportPoint.y > 1.1f)
                {
                    continue;
                }

                if (Physics.Raycast(
                        gameplayCamera.transform.position,
                        directionToItem,
                        out var hit,
                        distanceToCamera + 0.05f,
                        focusMask,
                        queryTriggerInteraction))
                {
                    var hitItem = hit.collider != null ? hit.collider.GetComponentInParent<InteractableItem>() : null;
                    if (hitItem != candidate &&
                        !IsPlayerCollider(hit.collider, playerTransform) &&
                        !AreRelatedInteractables(hitItem, candidate))
                    {
                        continue;
                    }
                }

                var screenDist = Vector2.Distance(new Vector2(viewportPoint.x, viewportPoint.y), new Vector2(0.5f, 0.5f));
                var score = (screenDist * 8f) + (distanceToPlayer * 0.5f) + (distanceToCamera * 0.15f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate != null)
            {
                SetFocus(bestCandidate);
                currentFocus.CollectVisibleOptions(visibleOptions);

                if (visibleOptions.Count == 0)
                {
                    if (currentFocus.TryGetOption(InteractionOptionSlot.Top, out var top) && top != null)
                        visibleOptions.Add(top);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Left, out var left) && left != null)
                        visibleOptions.Add(left);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Right, out var right) && right != null)
                        visibleOptions.Add(right);
                    else if (currentFocus.TryGetOption(InteractionOptionSlot.Bottom, out var bottom) && bottom != null)
                        visibleOptions.Add(bottom);
                }

                promptPresenter.Render(currentFocus, visibleOptions, gameplayCamera, inputSource);
                return;
            }

            ClearFocus();
        }

        private bool TryGetDirectFocusCandidate(Transform playerTransform, out InteractableItem candidate)
        {
            candidate = null;
            if (gameplayCamera == null)
            {
                return false;
            }

            var ray = gameplayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(
                    ray,
                    out var hit,
                    maxFocusDistance + 0.5f,
                    focusMask,
                    queryTriggerInteraction))
            {
                return false;
            }

            if (IsPlayerCollider(hit.collider, playerTransform))
            {
                return false;
            }

            var hitItem = hit.collider != null ? hit.collider.GetComponentInParent<InteractableItem>() : null;
            if (!IsValidFocusCandidate(hitItem, playerTransform))
            {
                return false;
            }

            candidate = hitItem;
            return true;
        }

        private bool IsValidFocusCandidate(InteractableItem candidate, Transform playerTransform)
        {
            if (candidate == null || !candidate.isInteractable || !candidate.HasAnyVisibleOptions())
            {
                return false;
            }

            if (itemCooldowns.TryGetValue(candidate, out var nextTime) && Time.time < nextTime)
            {
                return false;
            }

            var referencePos = playerTransform != null ? playerTransform.position : gameplayCamera.transform.position;
            var focusPosition = candidate.GetPromptWorldPosition();
            return Vector3.Distance(referencePos, focusPosition) <= candidate.EffectiveMaxDistance(maxFocusDistance);
        }

        private Transform ResolvePlayerTransform()
        {
            if (cachedPlayerTransform != null && cachedPlayerTransform.gameObject.activeInHierarchy)
            {
                return cachedPlayerTransform;
            }

            if (Time.time < nextPlayerResolveTime)
            {
                return cachedPlayerTransform;
            }

            nextPlayerResolveTime = Time.time + 0.5f;

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            cachedPlayerTransform = playerObj != null ? playerObj.transform : null;
            return cachedPlayerTransform;
        }

        private static bool IsPlayerCollider(Collider collider, Transform playerTransform)
        {
            if (collider == null || playerTransform == null)
            {
                return false;
            }

            var colliderTransform = collider.transform;
            return colliderTransform == playerTransform || colliderTransform.IsChildOf(playerTransform);
        }

        private static bool AreRelatedInteractables(InteractableItem first, InteractableItem second)
        {
            if (first == null || second == null || first == second)
            {
                return false;
            }

            var firstTransform = first.transform;
            var secondTransform = second.transform;
            return firstTransform.IsChildOf(secondTransform) || secondTransform.IsChildOf(firstTransform);
        }

        private void HandlePromptInput()
        {
            if (currentFocus == null || inputSource == null || visibleOptions.Count == 0)
            {
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (scroll < 0) highlightedOptionIndex++;
                else highlightedOptionIndex--;
                
                if (highlightedOptionIndex >= visibleOptions.Count) highlightedOptionIndex = 0;
                if (highlightedOptionIndex < 0) highlightedOptionIndex = visibleOptions.Count - 1;
            }

            promptPresenter.SetHighlightedSlot(visibleOptions[highlightedOptionIndex].slot);

            InteractionOption selectedOption = null;

            if (Input.GetMouseButtonDown(0))
            {
                selectedOption = promptPresenter.GetOptionAtScreenPosition(Input.mousePosition, visibleOptions);
                if (selectedOption == null) 
                {
                    selectedOption = visibleOptions[highlightedOptionIndex];
                }
            }
            else if (inputSource.TryGetTriggeredSlot(out var slot))
            {
                currentFocus.TryGetOption(slot, out selectedOption);
            }

            if (selectedOption != null && selectedOption.enabled)
            {
                itemCooldowns[currentFocus] = Time.time + cooldownDuration;

                var invocation = new InteractionInvocation(this, currentFocus, selectedOption);
                currentFocus.InvokeOption(this, selectedOption);
                OptionInvoked?.Invoke(invocation);

                if (selectedOption.opensInspection)
                {
                    TryOpenInspection(currentFocus, selectedOption);
                }

                ClearFocus();
            }
        }

        private IEnumerator OpenInspectionRoutine(InteractableItem item, InspectionPresentation presentation)
        {
            promptPresenter.Hide();
            if (currentFocus != null)
            {
                currentFocus.SetFocused(false);
            }

            InspectionStateChanged?.Invoke(true);
            yield return inspectionPresenter.Open(item, presentation);
            openInspectionRoutine = null;
        }

        private void SetFocus(InteractableItem item)
        {
            if (currentFocus == item)
            {
                return;
            }

            if (currentFocus != null)
            {
                currentFocus.SetFocused(false);
            }

            currentFocus = item;
            highlightedOptionIndex = 0;
            if (currentFocus != null)
            {
                currentFocus.SetFocused(true);
            }

            FocusChanged?.Invoke(currentFocus);
        }

        private void ClearFocus()
        {
            if (currentFocus != null)
            {
                currentFocus.SetFocused(false);
                currentFocus = null;
                FocusChanged?.Invoke(null);
            }

            visibleOptions.Clear();
            if (promptPresenter != null)
            {
                promptPresenter.Hide();
            }
        }

        private void Reset()
        {
            gameplayCamera = Camera.main;
            inputSource = GetComponent<InteractionInputSource>();
        }
    }
}
