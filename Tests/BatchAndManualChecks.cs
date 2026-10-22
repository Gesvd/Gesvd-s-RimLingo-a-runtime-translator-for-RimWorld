using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Verse;

namespace RuntimeTranslator.Tests
{
    internal sealed class RecoveringProvider : ITranslationProvider
    {
        public string DisplayName { get { return "Recovering"; } }
        public Func<TranslationBatchRequest, int, TranslationBatchResult> Handle;
        public int Calls;
        public Task<TranslationBatchResult> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken token)
        { return Task.FromResult(Handle(request, ++Calls)); }
    }

    public static class BatchAndManualChecks
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static int passed;
        private static void Check(bool valid, string name)
        {
            if (!valid) throw new Exception("FAILED: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static TranslationBatchResult Success(TranslationBatchRequest request)
        {
            TranslationBatchResult result = new TranslationBatchResult();
            foreach (var entry in request.Entries) result.Translations[entry.Id] = "translated " + entry.SourceText;
            return result;
        }
        private static void Worker(List<TranslationEntry> entries, ITranslationProvider provider, RuntimeTranslatorSettings settings, CancellationToken token = default(CancellationToken))
        {
            ((Task)typeof(TranslationManager).GetMethod("RunWorker", Hidden).Invoke(null,
                new object[] { entries, provider, settings.Snapshot(), token, false })).GetAwaiter().GetResult();
        }
        public static int Run(string folder)
        {
            GenFilePaths.ConfigFolderPath = folder;
            RuntimeTranslatorSettings settings = new RuntimeTranslatorSettings { ApplyRuntimeTranslations = false, ExportLanguageFiles = false, SkipCompletedTranslations = false };
            TranslationManager.Initialize(settings);
            TranslationCacheStore cache = (TranslationCacheStore)typeof(TranslationManager).GetField("cache", Hidden).GetValue(null);
            List<TranslationEntry> entries = Enumerable.Range(0, 3).Select(i => new TranslationEntry
            { Id = "entry-" + i, SourceText = "source " + i, SourceHash = "hash", SourceLanguage = "English", TargetLanguage = "ChineseSimplified" }).ToList();
            var longEntries = entries.Select(entry => new TranslationEntry { Id = entry.Id, SourceText = new string('x', 7000) }).ToList();
            Check(TranslationBatches.Take(longEntries, 0, 50).Count == 1 && TranslationBatches.Take(entries, 0, 50).Count == 3,
                "batching respects text length as well as the player's entry limit");
            longEntries[0].SourceText = new string('x', 20000);
            Check(TranslationBatches.Take(longEntries, 0, 50)[0].SourceText.Length == 20000, "an oversized single entry is kept intact instead of silently truncated");

            TranslationBatchRequest request = new TranslationBatchRequest { Entries = entries, SourceLanguage = "English", TargetLanguage = "ChineseSimplified" };
            RecoveringProvider provider = new RecoveringProvider { Handle = (batch, call) =>
            {
                if (call < 3) throw new TranslationRequestException("temporary") { Retryable = true };
                return Success(batch);
            } };
            var translated = TranslationBatches.TranslateAsync(provider, request, CancellationToken.None).GetAwaiter().GetResult();
            Check(provider.Calls == 3 && translated.Translations.Count == 3, "temporary provider failures get two bounded retries");
            provider = new RecoveringProvider { Handle = (batch, call) => { throw new TranslationRequestException("bad key"); } };
            translated = TranslationBatches.TranslateAsync(provider, request, CancellationToken.None).GetAwaiter().GetResult();
            Check(provider.Calls == 1 && translated.Errors.Count == 3, "permanent authentication/configuration failures are not retried");
            bool outputLimit = false;
            try { OpenAiCompatibleProvider.ReadResponse(request, entries.Select(item => TextProtection.Protect(item.SourceText)).ToList(),
                "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"[]\"}}]}"); }
            catch (TranslationRequestException ex) { outputLimit = ex.TooLarge; }
            Check(outputLimit, "OpenAI-compatible output truncation is recognized before accepting a partial JSON response");
            provider = new RecoveringProvider { Handle = (batch, call) =>
            {
                if (batch.Entries.Count > 1) throw new TranslationRequestException("output limit") { TooLarge = true };
                if (batch.Entries[0].Id == "entry-1") throw new InvalidOperationException("bad entry");
                return Success(batch);
            } };
            int accepted = 0;
            translated = TranslationBatches.TranslateAsync(provider, request, CancellationToken.None, part => accepted += part.Translations.Count).GetAwaiter().GetResult();
            Check(translated.Translations.Count == 2 && translated.Errors.Count == 1 && accepted == 2,
                "oversized batches split and retain successful siblings without duplicating acceptance");
            using (CancellationTokenSource stopping = new CancellationTokenSource())
            {
                provider = new RecoveringProvider { Handle = (batch, call) =>
                {
                    if (batch.Entries.Count > 1) throw new TranslationRequestException("output limit") { TooLarge = true };
                    if (call == 3) { stopping.Cancel(); stopping.Token.ThrowIfCancellationRequested(); }
                    return Success(batch);
                } };
                Worker(entries.Take(2).ToList(), provider, settings, stopping.Token);
                Check(TranslationManager.Status.State == TranslationState.Cancelled && TranslationManager.Status.Completed == 1 && cache.Records.Count == 1,
                    "cancelling a split batch keeps its already completed and saved part");
            }
            using (CancellationTokenSource stopping = new CancellationTokenSource())
            {
                provider = new RecoveringProvider { Handle = (batch, call) => { stopping.Cancel(); throw new HttpRequestException("network interrupted"); } };
                bool cancelled = false;
                try { TranslationBatches.TranslateAsync(provider, request, stopping.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && provider.Calls == 1, "cancellation interrupts retry backoff without sending another request");
            }
            provider = new RecoveringProvider { Handle = (batch, call) =>
            {
                TranslationBatchResult result = new TranslationBatchResult(); result.Translations[batch.Entries[0].Id] = "successful";
                result.Errors[batch.Entries[1].Id] = "failed only this entry"; return result;
            } };
            Worker(entries.Take(2).ToList(), provider, settings);
            Check(TranslationManager.Status.Completed == 1 && TranslationManager.Status.Failed == 1 && TranslationManager.FailedEntryCount == 1,
                "partial results record exactly the failed entries for retry");

            TranslationEntry original = entries[2];
            cache.Put(original, "Machine");
            cache.TryUpdate(original.Id, original.SourceHash, "Machine", "player correction", out TranslationCacheRecord manual);
            provider = new RecoveringProvider { Handle = (batch, call) => Success(batch) };
            Worker(new List<TranslationEntry> { original }, provider, settings);
            Check(provider.Calls == 0 && TranslationManager.Status.Skipped == 1 && manual.IsManual,
                "manual corrections stay locked even when completed-cache skipping is disabled");
            TranslationEntry changedGlossary = manual.ToEntry(); changedGlossary.SourceHash = "new glossary hash";
            Check(!cache.Put(changedGlossary, "Another provider", true), "glossary refresh and provider changes cannot replace a locked correction");
            TranslationManager.SaveCache();
            TranslationCacheStore reloaded = new TranslationCacheStore(); reloaded.Load();
            Check(reloaded.Records.Single(record => record.Id == original.Id).IsManual, "manual locks survive saving and loading the XML cache");
            TranslationManager.SetManualLock(manual, false);
            provider = new RecoveringProvider { Handle = (batch, call) =>
            {
                TranslationManager.UpdateCachedTranslation(manual, "new edit during request");
                return Success(batch);
            } };
            Worker(new List<TranslationEntry> { original }, provider, settings);
            Check(manual.TranslatedText == "new edit during request" && !cache.Records.Any(record => record.Id == original.Id && record.Provider == "Recovering"),
                "a manual edit made during an API request wins over the late machine result");
            TranslationManager.SetManualLock(manual, false);
            provider = new RecoveringProvider { Handle = (batch, call) => Success(batch) };
            Worker(new List<TranslationEntry> { original }, provider, settings);
            Check(provider.Calls == 1 && cache.Records.Any(record => record.Id == original.Id && record.Provider == "Recovering"), "unlocking permits an explicitly requested retranslation");
            var alternate = cache.Records.Single(record => record.Id == original.Id && record.Provider == "Recovering");
            cache.SetManual(alternate.Id, alternate.SourceHash, alternate.Provider, true);
            cache.SetManual(manual.Id, manual.SourceHash, manual.Provider, true);
            Check(manual.IsManual && !alternate.IsManual, "explicitly protecting another cached variant selects that preferred translation");
            cache.TryUpdate(original.Id, original.SourceHash, "Machine", "locked old source", out _);
            TranslationEntry updatedSource = TranslationCacheRecord.FromEntry(original, "Test").ToEntry();
            updatedSource.SourceText = "updated mod source"; updatedSource.SourceHash = "updated source hash";
            Check(cache.Put(updatedSource, "Updated source"), "an old manual lock does not prevent translating changed mod source text");
            cache.TryUpdate(original.Id, original.SourceHash, "Machine", "", out _);
            Check(!manual.IsManual, "clearing a manual translation restores the previous retryable-empty behavior");
            TranslationEntry exportSource = new TranslationEntry { PackageId = "export.test", DefTypeName = "Def", DefName = "Item", FieldName = "label",
                SourceLanguage = "English", TargetLanguage = "ChineseSimplified", SourceText = "old source", TranslatedText = "old correction" };
            var oldRecord = TranslationCacheRecord.FromEntry(exportSource, "Manual"); oldRecord.IsManual = true; oldRecord.UpdatedUtcTicks = 1;
            exportSource.SourceText = "new source"; exportSource.TranslatedText = "updated source translation";
            var newRecord = TranslationCacheRecord.FromEntry(exportSource, "Machine"); newRecord.UpdatedUtcTicks = 2;
            string exportPath = Path.Combine(folder, "RuntimeTranslator", "GeneratedLanguages", "ChineseSimplified", "export.test", "DefInjected", "Def", "RuntimeTranslator.xml");
            LanguageFileExporter.Export(new[] { oldRecord, newRecord }, "ChineseSimplified");
            Check(XDocument.Load(exportPath).Root.Element("Item.label").Value == "updated source translation",
                "export does not resurrect a locked translation for an older source version");
            oldRecord.SourceText = "new source";
            LanguageFileExporter.Export(new[] { oldRecord, newRecord }, "ChineseSimplified");
            Check(XDocument.Load(exportPath).Root.Element("Item.label").Value == "old correction", "export prefers manual corrections for the same source version");
            return passed;
        }

    }
}
