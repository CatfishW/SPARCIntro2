using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Blocks.Gameplay.Core;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomNpcAmbientChatterLoop : MonoBehaviour
    {
        [SerializeField] private StoryNpcRegistry npcRegistry;
        [SerializeField] private ClassroomLlmService llmService;
        [SerializeField] private ClassroomNpcChatBubblePresenter bubblePresenter;
        [SerializeField] private ClassroomNpcAmbientController ambientController;
        [SerializeField, Min(4f)] private float minCadenceSeconds = 10f;
        [SerializeField, Min(5f)] private float maxCadenceSeconds = 18f;
        [SerializeField, Min(0.3f)] private float lineGapSeconds = 1.1f;
        [SerializeField, Min(0.6f)] private float pairTalkHoldSeconds = 2.2f;
        [SerializeField] private bool enabledByDefault = true;

        private readonly Dictionary<string, string> pairMemory = new Dictionary<string, string>(StringComparer.Ordinal);
        private Coroutine loopRoutine;

        private void OnEnable()
        {
            ResolveReferences();
            if (!enabledByDefault)
            {
                return;
            }

            loopRoutine = StartCoroutine(LoopRoutine());
        }

        private void OnDisable()
        {
            if (loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
        }

        public void SetEnabled(bool value)
        {
            enabledByDefault = value;
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (value && loopRoutine == null)
            {
                loopRoutine = StartCoroutine(LoopRoutine());
            }
            else if (!value && loopRoutine != null)
            {
                StopCoroutine(loopRoutine);
                loopRoutine = null;
            }
        }

        private IEnumerator LoopRoutine()
        {
            yield return new WaitForSecondsRealtime(2.2f);
            while (enabledByDefault)
            {
                ResolveReferences();

                if (npcRegistry == null || bubblePresenter == null)
                {
                    yield return new WaitForSecondsRealtime(2f);
                    continue;
                }

                var cast = npcRegistry.Npcs;
                if (cast == null || cast.Count < 2)
                {
                    yield return new WaitForSecondsRealtime(2f);
                    continue;
                }

                var firstIndex = UnityEngine.Random.Range(0, cast.Count);
                var secondIndex = UnityEngine.Random.Range(0, cast.Count - 1);
                if (secondIndex >= firstIndex)
                {
                    secondIndex++;
                }

                var first = cast[firstIndex];
                var second = cast[secondIndex];
                if (first == null || second == null)
                {
                    yield return new WaitForSecondsRealtime(1.2f);
                    continue;
                }

                var pairKey = BuildPairKey(first.NpcId, second.NpcId);
                var context = pairMemory.TryGetValue(pairKey, out var cachedContext)
                    ? cachedContext
                    : string.Empty;

                var exchangeTask = GenerateAmbientExchangeAsync(first, second, context);
                while (!exchangeTask.IsCompleted)
                {
                    yield return null;
                }

                if (exchangeTask.IsFaulted)
                {
                    Debug.LogWarning("[ClassroomNpcAmbientChatterLoop] Failed to generate ambient chatter.", this);
                    yield return new WaitForSecondsRealtime(3f);
                    continue;
                }

                var exchange = exchangeTask.Result;
                if (!string.IsNullOrWhiteSpace(exchange.FirstLine))
                {
                    ApplyPairConversationPose(first, second, true);
                    bubblePresenter.ShowBubble(first, exchange.FirstLine, 4.8f, first.NpcDisplayName);
                }

                yield return new WaitForSecondsRealtime(lineGapSeconds);

                if (!string.IsNullOrWhiteSpace(exchange.SecondLine))
                {
                    bubblePresenter.ShowBubble(second, exchange.SecondLine, 4.8f, second.NpcDisplayName);
                }

                yield return new WaitForSecondsRealtime(pairTalkHoldSeconds);
                ApplyPairConversationPose(first, second, false);

                var memory = new StringBuilder(220);
                memory.Append(first.NpcDisplayName).Append(": ").Append(exchange.FirstLine).Append('\n');
                memory.Append(second.NpcDisplayName).Append(": ").Append(exchange.SecondLine);
                pairMemory[pairKey] = memory.ToString();

                var wait = UnityEngine.Random.Range(minCadenceSeconds, maxCadenceSeconds);
                yield return new WaitForSecondsRealtime(wait);
            }

            loopRoutine = null;
        }

        private async Task<AmbientExchange> GenerateAmbientExchangeAsync(StoryNpcAgent first, StoryNpcAgent second, string recentContext)
        {
            if (llmService == null)
            {
                return BuildFallbackExchange(first, second);
            }

            var prompt = new StringBuilder(384);
            prompt.Append("Create a short back-and-forth between two students in a biology classroom.\n");
            prompt.Append("Return exactly two lines in this format:\n");
            prompt.Append(first.NpcDisplayName).Append(": <line>\n");
            prompt.Append(second.NpcDisplayName).Append(": <line>\n");
            prompt.Append("Each line <= 12 words. Keep it natural, concise, and mission-related.\n");
            prompt.Append("No narration, no stage directions.");
            if (!string.IsNullOrWhiteSpace(recentContext))
            {
                prompt.Append("\nRecent context:\n").Append(recentContext);
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var response = await llmService.StreamChatAsync(
                new[]
                {
                    new LlmChatMessage("system", "You write concise classroom NPC chatter. Keep lines brief."),
                    new LlmChatMessage("user", prompt.ToString())
                },
                onDelta: null,
                maxTokensOverride: 88,
                temperatureOverride: 0.6f,
                cancellation.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
            {
                return BuildFallbackExchange(first, second);
            }

            return ParseExchange(response, first, second);
        }

        private static AmbientExchange ParseExchange(string raw, StoryNpcAgent first, StoryNpcAgent second)
        {
            var firstLine = string.Empty;
            var secondLine = string.Empty;
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith(first.NpcDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    firstLine = ExtractLineBody(line);
                    continue;
                }

                if (line.StartsWith(second.NpcDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    secondLine = ExtractLineBody(line);
                }
            }

            if (string.IsNullOrWhiteSpace(firstLine) || string.IsNullOrWhiteSpace(secondLine))
            {
                return BuildFallbackExchange(first, second);
            }

            return new AmbientExchange(firstLine, secondLine);
        }

        private static string ExtractLineBody(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            var colon = line.IndexOf(':');
            if (colon < 0 || colon >= line.Length - 1)
            {
                return line.Trim();
            }

            return line.Substring(colon + 1).Trim();
        }

        private static AmbientExchange BuildFallbackExchange(StoryNpcAgent first, StoryNpcAgent second)
        {
            return new AmbientExchange(
                "Mouth entry is easy. Throat alignment is the hard part.",
                "Then we clear the airway and aim for the intestine zone.");
        }

        private static string BuildPairKey(string a, string b)
        {
            if (string.CompareOrdinal(a, b) <= 0)
            {
                return $"{a}|{b}";
            }

            return $"{b}|{a}";
        }

        private void ResolveReferences()
        {
            npcRegistry = npcRegistry != null ? npcRegistry : FindFirstObjectByType<StoryNpcRegistry>();
            llmService = llmService != null ? llmService : FindFirstObjectByType<ClassroomLlmService>();
            bubblePresenter = bubblePresenter != null ? bubblePresenter : FindFirstObjectByType<ClassroomNpcChatBubblePresenter>();
            ambientController = ambientController != null ? ambientController : FindFirstObjectByType<ClassroomNpcAmbientController>();
        }

        private void ApplyPairConversationPose(StoryNpcAgent first, StoryNpcAgent second, bool talking)
        {
            if (first == null || second == null || ambientController == null)
            {
                return;
            }

            ambientController.TrySetNpcCanMove(first.NpcId, !talking);
            ambientController.TrySetNpcCanMove(second.NpcId, !talking);

            if (!talking)
            {
                return;
            }

            if (!ambientController.TryGetNpcTransform(first.NpcId, out var firstTransform) ||
                !ambientController.TryGetNpcTransform(second.NpcId, out var secondTransform) ||
                firstTransform == null ||
                secondTransform == null)
            {
                return;
            }

            FaceTowards(firstTransform, secondTransform.position);
            FaceTowards(secondTransform, firstTransform.position);
            ambientController.TrySnapNpcToGround(first.NpcId);
            ambientController.TrySnapNpcToGround(second.NpcId);
        }

        private static void FaceTowards(Transform source, Vector3 targetPosition)
        {
            if (source == null)
            {
                return;
            }

            var direction = targetPosition - source.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            source.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private readonly struct AmbientExchange
        {
            public AmbientExchange(string firstLine, string secondLine)
            {
                FirstLine = firstLine ?? string.Empty;
                SecondLine = secondLine ?? string.Empty;
            }

            public string FirstLine { get; }
            public string SecondLine { get; }
        }
    }
}
