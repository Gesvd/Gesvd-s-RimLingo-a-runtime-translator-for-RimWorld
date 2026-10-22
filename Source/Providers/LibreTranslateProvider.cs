using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public sealed class LibreTranslateProvider : ITranslationProvider
    {
        private static readonly HttpClient onlineClient = new HttpClient();
        private static readonly HttpClient offlineClient = new HttpClient(new HttpClientHandler { UseProxy = false });
        private readonly RuntimeTranslatorSettings settings;
        internal bool IsBundled { get { return settings.UseBundledLibreTranslate; } }

        public LibreTranslateProvider(RuntimeTranslatorSettings settings)
        {
            this.settings = settings;
        }

        public string DisplayName
        {
            get { return settings.UseBundledLibreTranslate ? "LibreTranslate (bundled)" : "LibreTranslate"; }
        }

        public async Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
        {
            if (settings.UseBundledLibreTranslate && !BundledLibreTranslate.CanTranslate(request.SourceLanguage, request.TargetLanguage))
                throw new InvalidOperationException("No installed offline model route for this language pair. Enable the required RimLingo language expansion mods and restart RimWorld; Auto is available only with online providers.");
            TranslationBatchResult result = new TranslationBatchResult();
            List<GlossaryTerm> terms = Glossary.ForLanguages(settings, request.SourceLanguage, request.TargetLanguage);
            string baseUrl = settings.UseBundledLibreTranslate
                ? await BundledLibreTranslate.GetEndpointAsync(cancellationToken).ConfigureAwait(false) : settings.LibreTranslateUrl;
            if (settings.UseBundledLibreTranslate && request.Entries.Count > 1)
                return await TranslateBundledBatch(request, terms, baseUrl, cancellationToken).ConfigureAwait(false);
            HttpClient client = settings.UseBundledLibreTranslate ? offlineClient : onlineClient;
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
                            TranslationBatches.CheckResponse(response, body, DisplayName);

                            LibreTranslateResponse libreResponse = JsonHttp.Deserialize<LibreTranslateResponse>(body);
                            result.Translations[entry.Id] = protectedText.Restore(libreResponse.translatedText);
                        }
                    }
                }
            }

            return result;
        }

        private static async Task<TranslationBatchResult> TranslateBundledBatch(TranslationBatchRequest request, List<GlossaryTerm> terms,
            string baseUrl, CancellationToken token)
        {
            List<ProtectedText> texts = request.Entries.Select(entry => TextProtection.Protect(entry.SourceText, terms)).ToList();
            var body = new LibreBatchRequest { q = texts.Select(text => text.Text).ToArray(),
                source = NormalizeLanguage(request.SourceLanguage), target = NormalizeLanguage(request.TargetLanguage) };
            using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(baseUrl, "runtime-translator/translate")))
            {
                message.Content = new StringContent(JsonHttp.Serialize(body), System.Text.Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await offlineClient.SendAsync(message, token).ConfigureAwait(false))
                {
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    TranslationBatches.CheckResponse(response, json, "Bundled LibreTranslate");
                    LibreBatchResponse translated = JsonHttp.Deserialize<LibreBatchResponse>(json);
                    if (translated?.translatedTexts == null || translated.errors == null || translated.translatedTexts.Length != texts.Count || translated.errors.Length != texts.Count)
                        throw new InvalidOperationException("Offline batch response did not match the input count.");
                    TranslationBatchResult result = new TranslationBatchResult();
                    for (int i = 0; i < texts.Count; i++)
                    {
                        string id = request.Entries[i].Id;
                        try
                        {
                            if (!string.IsNullOrEmpty(translated.errors[i])) throw new InvalidOperationException(translated.errors[i]);
                            string value = texts[i].Restore(translated.translatedTexts[i]);
                            if (!TextProtection.HasVisibleText(value)) throw new InvalidOperationException("Offline engine returned empty text.");
                            result.Translations[id] = value;
                        }
                        catch (InvalidOperationException ex) { result.Errors[id] = ex.Message; }
                    }
                    return result;
                }
            }
        }

        private static string NormalizeLanguage(string language)
        {
            return LanguageCatalog.Code(language);
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + path.TrimStart('/');
        }

        [DataContract]
        private sealed class LibreBatchRequest
        {
            [DataMember] public string[] q;
            [DataMember] public string source;
            [DataMember] public string target;
        }

        [DataContract]
        private sealed class LibreBatchResponse
        {
            [DataMember] public string[] translatedTexts { get; set; }
            [DataMember] public string[] errors { get; set; }
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
