using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator
{
    public static class GlossaryTranslator
    {
        public static List<GlossaryTerm> MissingTerms(RuntimeTranslatorSettings settings)
        {
            return Glossary.ForLanguages(settings, settings.SourceLanguage, settings.TargetLanguage)
                .Where(term => string.IsNullOrWhiteSpace(term.Translation) && TextProtection.IsGlossarySource(term.Source))
                .Select(term => term.Clone()).ToList();
        }

        public static RuntimeTranslatorSettings ProviderSettings(RuntimeTranslatorSettings settings)
        {
            RuntimeTranslatorSettings providerSettings = settings.Snapshot();
            // Glossary words must be translated, not replaced by the glossary being built.
            providerSettings.GlossaryTerms.Clear();
            const string rules = " Translate standalone glossary terms for the RimWorld game. Use in-game meanings rather than chess meanings. Return one concise preferred term per item, with no explanations or alternatives.";
            providerSettings.OpenAiRules += rules;
            providerSettings.LaraTranslateRules += rules;
            providerSettings.DeepSeekRules += rules;
            providerSettings.ClaudeRules += rules;
            return providerSettings;
        }

        public static async Task<TranslationStatus> TranslateAsync(RuntimeTranslatorSettings settings, ITranslationProvider provider,
            Func<int> batchSize, Action<List<GlossaryTerm>> acceptBatch, CancellationToken token)
        {
            List<GlossaryTerm> pending = MissingTerms(settings);
            TranslationStatus status = new TranslationStatus { State = TranslationState.Running, Total = pending.Count };
            List<TranslationEntry> allEntries = pending.Select((term, index) => new TranslationEntry
            {
                Id = "glossary|" + index, SourceText = term.Source, DefName = term.Source,
                SourceLanguage = settings.SourceLanguage, TargetLanguage = settings.TargetLanguage
            }).ToList();
            try
            {
                for (int offset = 0; offset < pending.Count;)
                {
                    token.ThrowIfCancellationRequested();
                    List<TranslationEntry> entries = TranslationBatches.Take(allEntries, offset, TranslationBatches.Size(provider, batchSize()));
                    int count = entries.Count;
                    List<GlossaryTerm> batch = pending.GetRange(offset, count);
                    offset += count;
                    try
                    {
                        TranslationBatchResult result = await TranslationBatches.TranslateAsync(provider, new TranslationBatchRequest
                        {
                            Entries = entries, SourceLanguage = settings.SourceLanguage, TargetLanguage = settings.TargetLanguage
                        }, token, partial =>
                        {
                            List<GlossaryTerm> translated = new List<GlossaryTerm>();
                            for (int i = 0; i < batch.Count; i++)
                            {
                                if (!partial.Translations.TryGetValue(entries[i].Id, out string value) || !TextProtection.HasVisibleText(value)) continue;
                                batch[i].Translation = value.Trim();
                                translated.Add(batch[i]);
                                status.Completed++;
                            }
                            if (translated.Count > 0) acceptBatch(translated);
                        }).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        for (int i = 0; i < batch.Count; i++)
                        {
                            if (!result.Translations.TryGetValue(entries[i].Id, out string value) || !TextProtection.HasVisibleText(value))
                            {
                                status.Failed++;
                                if (result.Errors.TryGetValue(entries[i].Id, out string error)) status.LastError = error;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        status.Failed += count;
                        status.LastError = ex.Message;
                    }
                }
                token.ThrowIfCancellationRequested();
                status.State = TranslationState.Completed;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { status.State = TranslationState.Cancelled; }
            catch (Exception ex) { status.State = TranslationState.Failed; status.LastError = ex.Message; }
            return status;
        }

        public static int ApplyBatch(RuntimeTranslatorSettings settings, IEnumerable<GlossaryTerm> translated)
        {
            int applied = 0;
            foreach (GlossaryTerm result in translated)
            {
                GlossaryTerm current = Glossary.ForLanguages(settings, result.SourceLanguage, result.TargetLanguage)
                    .FirstOrDefault(term => string.Equals(term.Source, result.Source, StringComparison.OrdinalIgnoreCase));
                if (current == null || !string.IsNullOrWhiteSpace(current.Translation)) continue;
                current.Translation = result.Translation;
                applied++;
            }
            return applied;
        }
    }
}
