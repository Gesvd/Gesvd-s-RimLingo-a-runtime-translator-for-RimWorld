using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public sealed class LaraTranslateProvider : ITranslationProvider
    {
        private readonly RuntimeTranslatorSettings settings;

        public LaraTranslateProvider(RuntimeTranslatorSettings settings)
        {
            this.settings = settings;
        }

        public string DisplayName
        {
            get { return "Lara Translate Bridge"; }
        }

        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
        {
            TranslationBatchResult result = new TranslationBatchResult();
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, request.SourceLanguage, request.TargetLanguage);
            using (HttpClient client = new HttpClient())
            {
                foreach (TranslationEntry entry in request.Entries)
                {
                    ProtectedText protectedText = TextProtection.Protect(entry.SourceText, terms);
                    LaraTranslateRequest laraRequest = new LaraTranslateRequest
                    {
                        text = protectedText.Text,
                        source = NormalizeLanguage(request.SourceLanguage),
                        target = NormalizeLanguage(request.TargetLanguage),
                        content_type = settings.LaraTranslateModel ?? "text",
                        instructions = settings.LaraTranslateRules ?? string.Empty,
                        access_key_id = settings.LaraTranslateApiId ?? string.Empty,
                        access_key_secret = settings.LaraTranslateApiKey ?? string.Empty
                    };

                    using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(settings.LaraTranslateUrl, "translate")))
                    {
                        message.Content = new StringContent(JsonHttp.Serialize(laraRequest), Encoding.UTF8, "application/json");
                        using (HttpResponseMessage response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new InvalidOperationException("Lara Translate bridge returned " + (int)response.StatusCode + ": " + body);
                            }

                            LaraTranslateResponse laraResponse = JsonHttp.Deserialize<LaraTranslateResponse>(body);
                            string translated = PickTranslation(laraResponse);
                            if (string.IsNullOrWhiteSpace(translated))
                            {
                                throw new InvalidOperationException("Lara Translate bridge response did not contain a translated text field.");
                            }
                            result.Translations[entry.Id] = protectedText.Restore(translated);
                        }
                    }
                }
            }

            return result;
        }

        private static string PickTranslation(LaraTranslateResponse response)
        {
            if (response == null)
            {
                return string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(response.translatedText))
            {
                return response.translatedText;
            }
            if (!string.IsNullOrWhiteSpace(response.text))
            {
                return response.text;
            }
            return string.Empty;
        }

        private static string NormalizeLanguage(string language)
        {
            string code = LanguageCatalog.Code(language);
            return code == "zh" ? "zh-CN" : code == "zt" ? "zh-TW" : code;
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + path.TrimStart('/');
        }

        [DataContract]
        private sealed class LaraTranslateRequest
        {
            [DataMember] public string text;
            [DataMember] public string source;
            [DataMember] public string target;
            [DataMember] public string content_type;
            [DataMember] public string instructions;
            [DataMember] public string access_key_id;
            [DataMember] public string access_key_secret;
        }

        [DataContract]
        private sealed class LaraTranslateResponse
        {
            [DataMember] public string translatedText { get; set; }
            [DataMember] public string text { get; set; }
        }
    }
}
