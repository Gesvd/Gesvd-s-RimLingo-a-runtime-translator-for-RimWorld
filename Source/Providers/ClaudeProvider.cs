using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public sealed class ClaudeProvider : ITranslationProvider
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly RuntimeTranslatorSettings settings;
        public ClaudeProvider(RuntimeTranslatorSettings settings) { this.settings = settings; }
        public string DisplayName { get { return "Claude"; } }

        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
        {
            List<ProtectedText> texts = request.Entries.Select(entry => TextProtection.Protect(entry.SourceText,
                Glossary.ForLanguages(settings, request.SourceLanguage, request.TargetLanguage))).ToList();
            using (HttpRequestMessage message = BuildRequest(request, texts))
            using (HttpResponseMessage response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                TranslationBatches.CheckResponse(response, body, DisplayName);
                return ReadResponse(request, texts, body);
            }
        }

        internal HttpRequestMessage BuildRequest(TranslationBatchRequest request, List<ProtectedText> texts)
        {
            if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey)) throw new InvalidOperationException("Enter a Claude API key on the Provider tab.");
            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, settings.ClaudeBaseUrl.TrimEnd('/') + "/messages");
            message.Headers.Add("x-api-key", settings.ClaudeApiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            message.Content = new StringContent(JsonHttp.Serialize(new ClaudeRequest
            {
                model = settings.ClaudeModel, max_tokens = 8192,
                system = OpenAiCompatibleProvider.SystemPrompt(settings.ClaudeRules),
                messages = new List<ClaudeMessage> { new ClaudeMessage { role = "user", content = OpenAiCompatibleProvider.BuildPrompt(request, texts) } }
            }), Encoding.UTF8, "application/json");
            return message;
        }

        internal static TranslationBatchResult ReadResponse(TranslationBatchRequest request, List<ProtectedText> texts, string body)
        {
            ClaudeResponse response = JsonHttp.Deserialize<ClaudeResponse>(body);
            if (response?.stop_reason == "max_tokens")
                throw new TranslationRequestException("Claude output limit reached. The batch will be split when possible.") { TooLarge = true };
            if (response?.stop_reason != "end_turn" || response.content == null || response.content.Any(block => block == null || block.type == "refusal"))
                throw new InvalidOperationException("Claude did not finish a complete translation (refusal, output limit or unexpected response). Original translations were kept; reduce the batch size if the output limit was reached.");
            return OpenAiCompatibleProvider.ParseResult(request, texts, string.Concat(response.content.Where(block => block.type == "text").Select(block => block.text)));
        }

        [DataContract] private sealed class ClaudeRequest
        {
            [DataMember] public string model;
            [DataMember] public int max_tokens;
            [DataMember] public string system;
            [DataMember] public List<ClaudeMessage> messages;
        }
        [DataContract] private sealed class ClaudeMessage
        {
            [DataMember] public string role;
            [DataMember] public string content;
        }
        [DataContract] private sealed class ClaudeResponse
        {
            [DataMember] public string stop_reason { get; set; }
            [DataMember] public List<ClaudeContent> content { get; set; }
        }
        [DataContract] private sealed class ClaudeContent
        {
            [DataMember] public string type { get; set; }
            [DataMember] public string text { get; set; }
        }
    }
}
