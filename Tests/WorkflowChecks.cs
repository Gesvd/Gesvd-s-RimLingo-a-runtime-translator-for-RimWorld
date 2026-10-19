using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimeTranslator.Tests
{
    public sealed class BatchProbeProvider : ITranslationProvider
    {
        public string Name = "BatchProbe";
        public string DisplayName { get { return Name; } }
        public readonly List<int> Sizes = new List<int>();
        public readonly List<string> Texts = new List<string>();
        public Action<int> OnRequest;
        public int FailRequest;
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        {
            Sizes.Add(request.Entries.Count);
            Texts.AddRange(request.Entries.Select(entry => entry.SourceText));
            OnRequest?.Invoke(Sizes.Count);
            if (Sizes.Count == FailRequest) throw new InvalidOperationException("Test request failure");
            TranslationBatchResult result = new TranslationBatchResult();
            foreach (TranslationEntry entry in request.Entries) result.Translations[entry.Id] = "译" + entry.SourceText;
            return Task.FromResult(result);
        }
    }

    public static class WorkflowChecks
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        private static void Worker(List<TranslationEntry> entries, ITranslationProvider provider, RuntimeTranslatorSettings snapshot)
        {
            Task worker = (Task)typeof(TranslationManager).GetMethod("RunWorker", Hidden).Invoke(null,
                new object[] { entries, provider, snapshot, CancellationToken.None, false });
            worker.GetAwaiter().GetResult();
        }
        public static int Run(string folder)
        {
            Verse.GenFilePaths.ConfigFolderPath = folder;
            RuntimeTranslatorSettings live = new RuntimeTranslatorSettings { BatchSize = 1, ApplyRuntimeTranslations = false, ExportLanguageFiles = false, SkipCompletedTranslations = false };
            TranslationManager.Initialize(live);
            List<TranslationEntry> entries = Enumerable.Range(0, 5).Select(index => new TranslationEntry
            {
                Id = "workflow-" + index, SourceHash = "source-hash", SourceText = "word" + index,
                SourceLanguage = "English", TargetLanguage = "ChineseSimplified"
            }).ToList();
            BatchProbeProvider probe = new BatchProbeProvider { OnRequest = number => { if (number == 1) live.BatchSize = 3; } };
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.SequenceEqual(new[] { 1, 3, 1 }), "running translation reads the new batch size for the next request");
            live.SkipCompletedTranslations = true;
            probe = new BatchProbeProvider();
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.Count == 0 && TranslationManager.Status.Total == 0 && TranslationManager.Status.Skipped == 5,
                "skip-completed excludes finished entries from API requests and task totals");
            probe = new BatchProbeProvider { Name = "AnotherProvider" };
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.Count == 0 && TranslationManager.Status.Skipped == 5, "completed translations can be reused after changing provider");
            entries[0].SourceHash = "new-source-or-glossary-hash";
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.Sum() == 1 && TranslationManager.Status.Skipped == 4, "changed source or glossary still refreshes the affected entry");
            live.SkipCompletedTranslations = false;
            probe = new BatchProbeProvider();
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.Sum() == 5 && TranslationManager.Status.Skipped == 0, "turning skip-completed off explicitly retranslates all selected entries");
            TranslationCacheStore cache = (TranslationCacheStore)typeof(TranslationManager).GetField("cache", Hidden).GetValue(null);
            TranslationEntry cleared = new TranslationEntry { Id = entries[0].Id, SourceHash = entries[0].SourceHash, TranslatedText = "" };
            cache.Put(cleared, "BatchProbe");
            live.SkipCompletedTranslations = true;
            probe = new BatchProbeProvider();
            Worker(entries, probe, live.Snapshot());
            Check(probe.Sizes.Sum() == 1, "clearing the preferred translation does not resurrect an older provider result");

            RuntimeTranslatorSettings glossary = new RuntimeTranslatorSettings();
            glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "rifle" });
            glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "medicine" });
            glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "component" });
            glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "steel", Translation = "钢铁" });
            glossary.GlossaryTerms.Add(new GlossaryTerm { Source = "foreign", TargetLanguage = "Japanese" });
            RuntimeTranslatorSettings snapshot = glossary.Snapshot();
            RuntimeTranslatorSettings providerSettings = GlossaryTranslator.ProviderSettings(snapshot);
            Check(providerSettings.GlossaryTerms.Count == 0 && snapshot.GlossaryTerms.Count > 0 && providerSettings.OpenAiRules.Contains("RimWorld"),
                "glossary bootstrap disables self-substitution and supplies game context without mutating terms");
            int size = 1;
            int applied = 0;
            probe = new BatchProbeProvider { OnRequest = number =>
            {
                if (number == 1)
                {
                    size = 2;
                    glossary.GlossaryTerms.First(term => term.Source == "rifle").Translation = "手动步枪";
                }
            } };
            TranslationStatus result = GlossaryTranslator.TranslateAsync(snapshot, probe, () => size,
                batch => applied += GlossaryTranslator.ApplyBatch(glossary, batch), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Total == 3 && result.Completed == 3 && result.Failed == 0 && probe.Sizes.SequenceEqual(new[] { 1, 2 }),
                "standalone glossary job translates every missing term in live-sized batches");
            Check(!probe.Texts.Contains("pawn") && !probe.Texts.Contains("steel") && !probe.Texts.Contains("foreign"),
                "glossary translation preserves populated terms and ignores other language pairs");
            Check(applied == 2 && glossary.GlossaryTerms.First(term => term.Source == "rifle").Translation == "手动步枪",
                "background glossary results do not overwrite a player's newer manual edit");
            Check(GlossaryTranslator.MissingTerms(glossary).Count == 0 && glossary.GlossaryTerms.First(term => term.Source == "pawn").Translation == "小人",
                "glossary is ready for consistent translation while existing defaults stay intact");
            glossary.GlossaryTerms.First(term => term.Source == "medicine").Translation = "";
            glossary.GlossaryTerms.First(term => term.Source == "component").Translation = "";
            probe = new BatchProbeProvider { FailRequest = 2 };
            result = GlossaryTranslator.TranslateAsync(glossary.Snapshot(), probe, () => 1,
                batch => GlossaryTranslator.ApplyBatch(glossary, batch), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Completed == 1 && result.Failed == 1 && result.LastError == "Test request failure" && GlossaryTranslator.MissingTerms(glossary).Count == 1,
                "glossary batch failures retain earlier translations and leave failed terms retryable");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                probe = new BatchProbeProvider();
                result = GlossaryTranslator.TranslateAsync(glossary.Snapshot(), probe, () => 1,
                    batch => GlossaryTranslator.ApplyBatch(glossary, batch), cancellation.Token).GetAwaiter().GetResult();
                Check(result.State == TranslationState.Cancelled && probe.Sizes.Count == 0, "glossary translation can be cancelled without losing completed terms");
            }
            GlossaryTerm removed = glossary.GlossaryTerms.First(term => string.IsNullOrWhiteSpace(term.Translation) && term.TargetLanguage == "ChineseSimplified");
            glossary.GlossaryTerms.Remove(removed);
            Check(GlossaryTranslator.ApplyBatch(glossary, new[] { new GlossaryTerm { Source = removed.Source, Translation = "late result" } }) == 0,
                "late glossary results do not recreate a removed term");
            live.BatchSize = 2;
            live.SkipCompletedTranslations = false;
            TranslationManager.Initialize(live);
            List<TranslationEntry> large = Enumerable.Range(0, 122).Select(index => new TranslationEntry
            {
                Id = "large-batch-" + index, SourceHash = "hash", SourceText = "word" + index,
                SourceLanguage = "English", TargetLanguage = "ChineseSimplified"
            }).ToList();
            probe = new BatchProbeProvider { OnRequest = number => { if (number == 1) live.BatchSize = 75; } };
            Worker(large, probe, live.Snapshot());
            Check(probe.Sizes.SequenceEqual(new[] { 2, 75, 45 }), "live batch size can exceed 50 on the next text request");
            live.BatchSize = 50; probe = new BatchProbeProvider();
            Worker(large, probe, live.Snapshot());
            Check(probe.Sizes.SequenceEqual(new[] { 50, 50, 22 }), "slider maximum 50 is honored by the text worker");
            live.BatchSize = int.MaxValue; probe = new BatchProbeProvider();
            Worker(large, probe, live.Snapshot());
            Check(probe.Sizes.SequenceEqual(new[] { 122 }), "very large manual batch is limited to the remaining entries without overflow");
            foreach (int invalidSize in new[] { 0, -5 })
            {
                live.BatchSize = invalidSize; probe = new BatchProbeProvider();
                Worker(large.Take(2).ToList(), probe, live.Snapshot());
                Check(probe.Sizes.SequenceEqual(new[] { 1, 1 }), "nonpositive text batch falls back to one: " + invalidSize);
            }
            RuntimeTranslatorSettings largeGlossary = new RuntimeTranslatorSettings
            {
                GlossaryTerms = Enumerable.Range(0, 122).Select(index => new GlossaryTerm { Source = "large term " + index }).ToList()
            };
            size = 2;
            probe = new BatchProbeProvider { OnRequest = number => { if (number == 1) size = 75; } };
            result = GlossaryTranslator.TranslateAsync(largeGlossary, probe, () => size, batch => { }, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Completed == 122 && probe.Sizes.SequenceEqual(new[] { 2, 75, 45 }), "glossary requests also accept live values above 50");
            probe = new BatchProbeProvider();
            result = GlossaryTranslator.TranslateAsync(largeGlossary, probe, () => int.MaxValue, batch => { }, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Completed == 122 && probe.Sizes.SequenceEqual(new[] { 122 }), "very large glossary batch is limited to remaining terms without overflow");
            largeGlossary.GlossaryTerms = largeGlossary.GlossaryTerms.Take(2).ToList();
            probe = new BatchProbeProvider();
            result = GlossaryTranslator.TranslateAsync(largeGlossary, probe, () => 0, batch => { }, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Completed == 2 && probe.Sizes.SequenceEqual(new[] { 1, 1 }), "nonpositive glossary batch falls back to one");
            return passed;
        }
    }
}