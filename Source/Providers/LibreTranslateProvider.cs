using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public sealed class LibreTranslateProvider : ITranslationProvider
    {
        private readonly RuntimeTranslatorSettings settings;

        public LibreTranslateProvider(RuntimeTranslatorSettings settings)
        {
            this.settings = settings;
        }

        public string DisplayName
        {
            get { return settings.UseBundledLibreTranslate ? "LibreTranslate (bundled en-zh 1.9)" : "LibreTranslate"; }
        }

        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
        {
            if (settings.UseBundledLibreTranslate && (NormalizeLanguage(request.SourceLanguage) != "en" || NormalizeLanguage(request.TargetLanguage) != "zh"))
                throw new InvalidOperationException("Bundled LibreTranslate supports only English to ChineseSimplified.");
            TranslationBatchResult result = new TranslationBatchResult();
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, request.SourceLanguage, request.TargetLanguage);
            string baseUrl = settings.UseBundledLibreTranslate
                ? await BundledLibreTranslate.GetEndpointAsync(cancellationToken).ConfigureAwait(false) : settings.LibreTranslateUrl;
            using (HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = !settings.UseBundledLibreTranslate }))
            {
                foreach (TranslationEntry entry in request.Entries)
                {
                    ProtectedText protectedText = TextProtection.Protect(entry.SourceText, terms);
                    LibreTranslateRequest libreRequest = new LibreTranslateRequest
                    {
                        q = protectedText.Text,
                        source = NormalizeLanguage(request.SourceLanguage),
                        target = NormalizeLanguage(request.TargetLanguage),
                        format = "text",
                        api_key = settings.UseBundledLibreTranslate ? string.Empty : settings.LibreTranslateApiKey ?? string.Empty
                    };

                    using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(baseUrl, settings.UseBundledLibreTranslate ? "runtime-translator/translate" : "translate")))
                    {
                        message.Content = new StringContent(JsonHttp.Serialize(libreRequest), System.Text.Encoding.UTF8, "application/json");
                        using (HttpResponseMessage response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false))
                        {
                            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new InvalidOperationException("LibreTranslate returned " + (int)response.StatusCode + ": " + body);
                            }

                            LibreTranslateResponse libreResponse = JsonHttp.Deserialize<LibreTranslateResponse>(body);
                            result.Translations[entry.Id] = protectedText.Restore(libreResponse.translatedText);
                        }
                    }
                }
            }

            return result;
        }

        private static string NormalizeLanguage(string language)
        {
            string normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "english")
            {
                return "en";
            }
            if (normalized == "chinesesimplified" || normalized == "chinese" || normalized == "zh")
            {
                return "zh";
            }
            if (normalized == "japanese")
            {
                return "ja";
            }
            if (normalized == "korean")
            {
                return "ko";
            }
            return normalized;
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + path.TrimStart('/');
        }

        [DataContract]
        private sealed class LibreTranslateRequest
        {
            [DataMember] public string q;
            [DataMember] public string source;
            [DataMember] public string target;
            [DataMember] public string format;
            [DataMember] public string api_key;
        }

        [DataContract]
        private sealed class LibreTranslateResponse
        {
            [DataMember] public string translatedText { get; set; }
        }
    }
}
