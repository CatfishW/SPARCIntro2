using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class ClassroomLlmService : MonoBehaviour
    {
        [SerializeField] private string endpointBaseUrl = "https://game.agaii.org/mllm/v1";
        [SerializeField, Min(5)] private int requestTimeoutSeconds = 30;
        [SerializeField, Min(0)] private int retryCount = 1;
        [SerializeField, Min(64)] private int defaultMaxTokens = 192;
        [SerializeField, Range(0f, 2f)] private float defaultTemperature = 0.7f;
        [SerializeField] private bool disableThinking = true;
        [SerializeField, Min(1)] private int maxParsedActionsPerReply = 2;

        private static readonly HttpClient SharedClient = new HttpClient();
        private static readonly Regex SayLineRegex = new Regex(@"^\s*SAY\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex ActionsLineRegex = new Regex(@"^\s*ACTIONS\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private string cachedModelId = string.Empty;

        public string EndpointBaseUrl => endpointBaseUrl;

        public async Task<string> ResolveModelIdAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(cachedModelId))
            {
                return cachedModelId;
            }

            var modelsUrl = BuildUrl("models");
            using var timeout = CreateTimeoutTokenSource(cancellationToken);
            using var response = await SharedClient.GetAsync(modelsUrl, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var modelList = JsonUtility.FromJson<ModelListPayload>(payload);
            if (modelList?.data == null || modelList.data.Length == 0)
            {
                throw new InvalidOperationException("No models returned from classroom LLM endpoint.");
            }

            for (var index = 0; index < modelList.data.Length; index++)
            {
                var candidate = modelList.data[index];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
                {
                    continue;
                }

                if (!IsLikelyChatModel(candidate.id))
                {
                    continue;
                }

                cachedModelId = candidate.id;
                break;
            }

            for (var index = 0; index < modelList.data.Length; index++)
            {
                var candidate = modelList.data[index];
                if (!string.IsNullOrWhiteSpace(cachedModelId))
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(candidate?.id))
                {
                    cachedModelId = candidate.id;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(cachedModelId))
            {
                throw new InvalidOperationException("LLM model list did not contain a usable model id.");
            }

            return cachedModelId;
        }

        public async Task<string> StreamChatAsync(
            IReadOnlyList<LlmChatMessage> messages,
            Action<string> onDelta,
            int? maxTokensOverride,
            float? temperatureOverride,
            CancellationToken cancellationToken)
        {
            if (messages == null || messages.Count == 0)
            {
                return string.Empty;
            }

            var attempt = 0;
            Exception lastException = null;
            while (attempt <= Mathf.Max(0, retryCount))
            {
                attempt++;
                try
                {
                    var modelId = await ResolveModelIdAsync(cancellationToken).ConfigureAwait(false);
                    var dto = BuildChatRequest(modelId, messages, stream: true, maxTokensOverride, temperatureOverride);
                    var requestJson = JsonUtility.ToJson(dto);

                    using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("chat/completions"))
                    {
                        Content = content
                    };

                    using var timeout = CreateTimeoutTokenSource(cancellationToken);
                    using var response = await SharedClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException($"Classroom LLM request failed ({response.StatusCode}): {errorBody}");
                    }

                    var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (!mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
                    {
                        var fallbackJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var completion = JsonUtility.FromJson<ChatCompletionPayload>(fallbackJson);
                        return completion?.choices != null && completion.choices.Length > 0
                            ? completion.choices[0].message?.content ?? string.Empty
                            : string.Empty;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(stream);

                    var builder = new StringBuilder(256);
                    while (!reader.EndOfStream)
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var chunkPayload = line.Substring(5).Trim();
                        if (string.Equals(chunkPayload, "[DONE]", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        var chunk = JsonUtility.FromJson<ChatChunkPayload>(chunkPayload);
                        var delta = chunk?.choices != null && chunk.choices.Length > 0
                            ? chunk.choices[0].delta?.content
                            : string.Empty;

                        if (string.IsNullOrEmpty(delta))
                        {
                            continue;
                        }

                        builder.Append(delta);
                        onDelta?.Invoke(delta);
                    }

                    return builder.ToString().Trim();
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    lastException = exception;
                    if (attempt > retryCount + 1)
                    {
                        break;
                    }

                    await Task.Delay(220, cancellationToken).ConfigureAwait(false);
                }
            }

            throw lastException ?? new InvalidOperationException("Classroom LLM request failed unexpectedly.");
        }

        public StructuredNpcReply ParseStructuredNpcReply(string rawResponse)
        {
            var normalized = (rawResponse ?? string.Empty).Trim();
            var sayMatch = SayLineRegex.Match(normalized);
            var actionsMatch = ActionsLineRegex.Match(normalized);

            var say = sayMatch.Success
                ? sayMatch.Groups[1].Value.Trim()
                : normalized;

            if (string.IsNullOrWhiteSpace(say))
            {
                say = "I am listening.";
            }

            var actions = new List<string>();
            if (actionsMatch.Success)
            {
                var actionLine = actionsMatch.Groups[1].Value;
                var tokens = actionLine.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index < tokens.Length; index++)
                {
                    var actionToken = tokens[index].Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(actionToken) || string.Equals(actionToken, "none", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    actions.Add(actionToken);
                    if (actions.Count >= Mathf.Max(1, maxParsedActionsPerReply))
                    {
                        break;
                    }
                }
            }

            return new StructuredNpcReply(say, actions);
        }

        private string BuildUrl(string relativePath)
        {
            var root = string.IsNullOrWhiteSpace(endpointBaseUrl)
                ? "https://game.agaii.org/mllm/v1"
                : endpointBaseUrl.Trim();
            if (!root.EndsWith("/", StringComparison.Ordinal))
            {
                root += "/";
            }

            return root + relativePath.TrimStart('/');
        }

        private ChatRequestPayload BuildChatRequest(
            string modelId,
            IReadOnlyList<LlmChatMessage> sourceMessages,
            bool stream,
            int? maxTokensOverride,
            float? temperatureOverride)
        {
            var payload = new ChatRequestPayload
            {
                model = modelId,
                stream = stream,
                max_tokens = maxTokensOverride ?? defaultMaxTokens,
                temperature = temperatureOverride ?? defaultTemperature,
                chat_template_kwargs = disableThinking
                    ? new ChatTemplateKwargsPayload { enable_thinking = false }
                    : null
            };

            var messages = new ChatMessagePayload[sourceMessages.Count];
            for (var index = 0; index < sourceMessages.Count; index++)
            {
                messages[index] = new ChatMessagePayload
                {
                    role = sourceMessages[index].role,
                    content = sourceMessages[index].content
                };
            }

            payload.messages = messages;
            return payload;
        }

        private CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken)
        {
            var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Mathf.Max(5f, requestTimeoutSeconds)));
            return timeoutSource;
        }

        private static bool IsLikelyChatModel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            var token = modelId.Trim().ToLowerInvariant();
            if (token.Contains("imagine", StringComparison.Ordinal) ||
                token.Contains("video", StringComparison.Ordinal) ||
                token.Contains("tts", StringComparison.Ordinal) ||
                token.Contains("audio", StringComparison.Ordinal) ||
                token.Contains("embedding", StringComparison.Ordinal) ||
                token.Contains("rerank", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        [Serializable]
        private sealed class ModelListPayload
        {
            public ModelPayload[] data;
        }

        [Serializable]
        private sealed class ModelPayload
        {
            public string id;
        }

        [Serializable]
        private sealed class ChatRequestPayload
        {
            public string model;
            public bool stream;
            public int max_tokens;
            public float temperature;
            public ChatTemplateKwargsPayload chat_template_kwargs;
            public ChatMessagePayload[] messages;
        }

        [Serializable]
        private sealed class ChatTemplateKwargsPayload
        {
            public bool enable_thinking;
        }

        [Serializable]
        private sealed class ChatCompletionPayload
        {
            public ChatChoicePayload[] choices;
        }

        [Serializable]
        private sealed class ChatChunkPayload
        {
            public ChatChoicePayload[] choices;
        }

        [Serializable]
        private sealed class ChatChoicePayload
        {
            public ChatMessagePayload message;
            public ChatDeltaPayload delta;
        }

        [Serializable]
        private sealed class ChatDeltaPayload
        {
            public string content;
        }

        [Serializable]
        private sealed class ChatMessagePayload
        {
            public string role;
            public string content;
        }
    }

    [Serializable]
    public readonly struct LlmChatMessage
    {
        public LlmChatMessage(string role, string content)
        {
            this.role = role ?? "user";
            this.content = content ?? string.Empty;
        }

        public readonly string role;
        public readonly string content;
    }

    public readonly struct StructuredNpcReply
    {
        public StructuredNpcReply(string say, IReadOnlyList<string> actions)
        {
            Say = say ?? string.Empty;
            Actions = actions ?? Array.Empty<string>();
        }

        public string Say { get; }
        public IReadOnlyList<string> Actions { get; }
    }
}
