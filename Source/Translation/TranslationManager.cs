using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace RuntimeTranslator
{
    public static class TranslationManager
    {
        private static RuntimeTranslatorSettings settings;
        private static TranslationCacheStore cache;
        private static CancellationTokenSource cancellation;
        private static ManualResetEventSlim pauseGate = new ManualResetEventSlim(true);
        private static Task worker;
        private static IEnumerator<TranslationEntry> scanSteps;
        private static List<TranslationEntry> capturedEntries;
        private static TaskCompletionSource<List<TranslationEntry>> scanCompletion;
        private static readonly object statusLock = new object();
        private static readonly object pendingLock = new object();
        private static readonly object cacheLock = new object();
        private static readonly object saveLock = new object();
        private static readonly Queue<TranslationEntry> pendingApply = new Queue<TranslationEntry>();
        private static readonly Queue<Action> pendingMainThreadActions = new Queue<Action>();
        private static readonly TranslationStatus status = new TranslationStatus();
        private static readonly Dictionary<string, TranslationEntry> failedEntries = new Dictionary<string, TranslationEntry>();
        private static bool failuresFromGlossary;
        public static int FailedEntryCount { get { lock (statusLock) return failedEntries.Count; } }

        public static void RetryFailed() { Start(failuresFromGlossary, true); }

        public static TranslationStatus Status
        {
            get
            {
                lock (statusLock)
                {
                    return new TranslationStatus
                    {
                        State = status.State,
                        Total = status.Total,
                        Completed = status.Completed,
                        Failed = status.Failed,
                        Skipped = status.Skipped,
                        CurrentItem = status.CurrentItem,
                        LastError = status.LastError
                    };
                }
            }
        }

        public static void Initialize(RuntimeTranslatorSettings modSettings)
        {
            settings = modSettings;
            NativeTranslations.Reset();
            RuntimeTextApplier.ResetIndex();
            cache = new TranslationCacheStore();
            cache.Load();
        }

        public static void Start(bool applyGlossary = false, bool retryFailed = false)
        {
            if (settings == null || TranslationLifetime.IsStopping)
            {
                return;
            }

            if (worker != null && !worker.IsCompleted)
            {
                Messages.Message(UiText.Get("Gesvd’s RimLingo is already running."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!LanguageCatalog.IsSupported(settings.SourceLanguage, true) || !LanguageCatalog.IsSupported(settings.TargetLanguage, false))
            {
                Messages.Message(UiText.Get("Choose source and target languages from the dropdown menus."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!applyGlossary && !retryFailed && (settings.SelectedPackageIds == null || settings.SelectedPackageIds.Count == 0))
            {
                Messages.Message(UiText.Get("Select at least one mod before starting translation."), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<TranslationEntry> retryEntries;
            lock (statusLock) retryEntries = failedEntries.Values.Where(entry => Glossary.LanguageKey(entry.SourceLanguage) == Glossary.LanguageKey(settings.SourceLanguage) &&
                Glossary.LanguageKey(entry.TargetLanguage) == Glossary.LanguageKey(settings.TargetLanguage)).ToList();
            if (retryFailed && retryEntries.Count == 0)
            {
                Messages.Message(UiText.Get("No failed translations for this language pair."), MessageTypeDefOf.RejectInput, false);
                return;
            }
            scanSteps?.Dispose();
            scanSteps = null;
            cancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(TranslationLifetime.Token);
            pauseGate.Set();
            SetStatus(TranslationState.Scanning, 0, 0, 0, "Scanning selected mods", string.Empty);

            try
            {
                RuntimeTranslatorSettings taskSettings = settings.Snapshot();
                RuntimeTranslatorSettings scanSettings = taskSettings.Snapshot();
                if (retryFailed) scanSettings.SelectedPackageIds = retryEntries.Select(entry => entry.PackageId).Distinct().ToList();
                if (applyGlossary)
                {
                    taskSettings.SkipCompletedTranslations = false;
                    scanSettings.SelectedPackageIds = LoadedModManager.RunningMods.Where(ModTextScanner.IsPlayerMod).Select(pack => pack.PackageIdPlayerFacing).ToList();
                    scanSettings.SkipModsWithBundledChinese = false;
                }
                var folders = ModTextScanner.SelectedFolders(scanSettings);
                capturedEntries = new List<TranslationEntry>();
                scanSteps = ModTextScanner.CaptureSteps(scanSettings, cancellation.Token).GetEnumerator();
                scanCompletion = new TaskCompletionSource<List<TranslationEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<List<TranslationEntry>> capture = scanCompletion.Task;
                ITranslationProvider provider = ProviderFactory.Create(taskSettings);
                CancellationToken token = cancellation.Token;
                worker = Task.Run(async () =>
                {
                    try
                    {
                        List<TranslationEntry> scanned = ModTextScanner.FinishScan(await capture.ConfigureAwait(false), scanSettings, folders, token);
                        if (applyGlossary) scanned = GlossaryRefreshEntries(taskSettings, scanned);
                        if (retryFailed)
                        {
                            var originals = retryEntries.ToDictionary(entry => entry.Id, entry => entry.SourceText);
                            scanned = scanned.Where(entry => originals.TryGetValue(entry.Id, out string original) && original == entry.SourceText).ToList();
                            taskSettings.SkipCompletedTranslations = false;
                        }
                        token.ThrowIfCancellationRequested();
                        SetStatus(TranslationState.Running, scanned.Count, 0, 0, "Preparing queue", string.Empty);
                        await RunWorker(scanned, provider, taskSettings, token, applyGlossary).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    { SetStatus(TranslationState.Cancelled, 0, 0, 0, "Cancelled", string.Empty); }
                    catch (Exception ex)
                    {
                        SetStatus(TranslationState.Failed, 0, 0, 0, string.Empty, ex.Message);
                        Log.Error("[RuntimeTranslator] Scan failed: " + ex);
                    }
                });
                Messages.Message(UiText.Get("Gesvd’s RimLingo started. Preparing translation queue."), MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception ex)
            {
                scanSteps?.Dispose();
                scanSteps = null;
                scanCompletion?.TrySetException(ex);
                SetStatus(TranslationState.Failed, 0, 0, 0, string.Empty, ex.Message);
                Log.Error("[RuntimeTranslator] Failed to start: " + ex);
            }
        }

        internal static List<TranslationEntry> GlossaryRefreshEntries(RuntimeTranslatorSettings options)
        {
            NativeTranslations.Prepare(options.TargetLanguage);
            RuntimeTranslatorSettings scan = options.Snapshot();
            scan.SelectedPackageIds = LoadedModManager.RunningMods.Where(ModTextScanner.IsPlayerMod).Select(pack => pack.PackageIdPlayerFacing).ToList();
            scan.SkipModsWithBundledChinese = false;
            return GlossaryRefreshEntries(options, ModTextScanner.Scan(scan));
        }

        private static List<TranslationEntry> GlossaryRefreshEntries(RuntimeTranslatorSettings options, List<TranslationEntry> scanned)
        {
            List<GlossaryTerm> terms = Glossary.ForLanguages(options, options.SourceLanguage, options.TargetLanguage);
            List<TranslationEntry> entries = new List<TranslationEntry>();
            HashSet<string> currentSources = new HashSet<string>(scanned.Select(entry =>
                string.Join("|", entry.PackageId, entry.DefTypeName, entry.DefName, entry.FieldName, entry.SourceText)));
            foreach (TranslationCacheRecord record in GetCachedTranslationsSnapshot()
                .Where(record => Glossary.LanguageKey(record.SourceLanguage) == Glossary.LanguageKey(options.SourceLanguage) &&
                    Glossary.LanguageKey(record.TargetLanguage) == Glossary.LanguageKey(options.TargetLanguage))
                .GroupBy(record => record.Id).Select(group => group.OrderByDescending(record => record.UpdatedUtcTicks).First()))
            {
                if (!TextProtection.HasVisibleText(record.TranslatedText) || string.IsNullOrEmpty(record.SourceText) ||
                    !currentSources.Contains(string.Join("|", record.PackageId, record.DefTypeName, record.DefName, record.FieldName, record.SourceText))) continue;
                string hash = Glossary.SourceHash(record.SourceText, terms);
                if (hash == TextHasher.Sha1(record.SourceText)) continue;
                TranslationEntry entry = record.ToEntry();
                entry.SourceHash = hash;
                entry.TranslatedText = null;
                if (!NativeTranslations.Contains(entry)) entries.Add(entry);
            }
            return entries;
        }

        public static void Pause()
        {
            lock (statusLock)
            {
                if (worker == null || worker.IsCompleted || status.State != TranslationState.Running) return;
                pauseGate.Reset();
                status.State = TranslationState.Paused;
            }
        }

        public static void Resume()
        {
            lock (statusLock)
            {
                if (worker == null || worker.IsCompleted || status.State != TranslationState.Paused) return;
                pauseGate.Set();
                status.State = TranslationState.Running;
            }
        }

        public static void Cancel()
        {
            lock (statusLock)
            {
                if (cancellation == null || worker == null || worker.IsCompleted ||
                    (status.State != TranslationState.Running && status.State != TranslationState.Paused && status.State != TranslationState.Scanning)) return;
                status.State = TranslationState.Cancelling;
            }
            cancellation.Cancel();
            scanCompletion?.TrySetCanceled();
            pauseGate.Set();
        }

        public static void Tick()
        {
            if (TranslationLifetime.IsStopping) return;
            AdvanceScan();
            List<Action> notifications = new List<Action>();
            lock (pendingLock)
            {
                while (pendingMainThreadActions.Count > 0 && notifications.Count < 20)
                {
                    notifications.Add(pendingMainThreadActions.Dequeue());
                }
            }
            foreach (Action notification in notifications)
            {
                if (!TranslationLifetime.IsStopping) notification();
            }

            Stopwatch timer = Stopwatch.StartNew();
            for (int count = 0; count < 200 && timer.Elapsed.TotalMilliseconds < 2; count++)
            {
                TranslationEntry entry;
                lock (pendingLock)
                {
                    if (pendingApply.Count == 0) break;
                    entry = pendingApply.Dequeue();
                }
                RuntimeTextApplier.Apply(entry);
            }
        }

        private static void AdvanceScan()
        {
            if (scanSteps == null) return;
            try
            {
                if (cancellation.IsCancellationRequested) { scanCompletion.TrySetCanceled(); return; }
                Stopwatch timer = Stopwatch.StartNew();
                for (int count = 0; count < 200 && timer.Elapsed.TotalMilliseconds < 2; count++)
                {
                    if (!scanSteps.MoveNext()) { scanCompletion.TrySetResult(capturedEntries); return; }
                    if (scanSteps.Current != null) capturedEntries.Add(scanSteps.Current);
                }
                UpdateProgress(capturedEntries.Count, 0, 0, "Scanning selected mods");
            }
            catch (Exception ex) { scanCompletion.TrySetException(ex); }
            finally
            {
                if (scanCompletion.Task.IsCompleted)
                {
                    scanSteps.Dispose();
                    scanSteps = null;
                    capturedEntries = null;
                }
            }
        }

        public static void SaveCache()
        {
            if (TranslationLifetime.IsStopping) return;
            lock (saveLock)
            {
                TranslationCacheStore store;
                List<TranslationCacheRecord> snapshot;
                long revision;
                lock (cacheLock)
                {
                    store = cache;
                    if (store == null || !store.IsDirty || TranslationLifetime.IsStopping) return;
                    revision = store.Revision;
                    snapshot = store.Records.Select(CloneRecord).ToList();
                }
                if (store.SaveSnapshot(snapshot))
                {
                    lock (cacheLock) store.MarkSaved(revision);
                }
                else
                {
                    UpdateError("Could not save translation cache. The previous file was preserved.");
                    Notify("Gesvd’s RimLingo could not save its cache. See the log for details.", false);
                }
            }
        }

        public static long CacheRevision { get { lock (cacheLock) return cache?.Revision ?? -1; } }

        public static void ExportCurrentCache()
        {
            if (cache == null || settings == null)
            {
                return;
            }

            if (!TranslationLifetime.IsStopping)
            {
                NativeTranslations.Prepare(settings.TargetLanguage);
                LanguageFileExporter.Export(GetCachedTranslationsSnapshot(), settings.TargetLanguage);
            }
        }

        public static List<TranslationCacheRecord> GetCachedTranslationsSnapshot()
        {
            if (cache == null)
            {
                return new List<TranslationCacheRecord>();
            }

            lock (cacheLock)
            {
                return cache.Records
                    .Select(CloneRecord)
                    .ToList();
            }
        }

        public static void UpdateCachedTranslation(TranslationCacheRecord record, string translatedText)
        {
            if (cache == null || record == null)
            {
                return;
            }

            TranslationCacheRecord updated = null;
            lock (cacheLock)
            {
                if (cache.TryUpdate(record.Id, record.SourceHash, record.Provider, translatedText, out updated))
                {
                    if (settings.ApplyRuntimeTranslations)
                    {
                        QueueApply(updated.ToEntry());
                    }
                }
            }
        }

        public static void SetManualLock(TranslationCacheRecord record, bool locked)
        {
            if (record == null || cache == null) return;
            lock (cacheLock) cache.SetManual(record.Id, record.SourceHash, record.Provider, locked);
        }

        public static int ReplaceCachedTranslationsBatch(IEnumerable<string> visibleRecordKeys, string oldText, string newText, int batchSize, ISet<string> processedKeys)
        {
            if (cache == null || string.IsNullOrEmpty(oldText) || oldText == (newText ?? string.Empty))
            {
                return 0;
            }

            HashSet<string> keys = new HashSet<string>(visibleRecordKeys ?? Enumerable.Empty<string>());
            List<TranslationCacheRecord> changed = new List<TranslationCacheRecord>();
            int count = 0;

            lock (cacheLock)
            {
                foreach (TranslationCacheRecord record in cache.Records
                    .Where(record => keys.Contains(CacheRecordKey(record)) && !processedKeys.Contains(CacheRecordKey(record)) &&
                        (record.TranslatedText ?? string.Empty).Contains(oldText)))
                {
                    if (count >= Math.Max(1, batchSize))
                    {
                        break;
                    }

                    cache.TryUpdate(record.Id, record.SourceHash, record.Provider, record.TranslatedText.Replace(oldText, newText ?? string.Empty), out _);
                    processedKeys.Add(CacheRecordKey(record));
                    changed.Add(CloneRecord(record));
                    count++;
                }

            }
            if (count > 0) SaveCache();

            foreach (TranslationCacheRecord record in changed)
            {
                if (settings.ApplyRuntimeTranslations)
                {
                    QueueApply(record.ToEntry());
                }
            }

            return count;
        }

        public static string CacheRecordKey(TranslationCacheRecord record)
        {
            return (record.Id ?? string.Empty) + "|" + (record.SourceHash ?? string.Empty) + "|" + (record.Provider ?? string.Empty);
        }

        private static async Task RunWorker(List<TranslationEntry> scanned, ITranslationProvider provider, RuntimeTranslatorSettings taskSettings, CancellationToken token, bool applyGlossary = false)
        {
            int completed = 0;
            int failed = 0;
            int skipped = 0;
            int total = scanned.Count;
            lock (statusLock) { failedEntries.Clear(); failuresFromGlossary = applyGlossary; }
            try
            {
                string providerName = provider.DisplayName;
                Dictionary<Tuple<string, string>, TranslationCacheRecord> manual;
                lock (cacheLock) manual = cache.Records.Where(record => record.IsManual).GroupBy(record => Tuple.Create(record.Id, record.SourceText))
                    .ToDictionary(group => group.Key, group => CloneRecord(group.OrderByDescending(record => record.UpdatedUtcTicks).First()));
                Dictionary<string, TranslationCacheRecord> completedCache = new Dictionary<string, TranslationCacheRecord>();
                if (taskSettings.SkipCompletedTranslations)
                {
                    lock (cacheLock)
                    {
                        completedCache = cache.Records.GroupBy(record => record.Id + "|" + record.SourceHash)
                            .Select(group => group.OrderByDescending(record => record.Provider == providerName)
                                .ThenByDescending(record => record.UpdatedUtcTicks).First())
                            .Where(record => TextProtection.HasVisibleText(record.TranslatedText))
                            .ToDictionary(record => record.Id + "|" + record.SourceHash, CloneRecord);
                    }
                }
                List<TranslationEntry> pending = new List<TranslationEntry>();
                foreach (TranslationEntry entry in scanned)
                {
                    token.ThrowIfCancellationRequested();
                    if (NativeTranslations.Contains(entry)) { skipped++; continue; }
                    if (manual.TryGetValue(Tuple.Create(entry.Id, entry.SourceText), out TranslationCacheRecord edited))
                    {
                        skipped++;
                        if (taskSettings.ApplyRuntimeTranslations) QueueApply(edited.ToEntry());
                        continue;
                    }
                    TranslationCacheRecord cached;
                    completedCache.TryGetValue(entry.Id + "|" + entry.SourceHash, out cached);
                    if (cached != null && TextProtection.HasVisibleText(cached.TranslatedText))
                    {
                        skipped++;
                        if (taskSettings.ApplyRuntimeTranslations) QueueApply(cached.ToEntry());
                    }
                    else
                    {
                        pending.Add(entry);
                    }
                }

                total = pending.Count;
                UpdateProgress(total, completed, failed, "Skipped completed: " + skipped, skipped);
                for (int offset = 0; offset < pending.Count;)
                {
                    token.ThrowIfCancellationRequested();
                    pauseGate.Wait(token);
                    List<TranslationEntry> batch = TranslationBatches.Take(pending, offset, TranslationBatches.Size(provider, CurrentBatchSize));
                    offset += batch.Count;
                    UpdateProgress(total, completed, failed, batch.First().DefName, skipped);
                    try
                    {
                        TranslationBatchRequest request = new TranslationBatchRequest
                        {
                            Entries = batch,
                            SourceLanguage = taskSettings.SourceLanguage,
                            TargetLanguage = taskSettings.TargetLanguage
                        };
                        TranslationBatchResult result = await TranslationBatches.TranslateAsync(provider, request, token, partial =>
                        {
                            foreach (TranslationEntry entry in batch)
                            {
                                if (!partial.Translations.TryGetValue(entry.Id, out string translated) || !TextProtection.HasVisibleText(translated)) continue;
                                entry.TranslatedText = translated;
                                bool accepted;
                                lock (cacheLock) accepted = cache.Put(entry, providerName, applyGlossary);
                                if (!accepted) { skipped++; total--; continue; }
                                if (taskSettings.ApplyRuntimeTranslations) QueueApply(entry);
                                completed++;
                            }
                            SaveCache();
                            UpdateProgress(total, completed, failed, batch.First().DefName, skipped);
                        }).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        foreach (TranslationEntry entry in batch)
                        {
                            if (!result.Translations.TryGetValue(entry.Id, out string translated) || !TextProtection.HasVisibleText(translated))
                            {
                                failed++;
                                lock (statusLock) failedEntries[entry.Id] = entry;
                                if (result.Errors.TryGetValue(entry.Id, out string error)) UpdateError(error);
                            }
                        }
                        SaveCache();
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed += batch.Count;
                        lock (statusLock) foreach (TranslationEntry entry in batch) failedEntries[entry.Id] = entry;
                        UpdateError(ex.Message);
                        Log.Warning("[RuntimeTranslator] Batch failed: " + ex.Message);
                    }
                }

                token.ThrowIfCancellationRequested();
                if (taskSettings.ExportLanguageFiles)
                {
                    LanguageFileExporter.Export(GetCachedTranslationsSnapshot(), taskSettings.TargetLanguage, token);
                }
                token.ThrowIfCancellationRequested();
                SetStatus(TranslationState.Completed, total, completed, failed, "Done", Status.LastError, skipped);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                SetStatus(TranslationState.Cancelled, total, completed, failed, "Cancelled", Status.LastError, skipped);
            }
            catch (Exception ex)
            {
                SetStatus(TranslationState.Failed, total, completed, failed, string.Empty, ex.Message, skipped);
                Log.Error("[RuntimeTranslator] Translation task failed: " + ex);
            }
        }

        internal static void Notify(string message, bool success, string detail = "")
        {
            OnMainThread(() => Messages.Message(UiText.Get(message) + detail,
                success ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, false));
        }

        internal static void OnMainThread(Action action)
        {
            if (TranslationLifetime.IsStopping) return;
            lock (pendingLock)
            {
                if (!TranslationLifetime.IsStopping) pendingMainThreadActions.Enqueue(action);
            }
        }

        public static void Shutdown()
        {
            TranslationLifetime.Stop();
            scanCompletion?.TrySetCanceled();
            scanSteps?.Dispose();
            scanSteps = null;
            capturedEntries = null;
            BundledLibreTranslate.Stop();
            pauseGate.Set();
            lock (pendingLock)
            {
                pendingApply.Clear();
                pendingMainThreadActions.Clear();
            }
            // Previously saved batches survive; never join workers or wait for cache I/O here.
        }

        internal static int CurrentBatchSize
        {
            get { return Math.Max(1, Volatile.Read(ref settings.BatchSize)); }
        }

        private static void QueueApply(TranslationEntry entry)
        {
            lock (pendingLock)
            {
                if (!TranslationLifetime.IsStopping) pendingApply.Enqueue(entry);
            }
        }

        private static void SetStatus(TranslationState state, int total, int completed, int failed, string current, string error, int skipped = 0)
        {
            lock (statusLock)
            {
                status.State = state;
                status.Total = total;
                status.Completed = completed;
                status.Failed = failed;
                status.Skipped = skipped;
                status.CurrentItem = current ?? string.Empty;
                status.LastError = error ?? string.Empty;
            }
        }

        private static void UpdateProgress(int total, int completed, int failed, string current, int skipped = 0)
        {
            lock (statusLock)
            {
                status.Total = total;
                status.Completed = completed;
                status.Failed = failed;
                status.Skipped = skipped;
                status.CurrentItem = current ?? string.Empty;
            }
        }

        private static void UpdateError(string error)
        {
            lock (statusLock)
            {
                status.LastError = error ?? string.Empty;
            }
        }

        private static TranslationCacheRecord CloneRecord(TranslationCacheRecord record)
        {
            return new TranslationCacheRecord
            {
                Id = record.Id,
                PackageId = record.PackageId,
                ModName = record.ModName,
                DefTypeName = record.DefTypeName,
                DefName = record.DefName,
                FieldName = record.FieldName,
                SourceLanguage = record.SourceLanguage,
                TargetLanguage = record.TargetLanguage,
                SourceHash = record.SourceHash,
                SourceText = record.SourceText,
                TranslatedText = record.TranslatedText,
                Provider = record.Provider,
                IsManual = record.IsManual,
                UpdatedUtcTicks = record.UpdatedUtcTicks
            };
        }
    }
}
