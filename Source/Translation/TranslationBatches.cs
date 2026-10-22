using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    internal sealed class TranslationRequestException : InvalidOperationException
    {
        public bool Retryable;
        public bool TooLarge;
        public TimeSpan RetryAfter;
        public TranslationRequestException(string message) : base(message) { }
    }

    internal static class TranslationBatches
    {
        internal const int CharacterLimit = 12000;

        internal static int Size(ITranslationProvider provider, int maximum)
        {
            return provider is LaraTranslateProvider || (provider is LibreTranslateProvider libre && !libre.IsBundled) ? 1 : maximum;
        }

        internal static List<TranslationEntry> Take(List<TranslationEntry> entries, int offset, int maximum)
        {
            int count = 0;
            long characters = 0;
            while (offset + count < entries.Count && count < Math.Max(1, maximum))
            {
                TranslationEntry entry = entries[offset + count];
                long length = (long)(entry.Id?.Length ?? 0) + TextProtection.Protect(entry.SourceText).Text.Length + 40;
                if (count > 0 && characters + length > CharacterLimit) break;
                characters += length;
                count++;
            }
            return entries.GetRange(offset, count);
        }

        internal static void CheckResponse(HttpResponseMessage response, string body, string provider)
        {
            if (response.IsSuccessStatusCode) return;
            int code = (int)response.StatusCode;
            string detail = body ?? string.Empty;
            bool tooLarge = code == 413 || (code == 400 && (detail.IndexOf("context_length_exceeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                detail.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0));
            var retry = response.Headers.RetryAfter;
            TimeSpan wait = retry?.Delta ?? (retry?.Date.HasValue == true ? retry.Date.Value - DateTimeOffset.UtcNow : TimeSpan.Zero);
            throw new TranslationRequestException(provider + " returned " + code + ": " + (detail.Length > 2000 ? detail.Substring(0, 2000) : detail))
            {
                Retryable = code == 408 || code == 429 || code == 500 || code == 502 || code == 503 || code == 504,
                TooLarge = tooLarge, RetryAfter = wait
            };
        }

        internal static async Task<TranslationBatchResult> TranslateAsync(ITranslationProvider provider, TranslationBatchRequest request, CancellationToken token,
            Action<TranslationBatchResult> accept = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    TranslationBatchResult result = await provider.TranslateBatchAsync(request, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    accept?.Invoke(result);
                    return result;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (TranslationRequestException ex) when (ex.TooLarge && request.Entries.Count > 1)
                {
                    int half = request.Entries.Count / 2;
                    TranslationBatchResult merged = new TranslationBatchResult();
                    foreach (List<TranslationEntry> part in new[] { request.Entries.GetRange(0, half), request.Entries.GetRange(half, request.Entries.Count - half) })
                    {
                        TranslationBatchResult result = await TranslateAsync(provider, new TranslationBatchRequest
                        { Entries = part, SourceLanguage = request.SourceLanguage, TargetLanguage = request.TargetLanguage }, token, accept).ConfigureAwait(false);
                        foreach (var pair in result.Translations) merged.Translations[pair.Key] = pair.Value;
                        foreach (var pair in result.Errors) merged.Errors[pair.Key] = pair.Value;
                    }
                    return merged;
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    bool transient = ex is HttpRequestException || ex is OperationCanceledException || (ex is TranslationRequestException error && error.Retryable);
                    if (transient && attempt < 2)
                    {
                        double milliseconds = ex is TranslationRequestException errorWithDelay ? errorWithDelay.RetryAfter.TotalMilliseconds : 0;
                        milliseconds = Math.Max(500 * (attempt + 1), Math.Min(30000, milliseconds));
                        await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), token).ConfigureAwait(false);
                        continue;
                    }
                    TranslationBatchResult failed = new TranslationBatchResult();
                    foreach (TranslationEntry entry in request.Entries) failed.Errors[entry.Id] = ex.Message;
                    return failed;
                }
            }
        }
    }
}
