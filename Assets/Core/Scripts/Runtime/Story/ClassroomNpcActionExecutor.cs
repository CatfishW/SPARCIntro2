using System;
using System.Collections;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomNpcActionExecutor : MonoBehaviour
    {
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private ClassroomNpcAmbientController ambientController;
        [SerializeField] private CorePlayerManager localPlayerManager;
        [SerializeField] private ClassroomBodyKnowledgeQuizUi quizUi;
        [SerializeField] private ClassroomNpcChatBubblePresenter bubblePresenter;

        [SerializeField, Min(0.1f)] private float danceDurationSeconds = 3.8f;
        [SerializeField, Min(0.1f)] private float lieDownDurationSeconds = 2.7f;
        [SerializeField, Min(0.1f)] private float followDurationSeconds = 8f;

        public IEnumerator ExecuteActionsRoutine(StoryNpcAgent sourceNpc, IReadOnlyList<string> actions)
        {
            ResolveReferences();
            if (sourceNpc == null || actions == null || actions.Count == 0)
            {
                yield break;
            }

            for (var index = 0; index < actions.Count; index++)
            {
                var action = (actions[index] ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(action))
                {
                    continue;
                }

                if (action == "dance")
                {
                    yield return DanceRoutine(sourceNpc, danceDurationSeconds);
                    continue;
                }

                if (action == "jump")
                {
                    yield return JumpRoutine(sourceNpc);
                    continue;
                }

                if (action == "surprised")
                {
                    yield return SurprisedRoutine(sourceNpc);
                    continue;
                }

                if (action == "lie_down")
                {
                    yield return LieDownRoutine(sourceNpc, lieDownDurationSeconds);
                    continue;
                }

                if (action == "follow_player_short")
                {
                    yield return FollowPlayerRoutine(sourceNpc, followDurationSeconds);
                    continue;
                }

                if (action.StartsWith("go_talk_", StringComparison.Ordinal))
                {
                    var targetToken = action.Substring("go_talk_".Length);
                    var targetNpc = ResolveNpcByToken(targetToken);
                    if (targetNpc != null && targetNpc != sourceNpc)
                    {
                        yield return GoTalkRoutine(sourceNpc, targetNpc);
                    }

                    continue;
                }

                if (action == "start_quiz")
                {
                    if (quizUi != null)
                    {
                        yield return quizUi.OpenQuizAndWait(sourceNpc.NpcDisplayName);
                    }

                    continue;
                }
            }
        }

        private IEnumerator DanceRoutine(StoryNpcAgent npc, float durationSeconds)
        {
            if (!TryResolveNpcRoot(npc, out var root))
            {
                yield break;
            }

            SetNpcMotionEnabled(npc, false);
            var startPosition = root.position;
            var startYaw = root.eulerAngles.y;
            var elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                var phase = elapsed * 6.4f;
                var sway = Mathf.Sin(phase) * 0.16f;
                var bounce = Mathf.Sin(phase * 0.5f) * 0.06f;
                var yaw = Mathf.Sin(phase * 0.7f) * 32f;
                var localOffset = new Vector3(sway, 0f, bounce);
                root.position = startPosition + (root.right * localOffset.x) + (root.forward * localOffset.z);
                root.rotation = Quaternion.Euler(0f, startYaw + yaw, 0f);
                SnapNpcToGround(npc);
                yield return null;
            }

            root.position = startPosition;
            root.rotation = Quaternion.Euler(0f, startYaw, 0f);
            SnapNpcToGround(npc);
            SetNpcMotionEnabled(npc, true);
        }

        private IEnumerator JumpRoutine(StoryNpcAgent npc)
        {
            if (!TryResolveNpcRoot(npc, out var root))
            {
                yield break;
            }

            SetNpcMotionEnabled(npc, false);
            var startPosition = root.position;
            const float duration = 0.56f;
            const float jumpHeight = 0.36f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var height = 4f * t * (1f - t) * jumpHeight;
                root.position = startPosition + (Vector3.up * height);
                yield return null;
            }

            root.position = startPosition;
            SnapNpcToGround(npc);
            SetNpcMotionEnabled(npc, true);
        }

        private IEnumerator SurprisedRoutine(StoryNpcAgent npc)
        {
            if (bubblePresenter != null)
            {
                bubblePresenter.ShowBubble(npc, "Whoa!", 1.6f, npc != null ? npc.NpcDisplayName : "NPC");
            }

            yield return JumpRoutine(npc);
        }

        private IEnumerator LieDownRoutine(StoryNpcAgent npc, float durationSeconds)
        {
            if (!TryResolveNpcRoot(npc, out var root))
            {
                yield break;
            }

            SetNpcMotionEnabled(npc, false);
            var startRotation = root.rotation;
            var sideRotation = startRotation * Quaternion.Euler(0f, 0f, 82f);
            var rotateInDuration = 0.28f;
            var elapsed = 0f;

            while (elapsed < rotateInDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / rotateInDuration);
                root.rotation = Quaternion.Slerp(startRotation, sideRotation, t);
                yield return null;
            }

            yield return new WaitForSeconds(Mathf.Max(0.1f, durationSeconds));

            elapsed = 0f;
            while (elapsed < rotateInDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / rotateInDuration);
                root.rotation = Quaternion.Slerp(sideRotation, startRotation, t);
                yield return null;
            }

            root.rotation = startRotation;
            SnapNpcToGround(npc);
            SetNpcMotionEnabled(npc, true);
        }

        private IEnumerator FollowPlayerRoutine(StoryNpcAgent npc, float durationSeconds)
        {
            if (!TryResolveNpcRoot(npc, out var root))
            {
                yield break;
            }

            var player = ResolvePlayerTransform();
            if (player == null)
            {
                yield break;
            }

            SetNpcMotionEnabled(npc, false);
            var elapsed = 0f;
            var speed = 1.55f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                if (player == null)
                {
                    player = ResolvePlayerTransform();
                }

                if (player == null)
                {
                    yield return null;
                    continue;
                }

                var followPoint = player.position - (player.forward * 1.2f);
                var direction = followPoint - root.position;
                direction.y = 0f;
                var distance = direction.magnitude;
                if (distance > 0.1f)
                {
                    var move = direction.normalized * (speed * Time.deltaTime);
                    if (move.magnitude > distance)
                    {
                        move = direction;
                    }

                    root.position += move;
                    if (direction.sqrMagnitude > 0.0001f)
                    {
                        var look = Quaternion.LookRotation(direction.normalized, Vector3.up);
                        root.rotation = Quaternion.Slerp(root.rotation, look, Time.deltaTime * 8f);
                    }
                }

                SnapNpcToGround(npc);
                yield return null;
            }

            SetNpcMotionEnabled(npc, true);
        }

        private IEnumerator GoTalkRoutine(StoryNpcAgent source, StoryNpcAgent target)
        {
            if (!TryResolveNpcRoot(source, out var sourceRoot) || !TryResolveNpcRoot(target, out var targetRoot))
            {
                yield break;
            }

            SetNpcMotionEnabled(source, false);
            var duration = 6f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var toTarget = targetRoot.position - sourceRoot.position;
                toTarget.y = 0f;
                var distance = toTarget.magnitude;
                if (distance <= 1.25f)
                {
                    break;
                }

                var move = toTarget.normalized * (1.75f * Time.deltaTime);
                if (move.magnitude > distance)
                {
                    move = toTarget;
                }

                sourceRoot.position += move;
                sourceRoot.rotation = Quaternion.Slerp(
                    sourceRoot.rotation,
                    Quaternion.LookRotation(toTarget.normalized, Vector3.up),
                    Time.deltaTime * 10f);
                SnapNpcToGround(source);
                yield return null;
            }

            if (bubblePresenter != null)
            {
                bubblePresenter.ShowBubble(source, "Quick huddle?", 2.6f, source.NpcDisplayName);
                yield return new WaitForSeconds(0.8f);
                bubblePresenter.ShowBubble(target, "Give me the short version.", 2.8f, target.NpcDisplayName);
            }

            var faceDuration = 1.2f;
            elapsed = 0f;
            while (elapsed < faceDuration)
            {
                elapsed += Time.deltaTime;
                var toTarget = targetRoot.position - sourceRoot.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    sourceRoot.rotation = Quaternion.Slerp(
                        sourceRoot.rotation,
                        Quaternion.LookRotation(toTarget.normalized, Vector3.up),
                        Time.deltaTime * 8f);
                }

                yield return null;
            }

            SetNpcMotionEnabled(source, true);
        }

        private StoryNpcAgent ResolveNpcByToken(string token)
        {
            ResolveReferences();
            if (string.IsNullOrWhiteSpace(token) || npcRegistry == null)
            {
                return null;
            }

            var normalized = token.Trim().ToLowerInvariant();
            var cast = npcRegistry.Npcs;
            for (var index = 0; index < cast.Count; index++)
            {
                var npc = cast[index];
                if (npc == null)
                {
                    continue;
                }

                if ((npc.NpcId ?? string.Empty).ToLowerInvariant().Contains(normalized))
                {
                    return npc;
                }

                if ((npc.NpcDisplayName ?? string.Empty).ToLowerInvariant().Contains(normalized))
                {
                    return npc;
                }
            }

            return null;
        }

        private Transform ResolvePlayerTransform()
        {
            ResolveReferences();
            if (localPlayerManager != null)
            {
                return localPlayerManager.transform;
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
                    return candidate.transform;
                }
            }

            return players.Length > 0 ? players[0].transform : null;
        }

        private bool TryResolveNpcRoot(StoryNpcAgent npc, out Transform root)
        {
            root = null;
            if (npc == null)
            {
                return false;
            }

            root = npc.transform;
            return root != null;
        }

        private void SetNpcMotionEnabled(StoryNpcAgent npc, bool enabled)
        {
            if (npc == null || ambientController == null)
            {
                return;
            }

            ambientController.TrySetNpcCanMove(npc.NpcId, enabled);
        }

        private void SnapNpcToGround(StoryNpcAgent npc)
        {
            if (npc == null || ambientController == null)
            {
                return;
            }

            ambientController.TrySnapNpcToGround(npc.NpcId);
        }

        private void ResolveReferences()
        {
            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>();
            ambientController = ambientController != null ? ambientController : FindFirstObjectByType<ClassroomNpcAmbientController>();
            quizUi = quizUi != null ? quizUi : FindFirstObjectByType<ClassroomBodyKnowledgeQuizUi>(FindObjectsInactive.Include);
            bubblePresenter = bubblePresenter != null ? bubblePresenter : FindFirstObjectByType<ClassroomNpcChatBubblePresenter>();

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
                }
            }
        }
    }
}
