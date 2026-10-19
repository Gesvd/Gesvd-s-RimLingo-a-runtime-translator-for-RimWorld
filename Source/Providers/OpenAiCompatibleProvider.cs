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
    public sealed class OpenAiCompatibleProvider : ITranslationProvider
    {
        private readonly RuntimeTranslatorSettings settings;

        public OpenAiCompatibleProvider(RuntimeTranslatorSettings settings)
        {
            this.settings = settings;
        }

        public string DisplayName
        {
            get { return "OpenAI-compatible"; }
        }

        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
        {
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, request.SourceLanguage, request.TargetLanguage);
            List<ProtectedText> protectedTexts = request.Entries.Select(entry => TextProtection.Protect(entry.SourceText, terms)).ToList();
            OpenAiChatRequest chatRequest = new OpenAiChatRequest
            {
                model = settings.OpenAiModel,
                temperature = 0.2f,
                messages = new List<OpenAiMessage>
                {
                    new OpenAiMessage
                    {
                        role = "system",
                        content = "You translate RimWorld mod text. Preserve placeholders like __RT_TOKEN_0__ and all paragraph breaks. Return only a JSON array of objects with id and text fields, one per input item. Keep each id unchanged; put the translation in text. Escape line breaks inside JSON strings. Do not add explanations. Rules: " + (settings.OpenAiRules ?? string.Empty)
                    },
                    new OpenAiMessage
                    {
                        role = "user",
                        content = BuildPrompt(request, protectedTexts)
                    }
                }
            };

            string url = CombineUrl(settings.OpenAiBaseUrl, "chat/completions");
            using (HttpClient client = new HttpClient())
            using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, url))
            {
                if (!string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
                {
                    message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.OpenAiApiKey);
                }

                message.Content = new StringContent(JsonHttp.Serialize(chatRequest), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("Provider returned " + (int)response.StatusCode + ": " + body);
                    }

                    return ReadResponse(request, protectedTexts, body);
                }
            }
        }

        internal static TranslationBatchResult ReadResponse(TranslationBatchRequest request, List<ProtectedText> protectedTexts, string body)
        {
            OpenAiChoice choice = JsonHttp.Deserialize<OpenAiChatResponse>(body)?.choices?.FirstOrDefault();
            if (string.Equals(choice?.finish_reason, "content_filter", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(choice?.message?.refusal))
                throw new InvalidOperationException("Provider refused or filtered the translation. Original text was kept.");
            return ParseResult(request, protectedTexts, choice?.message?.content);
        }

        private static string BuildPrompt(TranslationBatchRequest request, List<ProtectedText> protectedTexts)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Source language: " + request.SourceLanguage);
            builder.AppendLine("Target language: " + request.TargetLanguage);
            builder.AppendLine("Translate these entries. Keep RimWorld terminology concise and natural.");
            List<TranslationItem> items = new List<TranslationItem>();
            for (int i = 0; i < request.Entries.Count; i++)
            {
                items.Add(new TranslationItem { id = request.Entries[i].Id, text = protectedTexts[i].Text });
            }
            builder.AppendLine(JsonHttp.Serialize(items));
            return builder.ToString();
        }

        private static TranslationBatchResult ParseResult(TranslationBatchRequest request, List<ProtectedText> protectedTexts, string content)
        {
            TranslationBatchResult result = new TranslationBatchResult();
            Dictionary<string, ProtectedText> protectionById = new Dictionary<string, ProtectedText>();
            for (int i = 0; i < request.Entries.Count; i++)
            {
                protectionById[request.Entries[i].Id] = protectedTexts[i];
            }

            string json = (content ?? string.Empty).Trim();
            if (json.StartsWith("```") && json.EndsWith("```"))
            {
                int firstLine = json.IndexOf('\n');
                if (firstLine < 0) throw new InvalidOperationException("Provider returned an invalid JSON code block.");
                json = json.Substring(firstLine + 1, json.Length - firstLine - 4).Trim();
            }
            List<TranslationItem> items = JsonHttp.Deserialize<List<TranslationItem>>(json);
            if (items == null || items.Count != request.Entries.Count)
            {
                throw new InvalidOperationException("Provider JSON must contain exactly one translation per input item.");
            }
            foreach (TranslationItem item in items)
            {
                if (item == null || item.id == null || !protectionById.TryGetValue(item.id, out ProtectedText protectedText) ||
                    result.Translations.ContainsKey(item.id) || !TextProtection.HasVisibleText(item.text))
                {
                    throw new InvalidOperationException("Provider JSON contains an unknown/duplicate ID or an empty translation.");
                }
                result.Translations.Add(item.id, protectedText.Restore(item.text));
            }

            return result;
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + path.TrimStart('/');
        }

        [DataContract]
        private sealed class TranslationItem
        {
            [DataMember(IsRequired = true)] public string id;
            [DataMember(IsRequired = true)] public string text;
        }

        [DataContract]
        private sealed class OpenAiChatRequest
        {
            [DataMember] public string model;
            [DataMember] public float temperature;
            [DataMember] public List<OpenAiMessage> messages;
        }

        [DataContract]
        private sealed class OpenAiMessage
        {
            [DataMember] public string role;
            [DataMember] public string content;
            [DataMember(EmitDefaultValue = false)] public string refusal { get; set; }
        }

        [DataContract]
        private sealed class OpenAiChatResponse
        {
            [DataMember] public List<OpenAiChoice> choices { get; set; }
        }

        [DataContract]
        private sealed class OpenAiChoice
        {
            [DataMember] public OpenAiMessage message { get; set; }
            [DataMember] public string finish_reason { get; set; }
        }
    }
}
